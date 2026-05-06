using System;
using System.IO;
using Dynamo.Core;
using Dynamo.Models;

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

        /// <summary>
        /// Unzips the downloaded archive and parses package metadata. The package root remains on the
        /// temporary extraction path until <see cref="CommitStagedPackageToInstallDirectory"/> runs.
        /// </summary>
        /// <param name="dynamoModel">Dynamo model (logger).</param>
        /// <param name="pkg">Parsed package with <see cref="Package.RootDirectory"/> set to the staging folder.</param>
        /// <param name="stagedExtractRoot">Temporary directory containing extracted files; caller must commit or cancel.</param>
        /// <returns>False when the archive is empty or package metadata cannot be read.</returns>
        internal bool TryStageDownloadedPackageForInstall(
            DynamoModel dynamoModel,
            out Package pkg,
            out string stagedExtractRoot)
        {
            pkg = null;
            stagedExtractRoot = null;

            this.DownloadState = State.Installing;

            var unzipPath = Greg.Utility.FileUtilities.UnZip(DownloadPath);
            if (!Directory.Exists(unzipPath))
            {
                throw new Exception(Properties.Resources.PackageEmpty);
            }

            stagedExtractRoot = unzipPath;
            pkg = Package.FromDirectory(unzipPath, dynamoModel.Logger);

            if (pkg == null)
            {
                TryDeleteDirectoryQuietly(stagedExtractRoot);
                stagedExtractRoot = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Copies a staged package into the Dynamo package folder, updates <see cref="Package.RootDirectory"/>,
        /// and deletes the staging directory.
        /// </summary>
        internal bool CommitStagedPackageToInstallDirectory(
            DynamoModel dynamoModel,
            string installDirectory,
            Package pkg,
            string stagedExtractRoot)
        {
            if (string.IsNullOrEmpty(installDirectory))
            {
                installDirectory = dynamoModel.PathManager.DefaultPackagesDirectory;
            }

            var installedPath = BuildInstallDirectoryString(installDirectory, pkg.Name);
            Directory.CreateDirectory(installedPath);

            foreach (string dirPath in Directory.GetDirectories(stagedExtractRoot, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(stagedExtractRoot, installedPath));
            }

            foreach (string newPath in Directory.GetFiles(stagedExtractRoot, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(stagedExtractRoot, installedPath));
            }

            pkg.RootDirectory = installedPath;

            TryDeleteDirectoryQuietly(stagedExtractRoot);

            return true;
        }

        /// <summary>
        /// Deletes the temporary extraction folder and returns the download handle to the downloaded state.
        /// </summary>
        internal void CancelStagedPackageInstall(string stagedExtractRoot)
        {
            TryDeleteDirectoryQuietly(stagedExtractRoot);

            if (DownloadState == State.Installing)
            {
                DownloadState = State.Downloaded;
            }
        }

        private static void TryDeleteDirectoryQuietly(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
            {
                return;
            }

            try
            {
                Directory.Delete(directoryPath, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }

        /// <summary>
        /// Extracts and parses the metadata of a downloaded package
        /// </summary>
        /// <param name="dynamoModel">Dynamo model</param>
        /// <param name="installDirectory">If specified, overrides Dynamo's default base folder for packages</param>
        /// <param name="pkg">Metatda parsed from the package</param>
        /// <returns>Whether the operation succeeded or not</returns>
        public bool Extract(DynamoModel dynamoModel, string installDirectory, out Package pkg)
        {
            if (!TryStageDownloadedPackageForInstall(dynamoModel, out pkg, out var stagedRoot))
            {
                return false;
            }

            try
            {
                return CommitStagedPackageToInstallDirectory(dynamoModel, installDirectory, pkg, stagedRoot);
            }
            catch
            {
                CancelStagedPackageInstall(stagedRoot);
                throw;
            }
        }

        // cancel, install, redownload

    }

}
