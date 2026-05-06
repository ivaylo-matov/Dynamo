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
        /// Extracts and parses the metadata of a downloaded package.
        /// Calls <see cref="BuildStagedPackage"/> followed by <see cref="FinalizeExtraction"/>.
        /// </summary>
        /// <param name="dynamoModel">Dynamo model</param>
        /// <param name="installDirectory">If specified, overrides Dynamo's default base folder for packages</param>
        /// <param name="pkg">Metatda parsed from the package</param>
        /// <returns>Whether the operation succeeded or not</returns>
        public bool Extract(DynamoModel dynamoModel, string installDirectory, out Package pkg)
        {
            var staged = BuildStagedPackage(dynamoModel, installDirectory);
            if (staged == null)
            {
                pkg = null;
                return false;
            }

            FinalizeExtraction(staged);
            pkg = staged.Package;
            return true;
        }

        /// <summary>
        /// Unzips the downloaded package into a temporary staging folder and parses its
        /// metadata, but does not yet copy any files into Dynamo's package folder. The returned
        /// <see cref="StagedPackage"/> can be inspected (for example, for conflicting custom node
        /// definitions) before either calling <see cref="FinalizeExtraction"/> to install the
        /// package or <see cref="CleanupStaging"/> to discard it.
        /// </summary>
        /// <param name="dynamoModel">Dynamo model</param>
        /// <param name="installDirectory">If specified, overrides Dynamo's default base folder for packages</param>
        /// <returns>A staged package handle, or null if the package metadata could not be parsed</returns>
        internal StagedPackage BuildStagedPackage(DynamoModel dynamoModel, string installDirectory)
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

            var installedPath = BuildInstallDirectoryString(installDirectory, stagedPkg.Name);

            return new StagedPackage(stagedPkg, unzipPath, installedPath);
        }

        /// <summary>
        /// Copies the staged package contents into Dynamo's package folder and updates
        /// <see cref="Package.RootDirectory"/> to point at the final installed location.
        /// </summary>
        /// <param name="staged">Staged package returned from <see cref="BuildStagedPackage"/>.</param>
        internal static void FinalizeExtraction(StagedPackage staged)
        {
            if (staged == null) throw new ArgumentNullException(nameof(staged));

            Directory.CreateDirectory(staged.InstalledPath);

            foreach (string dirPath in Directory.GetDirectories(staged.StagingPath, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(staged.StagingPath, staged.InstalledPath));

            foreach (string newPath in Directory.GetFiles(staged.StagingPath, "*.*", SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(staged.StagingPath, staged.InstalledPath));

            staged.Package.RootDirectory = staged.InstalledPath;
        }

        /// <summary>
        /// Deletes the temporary staging folder created by <see cref="BuildStagedPackage"/>.
        /// Used to back out an install when, for example, the user cancels after a conflict
        /// with an already-loaded package is detected.
        /// </summary>
        /// <param name="staged">Staged package returned from <see cref="BuildStagedPackage"/>.</param>
        internal static void CleanupStaging(StagedPackage staged)
        {
            if (staged == null) return;

            try
            {
                if (Directory.Exists(staged.StagingPath))
                {
                    Directory.Delete(staged.StagingPath, true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // cancel, install, redownload

    }

    /// <summary>
    /// Represents a downloaded package that has been unzipped to a temporary staging folder
    /// but has not yet been copied into Dynamo's package folder. Returned by
    /// <see cref="PackageDownloadHandle.BuildStagedPackage"/>.
    /// </summary>
    internal class StagedPackage
    {
        /// <summary>
        /// Package metadata parsed from the staged pkg.json. Its <see cref="Package.RootDirectory"/>
        /// initially points at <see cref="StagingPath"/> and is updated to <see cref="InstalledPath"/>
        /// once <see cref="PackageDownloadHandle.FinalizeExtraction"/> is called.
        /// </summary>
        public Package Package { get; }

        /// <summary>
        /// Temporary folder containing the unzipped package contents.
        /// </summary>
        public string StagingPath { get; }

        /// <summary>
        /// Final installation folder under Dynamo's packages directory. Created and populated
        /// by <see cref="PackageDownloadHandle.FinalizeExtraction"/>.
        /// </summary>
        public string InstalledPath { get; }

        internal StagedPackage(Package package, string stagingPath, string installedPath)
        {
            Package = package;
            StagingPath = stagingPath;
            InstalledPath = installedPath;
        }
    }

}
