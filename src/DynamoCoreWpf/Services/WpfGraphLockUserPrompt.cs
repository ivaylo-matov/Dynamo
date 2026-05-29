using System;
using System.IO;
using System.Windows;
using Dynamo.Graph.Workspaces.Locking;
using Dynamo.UI.Prompts;
using Dynamo.Wpf.UI;

namespace Dynamo.Wpf.Services
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
            var result = DynamoMessageBox.Show(
                ownerProvider?.Invoke(),
                "This graph is already open in another Dynamo session. Save a copy or cancel.",
                "Graph already open",
                MessageBoxButton.OKCancel,
                new[] { "Save as", "Cancel" },
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
            var extension = Path.GetExtension(graphPath);
            var directory = Path.GetDirectoryName(graphPath);
            var dialog = new CustomSaveFileDialog
            {
                AddExtension = true,
                DefaultExt = string.IsNullOrEmpty(extension) ? ".dyn" : extension,
                Filter = "Dynamo graphs (*.dyn;*.dyf)|*.dyn;*.dyf|All files (*.*)|*.*",
                FileName = Path.GetFileName(graphPath)
            };

            if (Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }

            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }
    }
}
