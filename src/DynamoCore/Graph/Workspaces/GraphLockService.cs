using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json;

namespace Dynamo.Graph.Workspaces
{
    internal enum GraphLockAcquisitionStatus
    {
        Acquired,
        LockedByLiveSession,
        StaleLock,
        LockUnavailable
    }

    internal sealed class GraphLockAcquisitionResult
    {
        internal GraphLockAcquisitionResult(
            GraphLockAcquisitionStatus status,
            string graphPath,
            string lockFilePath,
            GraphLockData existingLock = null,
            Exception exception = null)
        {
            Status = status;
            GraphPath = graphPath;
            LockFilePath = lockFilePath;
            ExistingLock = existingLock;
            Exception = exception;
        }

        internal GraphLockAcquisitionStatus Status { get; }

        internal string GraphPath { get; }

        internal string LockFilePath { get; }

        internal GraphLockData ExistingLock { get; }

        internal Exception Exception { get; }

        internal bool LockAcquired => Status == GraphLockAcquisitionStatus.Acquired;
    }

    internal sealed class GraphLockData
    {
        [JsonProperty("schemaVersion")]
        internal int SchemaVersion { get; set; }

        [JsonProperty("graphPath")]
        internal string GraphPath { get; set; }

        [JsonProperty("userName")]
        internal string UserName { get; set; }

        [JsonProperty("hostName")]
        internal string HostName { get; set; }

        [JsonProperty("processId")]
        internal int ProcessId { get; set; }

        [JsonProperty("processStartTimeUtc")]
        internal DateTime ProcessStartTimeUtc { get; set; }

        [JsonProperty("dynamoVersion")]
        internal string DynamoVersion { get; set; }

        [JsonProperty("dynamoMajorMinorVersion")]
        internal string DynamoMajorMinorVersion { get; set; }

        [JsonProperty("dynamoBuild")]
        internal string DynamoBuild { get; set; }

        [JsonProperty("lastHeartbeatUtc")]
        internal DateTime LastHeartbeatUtc { get; set; }

        [JsonProperty("sessionId")]
        internal Guid SessionId { get; set; }
    }

    internal sealed class GraphLockService : IDisposable
    {
        internal const int CurrentSchemaVersion = 1;
        internal static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

        private readonly object lockObject = new object();
        private readonly Dictionary<string, GraphLockData> ownedLocks;
        private readonly StringComparer pathComparer;
        private readonly Guid sessionId;
        private readonly int processId;
        private readonly DateTime processStartTimeUtc;
        private readonly string userName;
        private readonly string hostName;
        private readonly string dynamoVersion;
        private readonly string dynamoMajorMinorVersion;
        private readonly string dynamoBuild;
        private readonly TimeSpan heartbeatInterval;
        private readonly TimeSpan staleHeartbeatThreshold;
        private readonly bool unregisterProcessExit;
        private Timer heartbeatTimer;
        private bool disposed;

        internal GraphLockService(
            string dynamoVersion = null,
            string dynamoBuild = null,
            TimeSpan? heartbeatInterval = null,
            int staleHeartbeatMultiplier = 5,
            bool registerProcessExit = true)
        {
            this.heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
            staleHeartbeatThreshold = TimeSpan.FromTicks(this.heartbeatInterval.Ticks * Math.Max(1, staleHeartbeatMultiplier));
            pathComparer = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            ownedLocks = new Dictionary<string, GraphLockData>(pathComparer);
            sessionId = Guid.NewGuid();
            userName = Environment.UserName;
            hostName = Environment.MachineName;
            this.dynamoVersion = string.IsNullOrWhiteSpace(dynamoVersion) ? GetAssemblyVersion() : dynamoVersion;
            this.dynamoMajorMinorVersion = GetMajorMinorVersion(this.dynamoVersion);
            this.dynamoBuild = string.IsNullOrWhiteSpace(dynamoBuild) ? GetAssemblyInformationalVersion() : dynamoBuild;

            using (var process = Process.GetCurrentProcess())
            {
                processId = process.Id;
                processStartTimeUtc = GetProcessStartTimeUtc(process);
            }

            if (registerProcessExit)
            {
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
                unregisterProcessExit = true;
            }
        }

        internal GraphLockAcquisitionResult TryAcquireLock(string graphPath, bool force = false)
        {
            if (string.IsNullOrWhiteSpace(graphPath))
            {
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.LockUnavailable, graphPath, null);
            }

            var canonicalGraphPath = GetCanonicalGraphPath(graphPath);
            var lockFilePath = GetLockFilePath(canonicalGraphPath);

            lock (lockObject)
            {
                if (ownedLocks.ContainsKey(canonicalGraphPath))
                {
                    return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.Acquired, canonicalGraphPath, lockFilePath);
                }
            }

            if (force)
            {
                return CreateOrOverwriteLock(canonicalGraphPath, lockFilePath);
            }

            try
            {
                var lockData = CreateLockData(canonicalGraphPath);
                using (var fileStream = new FileStream(lockFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var streamWriter = new StreamWriter(fileStream))
                using (var jsonWriter = new JsonTextWriter(streamWriter))
                {
                    CreateSerializer().Serialize(jsonWriter, lockData);
                }

                AddOwnedLock(canonicalGraphPath, lockData);
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.Acquired, canonicalGraphPath, lockFilePath);
            }
            catch (IOException)
            {
                if (!File.Exists(lockFilePath))
                {
                    return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.LockUnavailable, canonicalGraphPath, lockFilePath);
                }

                return EvaluateExistingLock(canonicalGraphPath, lockFilePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.LockUnavailable, canonicalGraphPath, lockFilePath, exception: ex);
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is DirectoryNotFoundException)
            {
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.LockUnavailable, canonicalGraphPath, lockFilePath, exception: ex);
            }
        }

        internal void ReleaseLock(string graphPath)
        {
            if (string.IsNullOrWhiteSpace(graphPath))
            {
                return;
            }

            GraphLockData lockData;
            var canonicalGraphPath = GetCanonicalGraphPath(graphPath);

            lock (lockObject)
            {
                if (!ownedLocks.TryGetValue(canonicalGraphPath, out lockData))
                {
                    return;
                }

                ownedLocks.Remove(canonicalGraphPath);
                StopHeartbeatTimerIfNeeded();
            }

            DeleteLockFileIfOwned(lockData);
        }

        internal void ReleaseAllLocks()
        {
            List<GraphLockData> locksToRelease;
            lock (lockObject)
            {
                locksToRelease = ownedLocks.Values.ToList();
                ownedLocks.Clear();
                StopHeartbeatTimerIfNeeded();
            }

            foreach (var lockData in locksToRelease)
            {
                DeleteLockFileIfOwned(lockData);
            }
        }

        internal static string GetLockFilePath(string graphPath)
        {
            var directory = Path.GetDirectoryName(graphPath);
            var fileName = Path.GetFileName(graphPath);
            return Path.Combine(directory, "." + fileName + ".lock");
        }

        internal static string GetCanonicalGraphPath(string graphPath)
        {
            return Path.GetFullPath(graphPath);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (unregisterProcessExit)
            {
                AppDomain.CurrentDomain.ProcessExit -= CurrentDomain_ProcessExit;
            }

            ReleaseAllLocks();
            heartbeatTimer?.Dispose();
            disposed = true;
        }

        private GraphLockAcquisitionResult CreateOrOverwriteLock(string canonicalGraphPath, string lockFilePath)
        {
            try
            {
                var lockData = CreateLockData(canonicalGraphPath);
                WriteLockData(lockFilePath, lockData);
                AddOwnedLock(canonicalGraphPath, lockData);
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.Acquired, canonicalGraphPath, lockFilePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is DirectoryNotFoundException)
            {
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.LockUnavailable, canonicalGraphPath, lockFilePath, exception: ex);
            }
        }

        private GraphLockAcquisitionResult EvaluateExistingLock(string canonicalGraphPath, string lockFilePath)
        {
            var existingLock = ReadLockData(lockFilePath);
            if (existingLock == null)
            {
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.StaleLock, canonicalGraphPath, lockFilePath);
            }

            if (existingLock.SessionId == sessionId)
            {
                AddOwnedLock(canonicalGraphPath, existingLock);
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.Acquired, canonicalGraphPath, lockFilePath);
            }

            if (IsStale(existingLock) || IsDeadLocalProcess(existingLock))
            {
                return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.StaleLock, canonicalGraphPath, lockFilePath, existingLock);
            }

            return new GraphLockAcquisitionResult(GraphLockAcquisitionStatus.LockedByLiveSession, canonicalGraphPath, lockFilePath, existingLock);
        }

        private GraphLockData CreateLockData(string canonicalGraphPath)
        {
            return new GraphLockData
            {
                SchemaVersion = CurrentSchemaVersion,
                GraphPath = canonicalGraphPath,
                UserName = userName,
                HostName = hostName,
                ProcessId = processId,
                ProcessStartTimeUtc = processStartTimeUtc,
                DynamoVersion = dynamoVersion,
                DynamoMajorMinorVersion = dynamoMajorMinorVersion,
                DynamoBuild = dynamoBuild,
                LastHeartbeatUtc = DateTime.UtcNow,
                SessionId = sessionId
            };
        }

        private void AddOwnedLock(string canonicalGraphPath, GraphLockData lockData)
        {
            lock (lockObject)
            {
                ownedLocks[canonicalGraphPath] = lockData;
                EnsureHeartbeatTimer();
            }
        }

        private void EnsureHeartbeatTimer()
        {
            if (heartbeatTimer == null)
            {
                heartbeatTimer = new Timer(OnHeartbeat, null, heartbeatInterval, heartbeatInterval);
            }
            else
            {
                heartbeatTimer.Change(heartbeatInterval, heartbeatInterval);
            }
        }

        private void StopHeartbeatTimerIfNeeded()
        {
            if (ownedLocks.Count == 0 && heartbeatTimer != null)
            {
                heartbeatTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        private void OnHeartbeat(object state)
        {
            List<KeyValuePair<string, GraphLockData>> locksToUpdate;
            lock (lockObject)
            {
                locksToUpdate = ownedLocks.ToList();
            }

            foreach (var lockEntry in locksToUpdate)
            {
                var lockFilePath = GetLockFilePath(lockEntry.Key);
                var existingLock = ReadLockData(lockFilePath);
                if (existingLock == null || existingLock.SessionId != sessionId)
                {
                    lock (lockObject)
                    {
                        ownedLocks.Remove(lockEntry.Key);
                        StopHeartbeatTimerIfNeeded();
                    }
                    continue;
                }

                existingLock.LastHeartbeatUtc = DateTime.UtcNow;
                try
                {
                    WriteLockData(lockFilePath, existingLock);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Debug.WriteLine(ex.Message);
                    continue;
                }

                lock (lockObject)
                {
                    if (ownedLocks.ContainsKey(lockEntry.Key))
                    {
                        ownedLocks[lockEntry.Key] = existingLock;
                    }
                }
            }
        }

        private void DeleteLockFileIfOwned(GraphLockData lockData)
        {
            try
            {
                var lockFilePath = GetLockFilePath(lockData.GraphPath);
                var existingLock = ReadLockData(lockFilePath);
                if (existingLock?.SessionId == sessionId && File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                }
            }
            catch (IOException ex)
            {
                Debug.WriteLine(ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private bool IsStale(GraphLockData lockData)
        {
            return DateTime.UtcNow - lockData.LastHeartbeatUtc > staleHeartbeatThreshold;
        }

        private bool IsDeadLocalProcess(GraphLockData lockData)
        {
            if (!string.Equals(lockData.HostName, hostName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var process = Process.GetProcessById(lockData.ProcessId))
                {
                    var startTimeUtc = GetProcessStartTimeUtc(process);
                    return startTimeUtc != lockData.ProcessStartTimeUtc;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return false;
            }
        }

        private static GraphLockData ReadLockData(string lockFilePath)
        {
            const int maxAttempts = 2;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using (var fileStream = new FileStream(lockFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var streamReader = new StreamReader(fileStream))
                    using (var jsonReader = new JsonTextReader(streamReader))
                    {
                        return CreateSerializer().Deserialize<GraphLockData>(jsonReader);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is JsonException)
                {
                    if (attempt == maxAttempts - 1)
                    {
                        return null;
                    }

                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException)
                {
                    return null;
                }
            }

            return null;
        }

        private static void WriteLockData(string lockFilePath, GraphLockData lockData)
        {
            using (var fileStream = new FileStream(lockFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var streamWriter = new StreamWriter(fileStream))
            using (var jsonWriter = new JsonTextWriter(streamWriter))
            {
                jsonWriter.Formatting = Formatting.Indented;
                CreateSerializer().Serialize(jsonWriter, lockData);
            }
        }

        private static JsonSerializer CreateSerializer()
        {
            return JsonSerializer.Create(new JsonSerializerSettings
            {
                Culture = CultureInfo.InvariantCulture,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore
            });
        }

        private static DateTime GetProcessStartTimeUtc(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                return DateTime.MinValue;
            }
        }

        private static string GetMajorMinorVersion(string version)
        {
            Version parsedVersion;
            return Version.TryParse(version, out parsedVersion)
                ? parsedVersion.ToString(2)
                : version;
        }

        private static string GetAssemblyVersion()
        {
            return typeof(GraphLockService).Assembly.GetName().Version?.ToString() ?? string.Empty;
        }

        private static string GetAssemblyInformationalVersion()
        {
            var attribute = typeof(GraphLockService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attribute?.InformationalVersion ?? GetAssemblyVersion();
        }

        private static bool IsWindows()
        {
            var platform = Environment.OSVersion.Platform;
            return platform == PlatformID.Win32NT ||
                   platform == PlatformID.Win32S ||
                   platform == PlatformID.Win32Windows ||
                   platform == PlatformID.WinCE;
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            ReleaseAllLocks();
        }
    }
}
