using System;
using System.IO;
using Dynamo.Core;
using Dynamo.Logging;
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

        internal enum ExtractionState
        {
            Success,
            InvalidPackage,
            Cancelled
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
        /// Extracts and parses the metadata of a downloaded package
        /// </summary>
        /// <param name="dynamoModel">Dynamo model</param>
        /// <param name="installDirectory">If specified, overrides Dynamo's default base folder for packages</param>
        /// <param name="pkg">Metatda parsed from the package</param>
        /// <returns>Whether the operation succeeded or not</returns>
        public bool Extract(DynamoModel dynamoModel, string installDirectory, out Package pkg)
        {
            return Extract(dynamoModel, installDirectory, null, out pkg) == ExtractionState.Success;
        }

        internal ExtractionState Extract(DynamoModel dynamoModel, string installDirectory, Func<Package, bool> validatePackage, out Package pkg)
        {
            this.DownloadState = State.Installing;

            // unzip, place files
            var unzipPath = Greg.Utility.FileUtilities.UnZip(DownloadPath);
            if (!Directory.Exists(unzipPath))
            {
                throw new Exception(Properties.Resources.PackageEmpty);
            }

            // provide handle to installed package 
            pkg = Package.FromDirectory(unzipPath, dynamoModel.Logger);

            // validate package metadata before installing
            if (pkg == null)
            {
                TryDeleteDirectory(unzipPath, dynamoModel.Logger);
                return ExtractionState.InvalidPackage;
            }

            if (String.IsNullOrEmpty(installDirectory))
                installDirectory = dynamoModel.PathManager.DefaultPackagesDirectory;

            // validate staged package before final copy
            if (validatePackage != null && !validatePackage(pkg))
            {
                TryDeleteDirectory(unzipPath, dynamoModel.Logger);
                return ExtractionState.Cancelled;
            }

            var installedPath = BuildInstallDirectoryString(installDirectory, pkg.Name);
            Directory.CreateDirectory(installedPath);

            // Now create all of the directories
            foreach (string dirPath in Directory.GetDirectories(unzipPath, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(unzipPath, installedPath));

            // Copy all the files
            foreach (string newPath in Directory.GetFiles(unzipPath, "*.*", SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(unzipPath, installedPath));

            // Update root directory to final path
            pkg.RootDirectory = installedPath;

            return ExtractionState.Success;
        }

        private static void TryDeleteDirectory(string directory, ILogger logger)
        {
            try
            {
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
            catch (IOException ex)
            {
                logger?.Log($"Failed to delete package staging directory {directory}: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                logger?.Log($"Failed to delete package staging directory {directory}: {ex}");
            }
        }
    }

}
