using System;
using System.IO;
using Dynamo;
using Dynamo.Graph.Workspaces.Locking;
using NUnit.Framework;

namespace Dynamo.Tests.Graph.Workspaces.Locking
{
    [TestFixture]
    internal class GraphLockManagerTests : DynamoModelTestBase
    {
        [Test]
        public void TryAcquireReturnsCancelledWhenPromptCancels()
        {
            var graphPath = CreateGraphWithForeignLock(DateTime.UtcNow);
            var prompt = new FakePrompt(GraphLockUserResponse.Cancel());
            using (var manager = new GraphLockManager(CurrentDynamoModel, prompt, forceEnable: true))
            {
                var result = manager.TryAcquire(graphPath, true);

                Assert.AreEqual(GraphLockConflict.Cancelled, result.Conflict);
                Assert.IsNotNull(prompt.LastExistingLock);
                Assert.IsFalse(prompt.LastWasStale);
            }
        }

        [Test]
        public void TryAcquireCopiesGraphWhenPromptChoosesSaveAs()
        {
            var graphPath = CreateGraphWithForeignLock(DateTime.UtcNow);
            File.WriteAllText(graphPath, "{\"hello\":\"world\"}");
            var saveAsPath = Path.Combine(TempFolder, "copy.dyn");
            var prompt = new FakePrompt(GraphLockUserResponse.SaveAs(saveAsPath));
            using (var manager = new GraphLockManager(CurrentDynamoModel, prompt, heartbeatMilliseconds: 1000, forceEnable: true))
            {
                var result = manager.TryAcquire(graphPath, true);

                Assert.AreEqual(GraphLockConflict.Acquired, result.Conflict);
                Assert.AreEqual(saveAsPath, result.GraphPath);
                Assert.AreEqual(File.ReadAllText(graphPath), File.ReadAllText(saveAsPath));
                Assert.IsTrue(File.Exists(GraphLockFile.PathFor(saveAsPath)));

                manager.CompleteOpen(saveAsPath, false);
            }
        }

        [Test]
        public void CompleteOpenReleasesAcquiredLockWhenOpenFails()
        {
            var graphPath = Path.Combine(TempFolder, "failed.dyn");
            File.WriteAllText(graphPath, "{}");
            using (var manager = new GraphLockManager(CurrentDynamoModel, forceEnable: true))
            {
                var result = manager.TryAcquire(graphPath, true);

                manager.CompleteOpen(graphPath, false);

                Assert.AreEqual(GraphLockConflict.Acquired, result.Conflict);
                Assert.IsFalse(File.Exists(GraphLockFile.PathFor(graphPath)));
            }
        }

        private string CreateGraphWithForeignLock(DateTime lastHeartbeatUtc)
        {
            var graphPath = Path.Combine(TempFolder, Guid.NewGuid() + ".dyn");
            File.WriteAllText(graphPath, "{}");
            var sidecarPath = GraphLockFile.PathFor(graphPath);
            GraphLockFile.TryCreateExclusive(sidecarPath, new GraphLockInfo
            {
                SchemaVersion = 1,
                SessionId = Guid.NewGuid(),
                GraphPath = graphPath,
                MachineName = "other-machine",
                ProcessId = 123456,
                ProcessStartUtc = DateTime.UtcNow.AddHours(-1),
                LastHeartbeatUtc = lastHeartbeatUtc
            });

            return graphPath;
        }

        private sealed class FakePrompt : IGraphLockUserPrompt
        {
            private readonly GraphLockUserResponse response;

            internal FakePrompt(GraphLockUserResponse response)
            {
                this.response = response;
            }

            internal GraphLockInfo LastExistingLock { get; private set; }

            internal bool LastWasStale { get; private set; }

            public GraphLockUserResponse AskUser(string graphPath, GraphLockInfo existingLock, bool isStale)
            {
                LastExistingLock = existingLock;
                LastWasStale = isStale;
                return response;
            }
        }
    }
}
