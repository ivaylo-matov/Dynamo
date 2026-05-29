using System;
using System.Globalization;
using System.Windows;
using Dynamo.Graph.Workspaces.Locking;

namespace Dynamo.UI.Prompts
{
    internal sealed class WpfGraphLockUserPrompt : IGraphLockUserPrompt
    {
        private readonly Func<Window> ownerProvider;

        internal WpfGraphLockUserPrompt(Func<Window> ownerProvider)
        {
            this.ownerProvider = ownerProvider;
        }

        public GraphLockUserDecision AskUser(string graphPath, GraphLockInfo existingLock, bool isStale)
        {
            var owner = ownerProvider?.Invoke();
            var result = DynamoMessageBox.Show(
                owner,
                BuildBody(graphPath, existingLock, isStale),
                "Graph already open",
                MessageBoxButton.YesNoCancel,
                new[]
                {
                    "Open read-only",
                    "Open anyway",
                    "Cancel"
                },
                MessageBoxImage.Warning);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    return GraphLockUserDecision.ReadOnly;
                case MessageBoxResult.No:
                    return ConfirmOpenAnyway(owner);
                default:
                    return GraphLockUserDecision.Cancel;
            }
        }

        private static GraphLockUserDecision ConfirmOpenAnyway(Window owner)
        {
            var confirm = DynamoMessageBox.Show(
                owner,
                "Opening anyway can overwrite changes from another Dynamo session. Continue?",
                "Graph already open",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            return confirm == MessageBoxResult.Yes
                ? GraphLockUserDecision.Takeover
                : GraphLockUserDecision.Cancel;
        }

        private static string BuildBody(string graphPath, GraphLockInfo existingLock, bool isStale)
        {
            if (existingLock == null)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} appears to already be open in another Dynamo session.",
                    graphPath);
            }

            var lastActivity = FormatAge(DateTime.UtcNow - existingLock.LastHeartbeatUtc);
            var format = isStale
                ? "{0} appears to already be open, but the lock may be stale. Last activity {4}."
                : "{0} is already open in Dynamo {1} by {2} on {3}. Last activity {4}.";

            return string.Format(
                CultureInfo.CurrentCulture,
                format,
                graphPath,
                existingLock.DynamoMajorMinor,
                existingLock.UserName,
                existingLock.MachineName,
                lastActivity);
        }

        private static string FormatAge(TimeSpan age)
        {
            if (age.TotalSeconds < 60)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0} seconds ago", Math.Max(0, (int)age.TotalSeconds));
            }

            if (age.TotalMinutes < 60)
            {
                return string.Format(CultureInfo.CurrentCulture, "{0} minutes ago", (int)age.TotalMinutes);
            }

            return string.Format(CultureInfo.CurrentCulture, "{0} hours ago", (int)age.TotalHours);
        }

    }
}
