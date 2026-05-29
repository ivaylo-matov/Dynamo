using System;
using System.Globalization;
using System.IO;
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

        public GraphLockUserResponse AskUser(string graphPath, GraphLockInfo existingLock, bool isStale)
        {
            var owner = ownerProvider?.Invoke();
            var result = DynamoMessageBox.Show(
                owner,
                BuildBody(graphPath, existingLock, isStale),
                "Graph already open",
                MessageBoxButton.OKCancel,
                new[]
                {
                    "Save as",
                    "Cancel"
                },
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK)
            {
                return GraphLockUserResponse.Cancel();
            }

            var saveAsPath = ShowSaveAsDialog(graphPath);
            return string.IsNullOrEmpty(saveAsPath)
                ? GraphLockUserResponse.Cancel()
                : GraphLockUserResponse.SaveAs(saveAsPath);
        }

        private static string ShowSaveAsDialog(string graphPath)
        {
            using (var dialog = new System.Windows.Forms.SaveFileDialog())
            {
                var extension = Path.GetExtension(graphPath);
                dialog.DefaultExt = string.IsNullOrEmpty(extension) ? "dyn" : extension.TrimStart('.');
                dialog.Filter = "Dynamo graphs (*.dyn;*.dyf)|*.dyn;*.dyf|All files (*.*)|*.*";
                dialog.FileName = Path.GetFileName(graphPath);

                var directory = Path.GetDirectoryName(graphPath);
                if (Directory.Exists(directory))
                {
                    dialog.InitialDirectory = directory;
                }

                return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    ? dialog.FileName
                    : null;
            }
        }

        private static string BuildBody(string graphPath, GraphLockInfo existingLock, bool isStale)
        {
            if (existingLock == null)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "{0} is already open in another Dynamo session. Cancel or save a copy.",
                    graphPath);
            }

            var format = isStale
                ? "{0} appears to already be open, but the lock may be stale. Cancel or save a copy."
                : "{0} is already open in Dynamo {1} by {2} on {3}. Cancel or save a copy.";

            return string.Format(
                CultureInfo.CurrentCulture,
                format,
                graphPath,
                existingLock.DynamoMajorMinor,
                existingLock.UserName,
                existingLock.MachineName);
        }

    }
}
