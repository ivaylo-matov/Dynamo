namespace Dynamo.Graph.Workspaces.Locking
{
    internal interface IGraphLockUserPrompt
    {
        GraphLockUserDecision AskUser(string graphPath, GraphLockInfo existingLock, bool isStale);
    }
}
