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

        private PackageDownloadHandle CreateHandleForPackage(string packageSourceDir, string name)
        {
            var zipPath = CreateTestPackageZip(packageSourceDir);
            return new PackageDownloadHandle
            {
                Name = name,
                DownloadPath = zipPath,
                VersionName = "1.0.0",
            };
        }

        [Test]
        public void BuildStagedPackageDoesNotCreatePackageInstallDirectory()
        {
            var sourceDir = Path.Combine(TestDirectory, "pkgs", "EvenOdd2");
            var installDirectory = Path.Combine(scratchDirectory, "packages");
            Directory.CreateDirectory(installDirectory);

            var existingEntries = Directory.GetFileSystemEntries(installDirectory);
            var handle = CreateHandleForPackage(sourceDir, "EvenOdd2");

            var stagedPkg = handle.BuildStagedPackage(CurrentDynamoModel, installDirectory);

            Assert.IsNotNull(stagedPkg);
            Assert.AreEqual("EvenOdd2", stagedPkg.Name);
            Assert.IsTrue(Directory.Exists(stagedPkg.RootDirectory),
                "Staged package's RootDirectory should point at an existing temp staging folder.");
            CollectionAssert.AreEqual(existingEntries, Directory.GetFileSystemEntries(installDirectory),
                "BuildStagedPackage must not write anything into the install directory.");

            handle.CleanupStaging();
        }

        [Test]
        public void FinalizeExtractionCopiesFilesAndUpdatesRootDirectory()
        {
            var sourceDir = Path.Combine(TestDirectory, "pkgs", "EvenOdd");
            var installDirectory = Path.Combine(scratchDirectory, "packages");
            Directory.CreateDirectory(installDirectory);

            var handle = CreateHandleForPackage(sourceDir, "EvenOdd");

            var stagedPkg = handle.BuildStagedPackage(CurrentDynamoModel, installDirectory);
            Assert.IsNotNull(stagedPkg);
            var stagingPath = stagedPkg.RootDirectory;

            try
            {
                handle.FinalizeExtraction(stagedPkg);

                Assert.AreNotEqual(stagingPath, stagedPkg.RootDirectory,
                    "FinalizeExtraction must update the staged package's RootDirectory.");
                Assert.IsTrue(Directory.Exists(stagedPkg.RootDirectory),
                    "Final installed directory should exist after FinalizeExtraction.");
                Assert.IsTrue(File.Exists(Path.Combine(stagedPkg.RootDirectory, "pkg.json")),
                    "FinalizeExtraction should copy pkg.json into the install directory.");
            }
            finally
            {
                handle.CleanupStaging();
                if (Directory.Exists(stagedPkg.RootDirectory))
                {
                    Directory.Delete(stagedPkg.RootDirectory, true);
                }
            }
        }

        [Test]
        public void CleanupStagingDeletesTheStagingDirectory()
        {
            var sourceDir = Path.Combine(TestDirectory, "pkgs", "EvenOdd");
            var installDirectory = Path.Combine(scratchDirectory, "packages");
            Directory.CreateDirectory(installDirectory);

            var handle = CreateHandleForPackage(sourceDir, "EvenOdd");

            var stagedPkg = handle.BuildStagedPackage(CurrentDynamoModel, installDirectory);
            Assert.IsNotNull(stagedPkg);
            var stagingPath = stagedPkg.RootDirectory;
            Assert.IsTrue(Directory.Exists(stagingPath));

            handle.CleanupStaging();

            Assert.IsFalse(Directory.Exists(stagingPath),
                "CleanupStaging should delete the temporary staging folder.");
        }

        [Test]
        public void CleanupStagingTolerantOfMissingDirectory()
        {
            // CleanupStaging should be safe to call before BuildStagedPackage and idempotent
            // when called a second time after the staging folder is already gone.
            var handle = new PackageDownloadHandle();
            Assert.DoesNotThrow(() => handle.CleanupStaging());

            var sourceDir = Path.Combine(TestDirectory, "pkgs", "EvenOdd");
            var installDirectory = Path.Combine(scratchDirectory, "packages");
            Directory.CreateDirectory(installDirectory);

            var stagedHandle = CreateHandleForPackage(sourceDir, "EvenOdd");
            var stagedPkg = stagedHandle.BuildStagedPackage(CurrentDynamoModel, installDirectory);
            Assert.IsNotNull(stagedPkg);

            stagedHandle.CleanupStaging();
            Assert.DoesNotThrow(() => stagedHandle.CleanupStaging());
        }

        [Test]
        public void FinalizeExtractionWithoutStageThrows()
        {
            var handle = new PackageDownloadHandle();
            // No BuildStagedPackage call — staging state is unset, so FinalizeExtraction must fail
            // loudly rather than silently doing nothing.
            Assert.Throws<InvalidOperationException>(() => handle.FinalizeExtraction(new Package(
                directory: Path.Combine(scratchDirectory, "ignored"),
                name: "Ignored",
                versionName: "1.0.0",
                license: string.Empty)));
        }
    }
}
