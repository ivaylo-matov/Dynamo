using System;
using System.IO;
using Dynamo.Core;
using Dynamo.Models;

using Greg.Responses;

namespace Dynamo.PackageManager
{
    /// <summary>
    /// View model for the installation of a package
    /// </summary>
    public class PackageDownloadHandle : NotificationObject
    {
        /// <summary>
        /// Possible states for a package installation
        /// </summary>
        public enum State
        {
            Uninitialized, Downloading, Downloaded, Installing, Installed, Error
        }

        private string _errorString = "";
        /// <summary>
        /// Error message that resulted from an unsuccessful installation
        /// </summary>
        public string ErrorString { get { return _errorString; } set { _errorString = value; RaisePropertyChanged("ErrorString"); } }

        private State _downloadState = State.Uninitialized;
        /// <summary>
        /// State of the installation of the package
        /// </summary>
        public State DownloadState
        {
            get { return _downloadState; }
            set
            {
                _downloadState = value;
                RaisePropertyChanged("DownloadState");
            }
        }

        /// <summary>
        /// Name of the package
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Identifier of the package
        /// </summary>
        public string Id { get; set; }

        private string _downloadPath;
        /// <summary>
        /// Path where the package is downloaded to
        /// </summary>
        public string DownloadPath { get { return _downloadPath; } set { _downloadPath = value; RaisePropertyChanged("DownloadPath"); } }

        private string _versionName;
        /// <summary>
        /// Version of the package
        /// </summary>
        public string VersionName { get { return _versionName; } set { _versionName = value; RaisePropertyChanged("VersionName"); } }

        /// <summary>
        /// Creates an empty view model for a package installation 
        /// </summary>
        public PackageDownloadHandle()
        {
            this.DownloadPath = string.Empty;
        }

        /// <summary>
        /// Transitions the installation to error with an error message
        /// </summary>
        /// <param name="errorString">Error message</param>
        public void Error(string errorString)
        {
            this.DownloadState = State.Error;
            this.ErrorString = errorString;
        }

        /// <summary>
        /// Transition the installation to downloaded with a path to the file
        /// </summary>
        /// <param name="filePath">Path to the file</param>
        public void Done(string filePath)
        {
            this.DownloadState = State.Downloaded;
            this.DownloadPath = filePath;
        }

        private static string BuildInstallDirectoryString(string packagesDirectory, string name)
        {
            // <user>/appdata/roaming/packages/package_name
            return packagesDirectory + @"\" + name.Replace("/", "_").Replace(@"\", "_");
        }

        // Paths populated by BuildStagedPackage and consumed by FinalizeExtraction / CleanupStaging.
        // Treated as transient install-lifecycle state, mirroring the other mutable fields on this
        // class (DownloadPath, DownloadState, ErrorString).
        private string stagingPath;
        private string installedPath;

        /// <summary>
        /// Extracts and parses the metadata of a downloaded package.
        /// Calls <see cref="BuildStagedPackage"/> followed by <see cref="FinalizeExtraction"/>.
        /// </summary>
        /// <param name="dynamoModel">Dynamo model</param>
        /// <param name="installDirectory">If specified, overrides Dynamo's default base folder for packages</param>
        /// <param name="pkg">Metatda parsed from the package</param>
        /// <returns>Whether the operation succeeded or not</returns>
        public bool Extract(DynamoModel dynamoModel, string installDirectory, out Package pkg)
        {
            pkg = BuildStagedPackage(dynamoModel, installDirectory);
            if (pkg == null)
            {
                return false;
            }

            FinalizeExtraction(pkg);
            return true;
        }

        /// <summary>
        /// Unzips the downloaded package into a temporary staging folder and parses its
        /// metadata, but does not yet copy any files into Dynamo's package folder. The returned
        /// <see cref="Package"/> can be inspected (for example, for conflicting custom node
        /// definitions) before either calling <see cref="FinalizeExtraction"/> to install the
        /// package or <see cref="CleanupStaging"/> to discard it. The staged package's
        /// <see cref="Package.RootDirectory"/> initially points at the staging folder.
        /// </summary>
        /// <param name="dynamoModel">Dynamo model</param>
        /// <param name="installDirectory">If specified, overrides Dynamo's default base folder for packages</param>
        /// <returns>The staged package, or null if the package metadata could not be parsed</returns>
        internal Package BuildStagedPackage(DynamoModel dynamoModel, string installDirectory)
        {
            this.DownloadState = State.Installing;

            // unzip into a temp staging folder; nothing is written to the Dynamo packages folder yet.
            var unzipPath = Greg.Utility.FileUtilities.UnZip(DownloadPath);
            if (!Directory.Exists(unzipPath))
            {
                throw new Exception(Properties.Resources.PackageEmpty);
            }

            var stagedPkg = Package.FromDirectory(unzipPath, dynamoModel.Logger);
            if (stagedPkg == null)
            {
                return null;
            }

            if (String.IsNullOrEmpty(installDirectory))
                installDirectory = dynamoModel.PathManager.DefaultPackagesDirectory;

            stagingPath = unzipPath;
            installedPath = BuildInstallDirectoryString(installDirectory, stagedPkg.Name);
            return stagedPkg;
        }

        /// <summary>
        /// Copies the staged package contents into Dynamo's package folder and updates
        /// <see cref="Package.RootDirectory"/> to point at the final installed location.
        /// Must be preceded by a successful call to <see cref="BuildStagedPackage"/>.
        /// </summary>
        /// <param name="stagedPkg">Package returned from <see cref="BuildStagedPackage"/>.</param>
        internal void FinalizeExtraction(Package stagedPkg)
        {
            if (stagedPkg == null) throw new ArgumentNullException(nameof(stagedPkg));
            if (string.IsNullOrEmpty(stagingPath) || string.IsNullOrEmpty(installedPath))
            {
                throw new InvalidOperationException(
                    "BuildStagedPackage must be called successfully before FinalizeExtraction.");
            }

            Directory.CreateDirectory(installedPath);

            foreach (string dirPath in Directory.GetDirectories(stagingPath, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(stagingPath, installedPath));

            foreach (string newPath in Directory.GetFiles(stagingPath, "*.*", SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(stagingPath, installedPath));

            stagedPkg.RootDirectory = installedPath;
        }

        /// <summary>
        /// Deletes the temporary staging folder created by <see cref="BuildStagedPackage"/>.
        /// Used to back out an install when, for example, the user cancels after a conflict
        /// with an already-loaded package is detected. Safe to call when the staging folder is
        /// already gone or was never created.
        /// </summary>
        internal void CleanupStaging()
        {
            if (string.IsNullOrEmpty(stagingPath)) return;

            try
            {
                if (Directory.Exists(stagingPath))
                {
                    Directory.Delete(stagingPath, true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // cancel, install, redownload

    }

}
