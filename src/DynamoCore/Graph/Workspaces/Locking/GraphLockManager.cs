using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using Dynamo.Models;

namespace Dynamo.Graph.Workspaces.Locking
{
    internal sealed class GraphLockManager : IDisposable
    {
        internal const int DefaultHeartbeatMilliseconds = 30000;
        private const int StaleFactor = 5;

        private readonly DynamoModel dynamoModel;
        private readonly ConcurrentDictionary<string, OwnedLock> locks;
        private readonly ConcurrentDictionary<string, byte> openingPaths;
        private readonly ConcurrentDictionary<string, OwnedLock> pendingSaveAsLocks;
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

        internal GraphLockManager(
            DynamoModel dynamoModel,
            IGraphLockUserPrompt prompt = null,
            int heartbeatMilliseconds = DefaultHeartbeatMilliseconds,
            bool forceEnable = false)
        {
            this.dynamoModel = dynamoModel ?? throw new ArgumentNullException(nameof(dynamoModel));
            this.prompt = prompt;
            this.heartbeatMilliseconds = heartbeatMilliseconds;
            locks = new ConcurrentDictionary<string, OwnedLock>(StringComparer.OrdinalIgnoreCase);
            openingPaths = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            pendingSaveAsLocks = new ConcurrentDictionary<string, OwnedLock>(StringComparer.OrdinalIgnoreCase);

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

        internal void SetPrompt(IGraphLockUserPrompt userPrompt)
        {
            prompt = userPrompt;
        }

        internal GraphLockAcquireResult TryAcquire(string graphPath, bool allowPromptUI)
        {
            if (!enabled || string.IsNullOrEmpty(graphPath))
            {
                return GraphLockAcquireResult.Acquired();
            }

            var normalizedPath = NormalizePath(graphPath);
            openingPaths[normalizedPath] = 0;

            return TryAcquireCore(normalizedPath, allowPromptUI, null);
        }

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

        internal GraphLockAcquireResult PrepareSaveAs(WorkspaceModel workspace, string newPath, bool allowPromptUI)
        {
            if (!enabled || workspace == null || string.IsNullOrEmpty(newPath))
            {
                return GraphLockAcquireResult.Acquired();
            }

            var normalizedNewPath = NormalizePath(newPath);
            if (IsSamePath(workspace.FileName, normalizedNewPath))
            {
                return GraphLockAcquireResult.Acquired();
            }

            var result = TryAcquireCore(normalizedNewPath, allowPromptUI, workspace);
            if (result.Conflict == GraphLockConflict.Acquired &&
                locks.TryRemove(normalizedNewPath, out var owned))
            {
                pendingSaveAsLocks[PendingSaveAsKey(workspace, normalizedNewPath)] = owned;
            }

            return result;
        }

        internal void CommitSaveAs(WorkspaceModel workspace, string newPath)
        {
            if (!enabled || workspace == null || string.IsNullOrEmpty(newPath))
            {
                return;
            }

            var normalizedNewPath = NormalizePath(newPath);
            var pendingKey = PendingSaveAsKey(workspace, normalizedNewPath);
            pendingSaveAsLocks.TryRemove(pendingKey, out var pendingLock);

            var oldPaths = locks
                .Where(pair => pair.Value.Workspace == workspace && !IsSamePath(pair.Key, normalizedNewPath))
                .Select(pair => pair.Key)
                .ToList();
            foreach (var oldPath in oldPaths)
            {
                Release(oldPath);
            }

            if (pendingLock != null)
            {
                pendingLock.Workspace = workspace;
                locks[normalizedNewPath] = pendingLock;
            }
        }

        internal void CancelSaveAs(WorkspaceModel workspace, string newPath)
        {
            if (!enabled || workspace == null || string.IsNullOrEmpty(newPath))
            {
                return;
            }

            var normalizedNewPath = NormalizePath(newPath);
            if (pendingSaveAsLocks.TryRemove(PendingSaveAsKey(workspace, normalizedNewPath), out var pendingLock))
            {
                ReleaseOwnedLock(normalizedNewPath, pendingLock);
            }
        }

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

        internal void ReleaseAll(object sender = null, EventArgs args = null)
        {
            foreach (var path in locks.Keys.ToList())
            {
                Release(path);
            }

            foreach (var pair in pendingSaveAsLocks.ToList())
            {
                if (pendingSaveAsLocks.TryRemove(pair.Key, out var owned))
                {
                    ReleaseOwnedLock(owned.Info.GraphPath, owned);
                }
            }

            heartbeatTimer?.Dispose();
            heartbeatTimer = null;
        }

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
                        return GraphLockAcquireResult.Acquired();
                    }

                    GraphLockInfo existingLock;
                    var readable = GraphLockFile.TryRead(sidecarPath, out existingLock);
                    var isStale = !readable || IsStale(existingLock);

                    if (readable && IsSelf(existingLock))
                    {
                        RegisterOwnedLock(normalizedPath, sidecarPath, existingLock, workspace);
                        return GraphLockAcquireResult.Acquired();
                    }

                    var decision = PromptIfAllowed(normalizedPath, readable ? existingLock : null, isStale, allowPromptUI);
                    switch (decision)
                    {
                        case GraphLockUserDecision.Cancel:
                            return GraphLockAcquireResult.Cancelled(existingLock);
                        case GraphLockUserDecision.ReadOnly:
                            return GraphLockAcquireResult.ReadOnly(existingLock);
                        case GraphLockUserDecision.Takeover:
                            GraphLockFile.WriteHeartbeat(sidecarPath, info);
                            RegisterOwnedLock(normalizedPath, sidecarPath, info, workspace);
                            return GraphLockAcquireResult.Acquired();
                    }
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

            return GraphLockAcquireResult.Unavailable();
        }

        private void RegisterOwnedLock(string normalizedPath, string sidecarPath, GraphLockInfo info, WorkspaceModel workspace)
        {
            locks[normalizedPath] = new OwnedLock
            {
                SidecarPath = sidecarPath,
                Info = info,
                Workspace = workspace
            };
        }

        private void OnHeartbeat(object state)
        {
            foreach (var pair in locks.ToList())
            {
                var owned = pair.Value;
                try
                {
                    GraphLockInfo current;
                    if (GraphLockFile.TryRead(owned.SidecarPath, out current) &&
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

        private void OnWorkspaceRemoveStarted(WorkspaceModel workspace)
        {
            ReleaseWorkspace(workspace);
        }

        private void OnWorkspaceRemoved(WorkspaceModel workspace)
        {
            if (workspace != null)
            {
                workspace.PropertyChanged -= OnWorkspacePropertyChanged;
            }
        }

        private void OnWorkspaceClearingStarted(WorkspaceModel workspace)
        {
            ReleaseWorkspace(workspace);
        }

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
            if (!locks.ContainsKey(normalizedPath))
            {
                TryAcquireCore(normalizedPath, true, workspace);
            }
        }

        private void OnShutdownStarted(DynamoModel model)
        {
            ReleaseAll();
        }

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

        private GraphLockUserDecision PromptIfAllowed(
            string graphPath,
            GraphLockInfo existingLock,
            bool isStale,
            bool allowPromptUI)
        {
            if (!allowPromptUI || prompt == null)
            {
                return GraphLockUserDecision.ReadOnly;
            }

            return prompt.AskUser(graphPath, existingLock, isStale);
        }

        private bool IsStale(GraphLockInfo existingLock)
        {
            if (existingLock == null)
            {
                return true;
            }

            var ageSeconds = (DateTime.UtcNow - existingLock.LastHeartbeatUtc).TotalSeconds;
            return ageSeconds > (heartbeatMilliseconds / 1000.0) * StaleFactor;
        }

        private static bool IsSelf(GraphLockInfo existingLock)
        {
            return existingLock != null &&
                   string.Equals(existingLock.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) &&
                   existingLock.ProcessId == Environment.ProcessId;
        }

        private GraphLockInfo BuildSelfInfo(string normalizedPath)
        {
            var process = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;

            return new GraphLockInfo
            {
                SchemaVersion = 1,
                SessionId = Guid.NewGuid(),
                GraphPath = normalizedPath,
                UserName = Environment.UserName,
                MachineName = Environment.MachineName,
                ProcessId = Environment.ProcessId,
                ProcessStartUtc = process.StartTime.ToUniversalTime(),
                DynamoVersion = DynamoModel.Version,
                DynamoMajorMinor = ExtractMajorMinor(DynamoModel.Version),
                AcquiredUtc = now,
                LastHeartbeatUtc = now
            };
        }

        private static string ExtractMajorMinor(string version)
        {
            if (Version.TryParse(version, out var parsedVersion))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}.{1}", parsedVersion.Major, parsedVersion.Minor);
            }

            return version;
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path);
        }

        private static bool IsSamePath(string firstPath, string secondPath)
        {
            if (string.IsNullOrEmpty(firstPath) || string.IsNullOrEmpty(secondPath))
            {
                return false;
            }

            return string.Equals(NormalizePath(firstPath), NormalizePath(secondPath), StringComparison.OrdinalIgnoreCase);
        }

        private static string PendingSaveAsKey(WorkspaceModel workspace, string normalizedPath)
        {
            return workspace.Guid + "|" + normalizedPath;
        }

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
