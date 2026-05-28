namespace Dynamo.Graph.Workspaces.Locking
{
    internal enum GraphLockConflict
    {
        Acquired,
        LiveConflict,
        StaleConflict,
        ReadOnly,
        Cancelled,
        Unavailable
    }

    internal sealed record GraphLockAcquireResult(GraphLockConflict Conflict, GraphLockInfo ExistingLock = null)
    {
        internal bool ShouldOpen => Conflict is GraphLockConflict.Acquired
            or GraphLockConflict.ReadOnly
            or GraphLockConflict.Unavailable;

        internal bool ShouldOpenReadOnly => Conflict == GraphLockConflict.ReadOnly;

        internal static GraphLockAcquireResult Acquired()
        {
            return new GraphLockAcquireResult(GraphLockConflict.Acquired);
        }

        internal static GraphLockAcquireResult ReadOnly(GraphLockInfo existingLock)
        {
            return new GraphLockAcquireResult(GraphLockConflict.ReadOnly, existingLock);
        }

        internal static GraphLockAcquireResult Cancelled(GraphLockInfo existingLock)
        {
            return new GraphLockAcquireResult(GraphLockConflict.Cancelled, existingLock);
        }

        internal static GraphLockAcquireResult Unavailable()
        {
            return new GraphLockAcquireResult(GraphLockConflict.Unavailable);
        }
    }

    internal enum GraphLockUserDecision
    {
        Cancel,
        ReadOnly,
        Takeover
    }
}
