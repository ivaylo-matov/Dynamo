using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using Dynamo.Models;

namespace Dynamo.Graph.Workspaces.Locking
{
    /// <summary>
    /// Coordinates graph lock acquisition, heartbeat, and release for a Dynamo model.
    /// </summary>
    internal sealed class GraphLockManager : IDisposable
    {
        internal const int DefaultHeartbeatMilliseconds = 30000;
        private const int StaleFactor = 5;

        private readonly DynamoModel dynamoModel;
        private readonly StringComparer pathComparer;
        private readonly ConcurrentDictionary<string, OwnedLock> locks;
        private readonly ConcurrentDictionary<string, byte> openingPaths;
        private readonly Guid sessionId;
        private readonly int processId;
        private readonly DateTime processStartTimeUtc;
        private readonly string machineName;
        private readonly int heartbeatMilliseconds;
        private readonly bool enabled;
        private Timer heartbeatTimer;
        private IGraphLockUserPrompt prompt;
        private bool disposed;

        private sealed class OwnedLock
        {
            internal string SidecarPath { get; set; }
            internal GraphLockInfo Info { get; set; }
            internal WorkspaceModel Workspace { get; set; }
        }

        /// <summary>
        /// Initializes a graph lock manager for a Dynamo model.
        /// </summary>
        /// <param name="dynamoModel">The Dynamo model whose workspaces are tracked.</param>
        /// <param name="prompt">The UI prompt used when a graph lock conflict is found.</param>
        /// <param name="heartbeatMilliseconds">The heartbeat interval for owned locks.</param>
        /// <param name="forceEnable">True to enable locking in modes that normally skip it.</param>
        internal GraphLockManager(
            DynamoModel dynamoModel,
            IGraphLockUserPrompt prompt = null,
            int heartbeatMilliseconds = DefaultHeartbeatMilliseconds,
            bool forceEnable = false)
        {
            this.dynamoModel = dynamoModel ?? throw new ArgumentNullException(nameof(dynamoModel));
            this.prompt = prompt;
            this.heartbeatMilliseconds = heartbeatMilliseconds;
            pathComparer = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            locks = new ConcurrentDictionary<string, OwnedLock>(pathComparer);
            openingPaths = new ConcurrentDictionary<string, byte>(pathComparer);
            sessionId = Guid.NewGuid();
            machineName = Environment.MachineName;

            using (var process = Process.GetCurrentProcess())
            {
                processId = process.Id;
                processStartTimeUtc = GetProcessStartTimeUtc(process);
            }

            enabled = forceEnable || (!DynamoModel.IsTestMode && !DynamoModel.IsHeadless && !dynamoModel.IsServiceMode);
            if (!enabled)
            {
                return;
            }

            dynamoModel.WorkspaceAdded += OnWorkspaceAdded;
            dynamoModel.WorkspaceRemoveStarted += OnWorkspaceRemoveStarted;
            dynamoModel.WorkspaceRemoved += OnWorkspaceRemoved;
            dynamoModel.WorkspaceClearingStarted += OnWorkspaceClearingStarted;
            dynamoModel.ShutdownStarted += OnShutdownStarted;

            AppDomain.CurrentDomain.ProcessExit += ReleaseAll;
            AppDomain.CurrentDomain.UnhandledException += ReleaseAll;

            heartbeatTimer = new Timer(OnHeartbeat, null, this.heartbeatMilliseconds, this.heartbeatMilliseconds);
        }

        /// <summary>
        /// Sets the UI prompt used when a graph lock conflict is detected.
        /// </summary>
        /// <param name="userPrompt">The prompt implementation, or null to cancel conflicts silently.</param>
        internal void SetPrompt(IGraphLockUserPrompt userPrompt)
        {
            prompt = userPrompt;
        }

        /// <summary>
        /// Attempts to acquire a graph lock before opening a graph file.
        /// </summary>
        /// <param name="graphPath">The graph path requested by the user.</param>
        /// <param name="allowPromptUI">True to allow user interaction when a conflict is found.</param>
        /// <returns>The lock acquisition result and graph path to open.</returns>
        internal GraphLockAcquireResult TryAcquire(string graphPath, bool allowPromptUI)
        {
            if (!enabled || string.IsNullOrEmpty(graphPath))
            {
                return GraphLockAcquireResult.Acquired(graphPath);
            }

            var normalizedPath = NormalizePath(graphPath);
            openingPaths[normalizedPath] = 0;

            var result = TryAcquireCore(normalizedPath, allowPromptUI, null);
            if (!IsSamePath(normalizedPath, result.GraphPath))
            {
                openingPaths.TryRemove(normalizedPath, out _);
            }

            return result;
        }

        /// <summary>
        /// Completes a graph open attempt and releases the lock if opening failed.
        /// </summary>
        /// <param name="graphPath">The graph path that was opened.</param>
        /// <param name="succeeded">Whether the graph opened successfully.</param>
        internal void CompleteOpen(string graphPath, bool succeeded)
        {
            if (!enabled || string.IsNullOrEmpty(graphPath))
            {
                return;
            }

            var normalizedPath = NormalizePath(graphPath);
            openingPaths.TryRemove(normalizedPath, out _);

            if (!succeeded)
            {
                Release(normalizedPath);
            }
        }

        /// <summary>
        /// Releases the lock for a graph path.
        /// </summary>
        /// <param name="graphPath">The graph path whose lock should be released.</param>
        internal void Release(string graphPath)
        {
            if (!enabled || string.IsNullOrEmpty(graphPath))
            {
                return;
            }

            var normalizedPath = NormalizePath(graphPath);
            if (openingPaths.ContainsKey(normalizedPath))
            {
                return;
            }

            if (locks.TryRemove(normalizedPath, out var owned))
            {
                ReleaseOwnedLock(normalizedPath, owned);
            }
        }

        /// <summary>
        /// Releases every lock owned by this manager.
        /// </summary>
        /// <param name="sender">Optional event sender.</param>
        /// <param name="args">Optional event arguments.</param>
        internal void ReleaseAll(object sender = null, EventArgs args = null)
        {
            foreach (var path in locks.Keys.ToList())
            {
                Release(path);
            }

            heartbeatTimer?.Dispose();
            heartbeatTimer = null;
        }

        // Performs the actual sidecar creation/read conflict flow for a normalized graph path.
        private GraphLockAcquireResult TryAcquireCore(string normalizedPath, bool allowPromptUI, WorkspaceModel workspace)
        {
            var sidecarPath = GraphLockFile.PathFor(normalizedPath);
            var info = BuildSelfInfo(normalizedPath);
            var attempt = 0;

            while (attempt < 2)
            {
                attempt++;
                try
                {
                    if (GraphLockFile.TryCreateExclusive(sidecarPath, info))
                    {
                        RegisterOwnedLock(normalizedPath, sidecarPath, info, workspace);
                        return GraphLockAcquireResult.Acquired(normalizedPath);
                    }

                    GraphLockInfo existingLock;
                    var readable = GraphLockFile.TryRead(sidecarPath, out existingLock);
                    var isStale = !readable || IsStale(existingLock) || IsDeadLocalProcess(existingLock);

                    if (readable && IsSelf(existingLock))
                    {
                        RegisterOwnedLock(normalizedPath, sidecarPath, existingLock, workspace);
                        return GraphLockAcquireResult.Acquired(normalizedPath);
                    }

                    var response = PromptIfAllowed(normalizedPath, readable ? existingLock : null, isStale, allowPromptUI);
                    if (response.ShouldSaveAs)
                    {
                        return TryCopyToSaveAsPath(normalizedPath, response.SaveAsPath, workspace, existingLock);
                    }

                    return GraphLockAcquireResult.Cancelled(existingLock);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log("GraphLock unavailable: " + ex.Message);
                }
                catch (SecurityException ex)
                {
                    Log("GraphLock unavailable: " + ex.Message);
                }
                catch (IOException ex)
                {
                    Log("GraphLock unavailable: " + ex.Message);
                }
            }

            return GraphLockAcquireResult.Unavailable(normalizedPath);
        }

        // Copies a locked graph to a user-selected path and locks that copy before opening.
        private GraphLockAcquireResult TryCopyToSaveAsPath(
            string sourcePath,
            string saveAsPath,
            WorkspaceModel workspace,
            GraphLockInfo existingLock)
        {
            if (string.IsNullOrWhiteSpace(saveAsPath))
            {
                return GraphLockAcquireResult.Cancelled(existingLock);
            }

            var normalizedSaveAsPath = NormalizePath(saveAsPath);
            if (IsSamePath(sourcePath, normalizedSaveAsPath))
            {
                return GraphLockAcquireResult.Cancelled(existingLock);
            }

            var sidecarPath = GraphLockFile.PathFor(normalizedSaveAsPath);
            var info = BuildSelfInfo(normalizedSaveAsPath);
            var ownsSaveAsLock = false;

            try
            {
                if (GraphLockFile.TryCreateExclusive(sidecarPath, info))
                {
                    ownsSaveAsLock = true;
                }
                else
                {
                    GraphLockInfo saveAsLock;
                    var readable = GraphLockFile.TryRead(sidecarPath, out saveAsLock);
                    if (readable && IsSelf(saveAsLock))
                    {
                        info = saveAsLock;
                        ownsSaveAsLock = true;
                    }
                    else if (!readable || IsStale(saveAsLock) || IsDeadLocalProcess(saveAsLock))
                    {
                        GraphLockFile.WriteHeartbeat(sidecarPath, info);
                        ownsSaveAsLock = true;
                    }
                    else
                    {
                        return GraphLockAcquireResult.Cancelled(saveAsLock);
                    }
                }

                File.Copy(sourcePath, normalizedSaveAsPath, true);
                openingPaths[normalizedSaveAsPath] = 0;
                RegisterOwnedLock(normalizedSaveAsPath, sidecarPath, info, workspace);
                return GraphLockAcquireResult.Acquired(normalizedSaveAsPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                Log("GraphLock save-as copy failed: " + ex.Message);
                if (ownsSaveAsLock)
                {
                    ReleaseOwnedLock(normalizedSaveAsPath, new OwnedLock { SidecarPath = sidecarPath, Info = info });
                }

                return GraphLockAcquireResult.Cancelled(existingLock);
            }
        }

        // Tracks a lock that this Dynamo process owns.
        private void RegisterOwnedLock(string normalizedPath, string sidecarPath, GraphLockInfo info, WorkspaceModel workspace)
        {
            locks[normalizedPath] = new OwnedLock
            {
                SidecarPath = sidecarPath,
                Info = info,
                Workspace = workspace
            };
        }

        // Refreshes heartbeat timestamps for all locks still owned by this session.
        private void OnHeartbeat(object state)
        {
            foreach (var pair in locks.ToList())
            {
                var owned = pair.Value;
                try
                {
                    GraphLockInfo current;
                    if (!GraphLockFile.TryRead(owned.SidecarPath, out current) ||
                        current.SessionId != owned.Info.SessionId)
                    {
                        locks.TryRemove(pair.Key, out _);
                        continue;
                    }

                    owned.Info.LastHeartbeatUtc = DateTime.UtcNow;
                    GraphLockFile.WriteHeartbeat(owned.SidecarPath, owned.Info);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    Log("GraphLock heartbeat failed: " + owned.SidecarPath + " - " + ex.Message);
                }
            }
        }

        // Deletes a sidecar only when it still belongs to this session.
        private void ReleaseOwnedLock(string normalizedPath, OwnedLock owned)
        {
            try
            {
                GraphLockInfo current;
                if (GraphLockFile.TryRead(owned.SidecarPath, out current) &&
                    current.SessionId == owned.Info.SessionId)
                {
                    GraphLockFile.TryDelete(owned.SidecarPath);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                Log("GraphLock release failed: " + normalizedPath + " - " + ex.Message);
            }
        }

        // Associates a newly opened workspace with its already acquired lock.
        private void OnWorkspaceAdded(WorkspaceModel workspace)
        {
            if (workspace == null || string.IsNullOrEmpty(workspace.FileName))
            {
                return;
            }

            var normalizedPath = NormalizePath(workspace.FileName);
            if (locks.TryGetValue(normalizedPath, out var owned))
            {
                owned.Workspace = workspace;
                workspace.PropertyChanged += OnWorkspacePropertyChanged;
            }
        }

        // Releases a workspace lock before the workspace is removed.
        private void OnWorkspaceRemoveStarted(WorkspaceModel workspace)
        {
            ReleaseWorkspace(workspace);
        }

        // Detaches workspace event handlers after removal.
        private void OnWorkspaceRemoved(WorkspaceModel workspace)
        {
            if (workspace != null)
            {
                workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            }
        }

        // Releases a workspace lock before the workspace is cleared.
        private void OnWorkspaceClearingStarted(WorkspaceModel workspace)
        {
            ReleaseWorkspace(workspace);
        }

        // Reconciles locks when a workspace file path changes, such as after Save As.
        private void OnWorkspacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(WorkspaceModel.FileName))
            {
                return;
            }

            var workspace = sender as WorkspaceModel;
            if (workspace == null || string.IsNullOrEmpty(workspace.FileName))
            {
                return;
            }

            var normalizedPath = NormalizePath(workspace.FileName);
            var oldPaths = locks
                .Where(pair => pair.Value.Workspace == workspace && !IsSamePath(pair.Key, normalizedPath))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var oldPath in oldPaths)
            {
                Release(oldPath);
            }

            if (!locks.ContainsKey(normalizedPath))
            {
                TryAcquireCore(normalizedPath, false, workspace);
            }
        }

        // Releases all locks when Dynamo begins shutting down.
        private void OnShutdownStarted(DynamoModel model)
        {
            ReleaseAll();
        }

        // Releases all locks associated with a workspace instance.
        private void ReleaseWorkspace(WorkspaceModel workspace)
        {
            if (workspace == null)
            {
                return;
            }

            var paths = locks
                .Where(pair => pair.Value.Workspace == workspace)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var path in paths)
            {
                Release(path);
            }
        }

        // Asks the WPF layer for a user decision only when UI prompts are allowed.
        private GraphLockUserResponse PromptIfAllowed(
            string graphPath,
            GraphLockInfo existingLock,
            bool isStale,
            bool allowPromptUI)
        {
            if (!allowPromptUI || prompt == null)
            {
                return GraphLockUserResponse.Cancel();
            }

            return prompt.AskUser(graphPath, existingLock, isStale);
        }

        // Determines whether a lock heartbeat is old enough to treat as stale.
        private bool IsStale(GraphLockInfo existingLock)
        {
            if (existingLock == null)
            {
                return true;
            }

            var ageSeconds = (DateTime.UtcNow - existingLock.LastHeartbeatUtc).TotalSeconds;
            return ageSeconds > (heartbeatMilliseconds / 1000.0) * StaleFactor;
        }

        // Determines whether an existing lock belongs to this Dynamo session.
        private bool IsSelf(GraphLockInfo existingLock)
        {
            return existingLock != null &&
                   (existingLock.SessionId == sessionId ||
                    (string.Equals(existingLock.MachineName, machineName, StringComparison.OrdinalIgnoreCase) &&
                     existingLock.ProcessId == processId &&
                     existingLock.ProcessStartUtc == processStartTimeUtc));
        }

        // Builds the lock metadata written by this Dynamo session.
        private GraphLockInfo BuildSelfInfo(string normalizedPath)
        {
            var now = DateTime.UtcNow;

            return new GraphLockInfo
            {
                SchemaVersion = 1,
                SessionId = sessionId,
                GraphPath = normalizedPath,
                MachineName = machineName,
                ProcessId = processId,
                ProcessStartUtc = processStartTimeUtc,
                LastHeartbeatUtc = now
            };
        }

        // Detects stale locks from dead processes on the same machine.
        private bool IsDeadLocalProcess(GraphLockInfo existingLock)
        {
            if (existingLock == null ||
                !string.Equals(existingLock.MachineName, machineName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                using (var process = Process.GetProcessById(existingLock.ProcessId))
                {
                    return GetProcessStartTimeUtc(process) != existingLock.ProcessStartUtc;
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
                Log("GraphLock process liveness check failed: " + ex.Message);
                return false;
            }
        }

        // Converts a graph path to the full path used for lock keys.
        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path);
        }

        // Compares graph paths using the platform-appropriate case sensitivity.
        private bool IsSamePath(string firstPath, string secondPath)
        {
            if (string.IsNullOrEmpty(firstPath) || string.IsNullOrEmpty(secondPath))
            {
                return false;
            }

            return pathComparer.Equals(NormalizePath(firstPath), NormalizePath(secondPath));
        }

        // Reads process start time safely because some platforms/processes can deny it.
        private static DateTime GetProcessStartTimeUtc(Process process)
        {
            try
            {
                return process.StartTime.ToUniversalTime();
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        // Determines whether path comparisons should use Windows case-insensitive behavior.
        private static bool IsWindows()
        {
            var platform = Environment.OSVersion.Platform;
            return platform == PlatformID.Win32NT ||
                   platform == PlatformID.Win32S ||
                   platform == PlatformID.Win32Windows ||
                   platform == PlatformID.WinCE;
        }

        // Logs graph-lock diagnostics without failing during shutdown.
        private void Log(string message)
        {
            try
            {
                dynamoModel.Logger?.Log(message);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }

        /// <summary>
        /// Releases owned graph locks and unsubscribes from Dynamo model events.
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            dynamoModel.WorkspaceAdded -= OnWorkspaceAdded;
            dynamoModel.WorkspaceRemoveStarted -= OnWorkspaceRemoveStarted;
            dynamoModel.WorkspaceRemoved -= OnWorkspaceRemoved;
            dynamoModel.WorkspaceClearingStarted -= OnWorkspaceClearingStarted;
            dynamoModel.ShutdownStarted -= OnShutdownStarted;

            AppDomain.CurrentDomain.ProcessExit -= ReleaseAll;
            AppDomain.CurrentDomain.UnhandledException -= ReleaseAll;

            ReleaseAll();
        }
    }
}
