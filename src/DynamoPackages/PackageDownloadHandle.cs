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

        /// <summary>
        /// Unzips the downloaded package into a temporary staging directory and parses its metadata.
        /// </summary>
        /// <param name="dynamoModel">Dynamo model.</param>
        /// <param name="pkg">Parsed package, or null on failure.</param>
        /// <param name="stagedPath">Absolute path of the staging directory.</param>
        /// <returns>True on success.</returns>
        internal bool Stage(DynamoModel dynamoModel, out Package pkg, out string stagedPath)
        {
            this.DownloadState = State.Installing;

            stagedPath = Greg.Utility.FileUtilities.UnZip(DownloadPath);
            if (!Directory.Exists(stagedPath))
            {
                throw new Exception(Properties.Resources.PackageEmpty);
            }

            pkg = Package.FromDirectory(stagedPath, dynamoModel.Logger);
            return pkg != null;
        }

        /// <summary>
        /// Copies the staged package into the Dynamo packages directory and updates
        /// <see cref="Package.RootDirectory"/> to the committed path.
        /// </summary>
        /// <param name="stagedPath">Path returned by <see cref="Stage"/>.</param>
        /// <param name="installDirectory">Base packages directory; falls back to the model default if null/empty.</param>
        /// <param name="dynamoModel">Dynamo model.</param>
        /// <param name="pkg">Package returned by <see cref="Stage"/>.</param>
        /// <returns>True on success.</returns>
        internal bool CommitInstall(string stagedPath, string installDirectory, DynamoModel dynamoModel, Package pkg)
        {
            if (pkg == null || string.IsNullOrEmpty(stagedPath) || !Directory.Exists(stagedPath))
            {
                return false;
            }

            if (string.IsNullOrEmpty(installDirectory))
            {
                installDirectory = dynamoModel.PathManager.DefaultPackagesDirectory;
            }

            var installedPath = BuildInstallDirectoryString(installDirectory, pkg.Name);
            Directory.CreateDirectory(installedPath);

            foreach (string dirPath in Directory.GetDirectories(stagedPath, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(stagedPath, installedPath));

            foreach (string newPath in Directory.GetFiles(stagedPath, "*.*", SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(stagedPath, installedPath));

            pkg.RootDirectory = installedPath;
            return true;
        }

        /// <summary>
        /// Best-effort delete of the staging directory created by <see cref="Stage"/>.
        /// </summary>
        /// <param name="stagedPath">Path returned by <see cref="Stage"/>.</param>
        internal void CleanUpStaging(string stagedPath)
        {
            if (string.IsNullOrEmpty(stagedPath))
            {
                return;
            }

            try
            {
                if (Directory.Exists(stagedPath))
                {
                    Directory.Delete(stagedPath, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup; ignore IO/permission failures
            }
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
            string stagedPath = null;
            try
            {
                if (!Stage(dynamoModel, out pkg, out stagedPath))
                {
                    return false;
                }

                return CommitInstall(stagedPath, installDirectory, dynamoModel, pkg);
            }
            finally
            {
                CleanUpStaging(stagedPath);
            }
        }

        // cancel, install, redownload

    }

}
