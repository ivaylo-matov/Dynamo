using System.Windows;
using Dynamo.PackageManager;
using Dynamo.Wpf.Utilities;

namespace Dynamo.ViewModels
{
    /// <summary>
    /// Shared Yes/No prompt when a package install would replace custom node definitions owned by another package.
    /// </summary>
    internal static class PackageCustomNodePackageConflictDialog
    {
        /// <summary>
        /// Shows the standard "Cannot Download Package" dialog and returns whether the user chose to mark the
        /// installed package for removal (Yes).
        /// </summary>
        /// <param name="owner">Optional owner window; may be null.</param>
        /// <param name="installed">Package already providing the conflicting definitions.</param>
        /// <param name="conflicting">Package being installed.</param>
        internal static bool ShowShouldMarkInstalledPackageForUninstall(
            Window owner,
            Package installed,
            Package conflicting)
        {
            var message = string.Format(Properties.Resources.MessageUninstallCustomNodeToContinue,
                installed.Name + " " + installed.VersionName, conflicting.Name + " " + conflicting.VersionName);

            var result = owner != null
                ? MessageBoxService.Show(owner, message, Properties.Resources.CannotDownloadPackageMessageBoxTitle,
                    MessageBoxButton.YesNo, MessageBoxImage.Error)
                : MessageBoxService.Show(message, Properties.Resources.CannotDownloadPackageMessageBoxTitle,
                    MessageBoxButton.YesNo, MessageBoxImage.Error);

            return result == MessageBoxResult.Yes;
        }
    }
}
