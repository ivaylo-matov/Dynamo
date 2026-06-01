namespace Dynamo.Graph.Workspaces.Locking
{
    /// <summary>
    /// Describes the result of trying to acquire a graph lock.
    /// </summary>
    internal enum GraphLockConflict
    {
        Acquired,
        Cancelled,
        Unavailable
    }

    /// <summary>
    /// Contains the outcome of a graph-lock acquisition attempt.
    /// </summary>
    internal sealed record GraphLockAcquireResult(GraphLockConflict Conflict, string GraphPath = null, GraphLockInfo ExistingLock = null)
    {
        internal bool ShouldOpen => Conflict is GraphLockConflict.Acquired
            or GraphLockConflict.Unavailable;

        /// <summary>
        /// Creates a successful graph-lock result.
        /// </summary>
        /// <param name="graphPath">The graph path that should be opened.</param>
        /// <returns>A result indicating that Dynamo can open the graph.</returns>
        internal static GraphLockAcquireResult Acquired(string graphPath = null)
        {
            return new GraphLockAcquireResult(GraphLockConflict.Acquired, graphPath);
        }

        /// <summary>
        /// Creates a cancelled graph-lock result.
        /// </summary>
        /// <param name="existingLock">The lock that caused the cancellation, if available.</param>
        /// <returns>A result indicating that Dynamo should not open the graph.</returns>
        internal static GraphLockAcquireResult Cancelled(GraphLockInfo existingLock)
        {
            return new GraphLockAcquireResult(GraphLockConflict.Cancelled, existingLock: existingLock);
        }

        /// <summary>
        /// Creates a result for lock-file access failures.
        /// </summary>
        /// <param name="graphPath">The graph path that should be opened despite unavailable lock state.</param>
        /// <returns>A result indicating that Dynamo can open the graph without an acquired lock.</returns>
        internal static GraphLockAcquireResult Unavailable(string graphPath = null)
        {
            return new GraphLockAcquireResult(GraphLockConflict.Unavailable, graphPath);
        }
    }

    /// <summary>
    /// Contains the user's graph-lock conflict decision.
    /// </summary>
    internal sealed record GraphLockUserResponse(string SaveAsPath = null)
    {
        internal bool ShouldSaveAs => !string.IsNullOrEmpty(SaveAsPath);

        /// <summary>
        /// Creates a response for a cancelled graph-lock prompt.
        /// </summary>
        /// <returns>A response with no copy destination.</returns>
        internal static GraphLockUserResponse Cancel()
        {
            return new GraphLockUserResponse();
        }

        /// <summary>
        /// Creates a response for saving the locked graph as a copy.
        /// </summary>
        /// <param name="saveAsPath">The path where the graph copy should be created.</param>
        /// <returns>A response containing the copy destination.</returns>
        internal static GraphLockUserResponse SaveAs(string saveAsPath)
        {
            return new GraphLockUserResponse(saveAsPath);
        }
    }
}
