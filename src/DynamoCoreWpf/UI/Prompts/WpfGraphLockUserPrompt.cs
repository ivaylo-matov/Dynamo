using System;
using System.Globalization;
using System.IO;
using System.Windows;
using Dynamo.Graph.Workspaces.Locking;
using Dynamo.Wpf.Properties;
using Dynamo.Wpf.Utilities;

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
            var dialog = new GraphLockConflictDialog(
                GetResource("GraphLockTitle", "Graph already open"),
                BuildBody(graphPath, existingLock, isStale),
                GetResource("GraphLockButtonReadOnly", "Open read-only"),
                GetResource("GraphLockButtonOpenAnyway", "Open anyway"),
                GetResource("GraphLockButtonCancel", "Cancel"));

            var owner = ownerProvider?.Invoke();
            if (owner != null)
            {
                dialog.Owner = owner;
            }

            var result = dialog.ShowDialog();
            if (result != true)
            {
                return GraphLockUserDecision.Cancel;
            }

            if (dialog.Decision == GraphLockUserDecision.Takeover)
            {
                var confirm = MessageBoxService.Show(
                    owner,
                    GetResource(
                        "GraphLockOpenAnywayConfirmation",
                        "Opening anyway can overwrite changes from another Dynamo session. Continue?"),
                    GetResource("GraphLockTitle", "Graph already open"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes)
                {
                    return GraphLockUserDecision.Cancel;
                }
            }

            return dialog.Decision;
        }

        private static string BuildBody(string graphPath, GraphLockInfo existingLock, bool isStale)
        {
            if (existingLock == null)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    GetResource(
                        "GraphLockBodyCorrupt",
                        "{0} has a graph lock file that could not be read. It may have been left by a previous session."),
                    graphPath);
            }

            var lastActivity = FormatAge(DateTime.UtcNow - existingLock.LastHeartbeatUtc);
            var format = isStale
                ? GetResource(
                    "GraphLockBodyStaleFormat",
                    "{0} appears to have been left locked by a previous Dynamo session that did not exit cleanly. Last activity {4}.")
                : GetResource(
                    "GraphLockBodyLiveFormat",
                    "{0} is open in Dynamo {1} by {2} on {3}. Last activity {4}.");

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

        internal static string GetResource(string key, string fallback)
        {
            return Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
        }
    }
}
