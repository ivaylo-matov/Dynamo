using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;

namespace Dynamo.PackageManager.Tests
{
    /// <summary>
    /// Tests for the staging, finalize, and cleanup phases that
    /// <see cref="PackageDownloadHandle.Extract"/> is built on top of. These phases are exercised
    /// by the package install flow in PackageManagerClientViewModel.SetPackageState so that custom
    /// node guid conflicts can be detected before any files are copied into Dynamo's package
    /// folder.
    /// </summary>
    [TestFixture]
    class PackageDownloadHandleTests : DynamoModelTestBase
    {
        private string scratchDirectory;

        public override void Setup()
        {
            base.Setup();
            scratchDirectory = Path.Combine(Path.GetTempPath(),
                "PackageDownloadHandleTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratchDirectory);
        }

        public override void Cleanup()
        {
            try
            {
                if (Directory.Exists(scratchDirectory))
                {
                    Directory.Delete(scratchDirectory, true);
                }
            }
            catch
            {
                // Best-effort cleanup; do not fail the test on filesystem hiccups.
            }

            base.Cleanup();
        }

        private string CreateTestPackageZip(string packageSourceDir)
        {
            var zipPath = Path.Combine(scratchDirectory, Path.GetFileName(packageSourceDir) + ".zip");
            ZipFile.CreateFromDirectory(packageSourceDir, zipPath, CompressionLevel.NoCompression, includeBaseDirectory: false);
            return zipPath;
        }

        [Test]
        public void BuildStagedPackageDoesNotCopyFilesIntoInstallDirectory()
        {
            var sourceDir = Path.Combine(TestDirectory, "pkgs", "EvenOdd2");
            var zipPath = CreateTestPackageZip(sourceDir);

            var installDirectory = Path.Combine(scratchDirectory, "packages");
            Directory.CreateDirectory(installDirectory);

            var handle = new PackageDownloadHandle
            {
                Name = "EvenOdd2",
                DownloadPath = zipPath,
                VersionName = "1.0.0",
            };

            var staged = handle.BuildStagedPackage(CurrentDynamoModel, installDirectory);

            Assert.IsNotNull(staged);
            Assert.AreEqual("EvenOdd2", staged.Package.Name);
            Assert.IsTrue(Directory.Exists(staged.StagingPath),
                "Staging folder should exist immediately after BuildStagedPackage.");
            Assert.IsFalse(Directory.Exists(staged.InstalledPath),
                "BuildStagedPackage must not create the final install directory.");
            Assert.AreEqual(staged.StagingPath, staged.Package.RootDirectory,
                "Staged package's RootDirectory should still point at the temp staging folder.");

            PackageDownloadHandle.CleanupStaging(staged);
        }

        [Test]
        public void FinalizeExtractionCopiesFilesAndUpdatesRootDirectory()
        {
            var sourceDir = Path.Combine(TestDirectory, "pkgs", "EvenOdd");
            var zipPath = CreateTestPackageZip(sourceDir);

            var installDirectory = Path.Combine(scratchDirectory, "packages");
            Directory.CreateDirectory(installDirectory);

            var handle = new PackageDownloadHandle
            {
                Name = "EvenOdd",
                DownloadPath = zipPath,
                VersionName = "1.0.0",
            };

            var staged = handle.BuildStagedPackage(CurrentDynamoModel, installDirectory);
            Assert.IsNotNull(staged);

            try
            {
                PackageDownloadHandle.FinalizeExtraction(staged);

                Assert.IsTrue(Directory.Exists(staged.InstalledPath),
                    "FinalizeExtraction should create the final install directory.");
                Assert.IsTrue(File.Exists(Path.Combine(staged.InstalledPath, "pkg.json")),
                    "FinalizeExtraction should copy pkg.json into the install directory.");
                Assert.AreEqual(staged.InstalledPath, staged.Package.RootDirectory,
                    "FinalizeExtraction should update the staged package's RootDirectory.");
            }
            finally
            {
                PackageDownloadHandle.CleanupStaging(staged);
                if (Directory.Exists(staged.InstalledPath))
                {
                    Directory.Delete(staged.InstalledPath, true);
                }
            }
        }

        [Test]
        public void CleanupStagingDeletesTheStagingDirectory()
        {
            var sourceDir = Path.Combine(TestDirectory, "pkgs", "EvenOdd");
            var zipPath = CreateTestPackageZip(sourceDir);

            var installDirectory = Path.Combine(scratchDirectory, "packages");
            Directory.CreateDirectory(installDirectory);

            var handle = new PackageDownloadHandle
            {
                Name = "EvenOdd",
                DownloadPath = zipPath,
                VersionName = "1.0.0",
            };

            var staged = handle.BuildStagedPackage(CurrentDynamoModel, installDirectory);
            Assert.IsNotNull(staged);
            Assert.IsTrue(Directory.Exists(staged.StagingPath));

            PackageDownloadHandle.CleanupStaging(staged);

            Assert.IsFalse(Directory.Exists(staged.StagingPath),
                "CleanupStaging should delete the temporary staging folder.");
            Assert.IsFalse(Directory.Exists(staged.InstalledPath),
                "CleanupStaging should not have created the final install directory.");
        }

        [Test]
        public void CleanupStagingTolerantOfMissingDirectory()
        {
            // Calling CleanupStaging twice (or against a non-existent staging path) should not throw.
            var staged = new StagedPackage(
                package: null,
                stagingPath: Path.Combine(scratchDirectory, "does", "not", "exist"),
                installedPath: Path.Combine(scratchDirectory, "ignored"));

            Assert.DoesNotThrow(() => PackageDownloadHandle.CleanupStaging(staged));
            Assert.DoesNotThrow(() => PackageDownloadHandle.CleanupStaging(null));
        }
    }
}
