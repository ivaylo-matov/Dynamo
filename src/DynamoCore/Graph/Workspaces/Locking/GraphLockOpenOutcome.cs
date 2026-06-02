namespace Dynamo.Graph.Workspaces.Locking
{
    /// <summary>
    /// Describes how the most recent file-open attempt interacted with the graph-lock feature.
    /// </summary>
    internal enum GraphLockOpenOutcome
    {
        /// <summary>
        /// The file was opened, or the lock could not be evaluated and the open proceeded anyway.
        /// </summary>
        Opened,

        /// <summary>
        /// The user cancelled at the graph-lock conflict prompt; no workspace was opened.
        /// </summary>
        Cancelled,

        /// <summary>
        /// The open was redirected to a Save As copy because the original was locked by another instance.
        /// </summary>
        RedirectedToCopy
    }
}
