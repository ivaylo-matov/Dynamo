using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Dynamo.Models;

namespace Dynamo.Utilities
{
    /// <summary>
    /// Tracks graph files opened in the current Dynamo process and detects when the same
    /// file is already open in another active Dynamo session.
    /// </summary>
    internal static class DynamoGraphFileSessionTracker
    {
        private const string MutexNamePrefix = "Dynamo_GraphFile_";
        private static readonly ConcurrentDictionary<string, Mutex> heldLocks =
            new ConcurrentDictionary<string, Mutex>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns true when the file is a persisted Dynamo graph that should participate in
        /// cross-session open tracking.
        /// </summary>
        internal static bool ShouldTrackFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || DynamoModel.IsTestMode)
            {
                return false;
            }

            var extension = Path.GetExtension(filePath);
            return extension.Equals(".dyn", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".dyf", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true when another active Dynamo session already has the file open.
        /// </summary>
        internal static bool IsOpenInAnotherSession(string filePath)
        {
            if (!ShouldTrackFile(filePath))
            {
                return false;
            }

            var normalizedPath = NormalizePath(filePath);
            if (heldLocks.ContainsKey(normalizedPath))
            {
                return false;
            }

            var mutexName = GetMutexName(normalizedPath);
            if (!Mutex.TryOpenExisting(mutexName, out Mutex existingMutex))
            {
                return false;
            }

            existingMutex.Dispose();
            return true;
        }

        /// <summary>
        /// Attempts to register the current process as having the graph file open.
        /// Returns false when another active Dynamo session already holds the lock.
        /// </summary>
        internal static bool TryRegisterOpenFile(string filePath)
        {
            if (!ShouldTrackFile(filePath))
            {
                return true;
            }

            var normalizedPath = NormalizePath(filePath);
            if (heldLocks.ContainsKey(normalizedPath))
            {
                return true;
            }

            var mutexName = GetMutexName(normalizedPath);
            var mutex = new Mutex(false, mutexName, out bool createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return false;
            }

            heldLocks[normalizedPath] = mutex;
            return true;
        }

        /// <summary>
        /// Unregisters the graph file for the current process.
        /// </summary>
        internal static void UnregisterOpenFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || DynamoModel.IsTestMode)
            {
                return;
            }

            var normalizedPath = NormalizePath(filePath);
            if (!heldLocks.TryRemove(normalizedPath, out Mutex mutex))
            {
                return;
            }

            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Mutex was not owned by this thread.
            }

            mutex.Dispose();
        }

        private static string NormalizePath(string filePath)
        {
            return Path.GetFullPath(filePath);
        }

        private static string GetMutexName(string normalizedPath)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
                return MutexNamePrefix + Convert.ToHexString(hash);
            }
        }
    }
}
