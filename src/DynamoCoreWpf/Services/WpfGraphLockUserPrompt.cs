using System;
using System.IO;
using System.Windows;
using Dynamo.Graph.Workspaces.Locking;
using Dynamo.UI.Prompts;
using Dynamo.Wpf.Properties;
using Dynamo.Wpf.UI;

namespace Dynamo.Wpf.Services
{
    /// <summary>
    /// Shows WPF UI for graph-lock conflicts.
    /// </summary>
    internal sealed class WpfGraphLockUserPrompt : IGraphLockUserPrompt
    {
        private readonly Func<Window> ownerProvider;
        private readonly Func<string> productNameProvider;

        /// <summary>
        /// Initializes a WPF graph-lock prompt.
        /// </summary>
        /// <param name="ownerProvider">Provides the owner window when a prompt is shown.</param>
        /// <param name="productNameProvider">Provides the product name for save-dialog filters.</param>
        internal WpfGraphLockUserPrompt(Func<Window> ownerProvider, Func<string> productNameProvider)
        {
            this.ownerProvider = ownerProvider;
            this.productNameProvider = productNameProvider;
        }

        /// <summary>
        /// Shows a Dynamo message box for a graph-lock conflict and optionally collects a copy destination.
        /// </summary>
        /// <param name="graphPath">The locked graph path.</param>
        /// <param name="existingLock">The existing lock metadata, or null if unavailable.</param>
        /// <param name="isStale">Whether the existing lock appears stale.</param>
        /// <returns>The user's graph-lock decision.</returns>
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

        // Shows a Save As dialog for the copy path, matching the graph file extension.
        private string ShowSaveAsDialog(string graphPath)
        {
            var extension = Path.GetExtension(graphPath);
            var directory = Path.GetDirectoryName(graphPath);
            var isCustomNode = extension.Equals(".dyf", StringComparison.OrdinalIgnoreCase);
            var defaultExtension = isCustomNode ? ".dyf" : ".dyn";
            var productName = productNameProvider?.Invoke() ?? "Dynamo";
            var filter = isCustomNode
                ? string.Format(Resources.FileDialogDynamoCustomNode, productName, "*.dyf")
                : string.Format(Resources.FileDialogDynamoWorkspace, productName, "*.dyn");
            filter += "|" + string.Format(Resources.FileDialogAllFiles, "*.*");
            var dialog = new CustomSaveFileDialog
            {
                AddExtension = true,
                DefaultExt = defaultExtension,
                Filter = filter,
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
