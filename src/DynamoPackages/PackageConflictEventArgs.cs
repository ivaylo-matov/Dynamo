using System;

namespace Dynamo.PackageManager
{
    /// <summary>
    /// Event data raised when a package install conflicts with an already-loaded custom-node
    /// package. Subscribers prompt the user and use <see cref="CancelInstall"/> to communicate
    /// the user's decision back to the install pipeline.
    /// </summary>
    public class PackageConflictEventArgs : EventArgs
    {
        /// <summary>
        /// The package that is currently installed and whose custom nodes share GUIDs with the
        /// package being installed.
        /// </summary>
        public Package Installed { get; }

        /// <summary>
        /// The package the user is attempting to install.
        /// </summary>
        public Package Conflicting { get; }

        /// <summary>
        /// When <c>true</c>, the install pipeline must abort the install: the staged contents
        /// must not be committed to the Dynamo packages directory, the staging folder must be
        /// cleaned up, and the existing package must not be marked for uninstall.
        /// Defaults to <c>false</c> so that pipelines proceed when no subscriber is attached.
        /// </summary>
        public bool CancelInstall { get; set; }

        /// <summary>
        /// Creates a new <see cref="PackageConflictEventArgs"/>.
        /// </summary>
        /// <param name="installed">The currently-installed conflicting package.</param>
        /// <param name="conflicting">The package being installed.</param>
        public PackageConflictEventArgs(Package installed, Package conflicting)
        {
            Installed = installed;
            Conflicting = conflicting;
            CancelInstall = false;
        }
    }
}
