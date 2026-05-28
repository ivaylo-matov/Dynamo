using System;
using System.IO;
using System.Linq;
using Dynamo.Graph.Workspaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Dynamo.Tests.Graph.Workspaces
{
    [TestFixture]
    internal class GraphLockServiceTests : UnitTestBase
    {
        [Test]
        [Category("UnitTests")]
        public void WhenLockDoesNotExistThenAcquireCreatesSidecar()
        {
            var graphPath = GetNewFileNameOnTempPath();
            File.WriteAllText(graphPath, "{}");

            using (var lockService = CreateLockService())
            {
                var result = lockService.TryAcquireLock(graphPath);

                Assert.AreEqual(GraphLockAcquisitionStatus.Acquired, result.Status);
                Assert.IsTrue(File.Exists(GraphLockService.GetLockFilePath(result.GraphPath)));

                var lockData = ReadLockData(result.GraphPath);
                Assert.AreEqual(GraphLockService.CurrentSchemaVersion, lockData.SchemaVersion);
                Assert.AreNotEqual(Guid.Empty, lockData.SessionId);
                Assert.AreEqual(GraphLockService.GetCanonicalGraphPath(graphPath), lockData.GraphPath);
                Assert.AreEqual(Environment.UserName, lockData.UserName);
                Assert.AreEqual(Environment.MachineName, lockData.MachineName);
                Assert.Greater(lockData.ProcessId, 0);
                Assert.AreNotEqual(DateTime.MinValue, lockData.ProcessStartUtc);
                Assert.AreEqual("3.6.1", lockData.DynamoVersion);
                Assert.AreEqual("3.6", lockData.DynamoMajorMinor);
                Assert.AreNotEqual(DateTime.MinValue, lockData.AcquiredUtc);
                Assert.AreEqual(lockData.AcquiredUtc, lockData.LastHeartbeatUtc);
                CollectionAssert.AreEqual(GetExpectedLockPropertyNames(), ReadLockPropertyNames(result.GraphPath));
            }
        }

        [Test]
        [Category("UnitTests")]
        public void WhenExistingLockHasLiveHeartbeatThenAcquireReportsLiveLock()
        {
            var graphPath = GetNewFileNameOnTempPath();
            File.WriteAllText(graphPath, "{}");

            using (var owningLockService = CreateLockService())
            using (var secondLockService = CreateLockService())
            {
                owningLockService.TryAcquireLock(graphPath);

                var result = secondLockService.TryAcquireLock(graphPath);

                Assert.AreEqual(GraphLockAcquisitionStatus.LockedByLiveSession, result.Status);
                Assert.IsNotNull(result.ExistingLock);
                Assert.AreEqual(Environment.UserName, result.ExistingLock.UserName);
                Assert.AreEqual(Environment.MachineName, result.ExistingLock.MachineName);
            }
        }

        [Test]
        [Category("UnitTests")]
        public void WhenExistingLockIsStaleThenAcquireReportsStaleLock()
        {
            var graphPath = GetNewFileNameOnTempPath();
            File.WriteAllText(graphPath, "{}");
            WriteStaleLock(graphPath);

            using (var lockService = CreateLockService())
            {
                var result = lockService.TryAcquireLock(graphPath);

                Assert.AreEqual(GraphLockAcquisitionStatus.StaleLock, result.Status);
                Assert.IsNotNull(result.ExistingLock);
            }
        }

        [Test]
        [Category("UnitTests")]
        public void WhenForceAcquireStaleLockThenSidecarIsOverwritten()
        {
            var graphPath = GetNewFileNameOnTempPath();
            File.WriteAllText(graphPath, "{}");
            WriteStaleLock(graphPath);

            using (var lockService = CreateLockService())
            {
                var result = lockService.TryAcquireLock(graphPath, true);

                Assert.AreEqual(GraphLockAcquisitionStatus.Acquired, result.Status);

                var lockData = ReadLockData(result.GraphPath);
                Assert.AreEqual(Environment.UserName, lockData.UserName);
                Assert.AreEqual(Environment.MachineName, lockData.MachineName);
                Assert.AreEqual("3.6.1", lockData.DynamoVersion);
            }
        }

        [Test]
        [Category("UnitTests")]
        public void WhenLockIsReleasedThenOwnedSidecarIsDeleted()
        {
            var graphPath = GetNewFileNameOnTempPath();
            File.WriteAllText(graphPath, "{}");

            using (var lockService = CreateLockService())
            {
                var result = lockService.TryAcquireLock(graphPath);
                Assert.IsTrue(File.Exists(GraphLockService.GetLockFilePath(result.GraphPath)));

                lockService.ReleaseLock(graphPath);

                Assert.IsFalse(File.Exists(GraphLockService.GetLockFilePath(result.GraphPath)));
            }
        }

        private static GraphLockService CreateLockService()
        {
            return new GraphLockService(
                "3.6.1",
                heartbeatInterval: TimeSpan.FromMilliseconds(100),
                registerProcessExit: false);
        }

        private static GraphLockData ReadLockData(string graphPath)
        {
            return JsonConvert.DeserializeObject<GraphLockData>(
                File.ReadAllText(GraphLockService.GetLockFilePath(graphPath)));
        }

        private static void WriteStaleLock(string graphPath)
        {
            var canonicalGraphPath = GraphLockService.GetCanonicalGraphPath(graphPath);
            var lockData = new GraphLockData
            {
                SchemaVersion = GraphLockService.CurrentSchemaVersion,
                SessionId = Guid.NewGuid(),
                GraphPath = canonicalGraphPath,
                UserName = "stale-user",
                MachineName = "stale-host",
                ProcessId = 123,
                ProcessStartUtc = DateTime.UtcNow.AddHours(-2),
                DynamoVersion = "3.5.0",
                DynamoMajorMinor = "3.5",
                AcquiredUtc = DateTime.UtcNow.AddHours(-2),
                LastHeartbeatUtc = DateTime.UtcNow.AddHours(-1)
            };

            File.WriteAllText(
                GraphLockService.GetLockFilePath(canonicalGraphPath),
                JsonConvert.SerializeObject(lockData));
        }

        private static string[] ReadLockPropertyNames(string graphPath)
        {
            return JObject.Parse(File.ReadAllText(GraphLockService.GetLockFilePath(graphPath)))
                .Properties()
                .Select(property => property.Name)
                .ToArray();
        }

        private static string[] GetExpectedLockPropertyNames()
        {
            return new[]
            {
                "schemaVersion",
                "sessionId",
                "graphPath",
                "userName",
                "machineName",
                "processId",
                "processStartUtc",
                "dynamoVersion",
                "dynamoMajorMinor",
                "acquiredUtc",
                "lastHeartbeatUtc"
            };
        }
    }
}
