using System;
using System.IO;
using Dynamo;
using Dynamo.Graph.Workspaces.Locking;
using NUnit.Framework;

namespace Dynamo.Tests.Graph.Workspaces.Locking
{
    [TestFixture]
    internal class GraphLockFileTests : UnitTestBase
    {
        [Test]
        public void PathForCreatesHiddenDynLockBesideGraph()
        {
            var graphPath = Path.Combine(TempFolder, "sample.dyn");

            var sidecarPath = GraphLockFile.PathFor(graphPath);

            Assert.AreEqual(Path.Combine(TempFolder, ".sample.dyn.dynlock"), sidecarPath);
        }

        [Test]
        public void TryCreateExclusiveReturnsFalseWhenLockExists()
        {
            var graphPath = Path.Combine(TempFolder, "sample.dyn");
            var sidecarPath = GraphLockFile.PathFor(graphPath);
            var info = CreateInfo(graphPath);

            var firstResult = GraphLockFile.TryCreateExclusive(sidecarPath, info);
            var secondResult = GraphLockFile.TryCreateExclusive(sidecarPath, info);

            Assert.IsTrue(firstResult);
            Assert.IsFalse(secondResult);
        }

        [Test]
        public void TryReadIgnoresUnknownFields()
        {
            var graphPath = Path.Combine(TempFolder, "sample.dyn");
            var sidecarPath = GraphLockFile.PathFor(graphPath);
            File.WriteAllText(sidecarPath, @"{
  ""schemaVersion"": 1,
  ""sessionId"": ""8f617b24-9f18-4aa7-a62e-8f78bb7a7a9e"",
  ""graphPath"": ""sample.dyn"",
  ""userName"": ""user"",
  ""machineName"": ""machine"",
  ""processId"": 123,
  ""processStartUtc"": ""2026-05-28T00:00:00Z"",
  ""dynamoVersion"": ""4.1.0.1234"",
  ""dynamoMajorMinor"": ""4.1"",
  ""acquiredUtc"": ""2026-05-28T00:00:00Z"",
  ""lastHeartbeatUtc"": ""2026-05-28T00:00:01Z"",
  ""futureField"": ""ignored""
}");

            var result = GraphLockFile.TryRead(sidecarPath, out var info);

            Assert.IsTrue(result);
            Assert.AreEqual("4.1", info.DynamoMajorMinor);
        }

        [Test]
        public void WriteHeartbeatReplacesExistingLockInfo()
        {
            var graphPath = Path.Combine(TempFolder, "sample.dyn");
            var sidecarPath = GraphLockFile.PathFor(graphPath);
            var info = CreateInfo(graphPath);
            GraphLockFile.TryCreateExclusive(sidecarPath, info);
            var updatedHeartbeat = info.LastHeartbeatUtc.AddMinutes(1);
            info.LastHeartbeatUtc = updatedHeartbeat;

            GraphLockFile.WriteHeartbeat(sidecarPath, info);
            GraphLockFile.TryRead(sidecarPath, out var readInfo);

            Assert.AreEqual(updatedHeartbeat, readInfo.LastHeartbeatUtc);
        }

        private static GraphLockInfo CreateInfo(string graphPath)
        {
            var now = DateTime.UtcNow;
            return new GraphLockInfo
            {
                SchemaVersion = 1,
                SessionId = Guid.NewGuid(),
                GraphPath = graphPath,
                UserName = "user",
                MachineName = "machine",
                ProcessId = 123,
                ProcessStartUtc = now,
                DynamoVersion = "4.1.0.1234",
                DynamoMajorMinor = "4.1",
                AcquiredUtc = now,
                LastHeartbeatUtc = now
            };
        }
    }
}
