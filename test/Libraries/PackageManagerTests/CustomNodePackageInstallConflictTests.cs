using System;
using System.Collections.Generic;
using System.IO;
using Dynamo.Core;
using Dynamo.Extensions;
using Dynamo.Graph.Workspaces;
using Dynamo.Interfaces;
using Moq;
using NUnit.Framework;

namespace Dynamo.PackageManager.Tests
{
    [TestFixture]
    class CustomNodePackageInstallConflictTests : DynamoModelTestBase
    {
        public string PackagesDirectory => Path.Combine(TestDirectory, "pkgs");

        protected override void GetLibrariesToPreload(List<string> libraries)
        {
            libraries.Add("DesignScriptBuiltin.dll");
            libraries.Add("DSCoreNodes.dll");
            base.GetLibrariesToPreload(libraries);
        }

        [Test]
        public void WhenStagedCustomNodeDefinitionsShareGuidsWithDifferentLoadedPackageThenTryFindConflictReturnsTrue()
        {
            var pathManager = new Mock<IPathManager>();
            pathManager.SetupGet(x => x.PackagesDirectories).Returns(() => new List<string> { PackagesDirectory });

            var loader = new PackageLoader(pathManager.Object);
            var libraryLoader = new ExtensionLibraryLoader(CurrentDynamoModel);

            loader.PackagesLoaded += libraryLoader.LoadPackages;
            loader.RequestLoadNodeLibrary += libraryLoader.LoadLibraryAndSuppressZTSearchImport;
            loader.RequestLoadCustomNodeDirectory += (dir, pkgInfo) =>
                CurrentDynamoModel.CustomNodeManager.AddUninitializedCustomNodesInPath(dir, isTestMode: false, packageInfo: pkgInfo);

            var evenOddDir = Path.Combine(PackagesDirectory, "EvenOdd");
            var evenOddPkg = Package.FromDirectory(evenOddDir, CurrentDynamoModel.Logger);
            loader.LoadPackages(new[] { evenOddPkg });

            var evenOdd2DyfDir = Path.Combine(PackagesDirectory, "EvenOdd2", "dyf");
            var conflictingPackageInfo = new PackageInfo("EvenOdd2", new Version(1, 0, 0));

            var hasConflict = CurrentDynamoModel.CustomNodeManager.TryFindCrossPackageCustomNodeGuidConflictWithLoadedPackages(
                evenOdd2DyfDir,
                CurrentDynamoModel.IsTestMode,
                conflictingPackageInfo,
                out var existing,
                out var candidate);

            Assert.IsTrue(hasConflict);
            Assert.IsNotNull(existing);
            Assert.IsNotNull(candidate);
            Assert.AreEqual("EvenOdd", existing.PackageInfo.Name);
            Assert.AreEqual("EvenOdd2", candidate.PackageInfo.Name);
        }

        [Test]
        public void WhenStagedDefinitionsBelongToSamePackageNameAsLoadedThenTryFindConflictReturnsFalse()
        {
            var pathManager = new Mock<IPathManager>();
            pathManager.SetupGet(x => x.PackagesDirectories).Returns(() => new List<string> { PackagesDirectory });

            var loader = new PackageLoader(pathManager.Object);
            var libraryLoader = new ExtensionLibraryLoader(CurrentDynamoModel);

            loader.PackagesLoaded += libraryLoader.LoadPackages;
            loader.RequestLoadNodeLibrary += libraryLoader.LoadLibraryAndSuppressZTSearchImport;
            loader.RequestLoadCustomNodeDirectory += (dir, pkgInfo) =>
                CurrentDynamoModel.CustomNodeManager.AddUninitializedCustomNodesInPath(dir, isTestMode: false, packageInfo: pkgInfo);

            var evenOddDir = Path.Combine(PackagesDirectory, "EvenOdd");
            var evenOddPkg = Package.FromDirectory(evenOddDir, CurrentDynamoModel.Logger);
            loader.LoadPackages(new[] { evenOddPkg });

            var evenOdd2DyfDir = Path.Combine(PackagesDirectory, "EvenOdd2", "dyf");
            var sameNamePackageInfo = new PackageInfo("EvenOdd", new Version(2, 0, 0));

            var hasConflict = CurrentDynamoModel.CustomNodeManager.TryFindCrossPackageCustomNodeGuidConflictWithLoadedPackages(
                evenOdd2DyfDir,
                CurrentDynamoModel.IsTestMode,
                sameNamePackageInfo,
                out _,
                out _);

            Assert.IsFalse(hasConflict);
        }

        [Test]
        public void WhenCustomNodeDirectoryDoesNotExistThenTryFindConflictReturnsFalse()
        {
            var missingDir = Path.Combine(TestDirectory, "nonexistent_dyf_folder");
            var packageInfo = new PackageInfo("Any", new Version(1, 0, 0));

            var hasConflict = CurrentDynamoModel.CustomNodeManager.TryFindCrossPackageCustomNodeGuidConflictWithLoadedPackages(
                missingDir,
                CurrentDynamoModel.IsTestMode,
                packageInfo,
                out _,
                out _);

            Assert.IsFalse(hasConflict);
        }
    }
}
