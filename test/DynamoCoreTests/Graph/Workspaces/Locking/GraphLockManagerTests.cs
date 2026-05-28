using System;
using System.IO;
using Dynamo.Graph.Workspaces.Locking;
using NUnit.Framework;

namespace Dynamo.Tests.Graph.Workspaces.Locking
{
    [TestFixture]
    internal class GraphLockManagerTests : DynamoModelTestBase
    {
        [Test]
        public void TryAcquireReturnsReadOnlyWhenPromptChoosesReadOnly()
        {
            var graphPath = CreateGraphWithForeignLock(DateTime.UtcNow);
            var prompt = new FakePrompt(GraphLockUserDecision.ReadOnly);
            using (var manager = new GraphLockManager(CurrentDynamoModel, prompt, forceEnable: true))
            {
                var result = manager.TryAcquire(graphPath, true);

                Assert.AreEqual(GraphLockConflict.ReadOnly, result.Conflict);
                Assert.IsTrue(result.ShouldOpenReadOnly);
                Assert.IsNotNull(prompt.LastExistingLock);
                Assert.IsFalse(prompt.LastWasStale);
            }
        }

        [Test]
        public void TryAcquireOverwritesStaleLockWhenPromptChoosesTakeover()
        {
            var graphPath = CreateGraphWithForeignLock(DateTime.UtcNow.AddMinutes(-10));
            var prompt = new FakePrompt(GraphLockUserDecision.Takeover);
            using (var manager = new GraphLockManager(CurrentDynamoModel, prompt, heartbeatMilliseconds: 1000, forceEnable: true))
            {
                var result = manager.TryAcquire(graphPath, true);
                GraphLockFile.TryRead(GraphLockFile.PathFor(graphPath), out var info);

                Assert.AreEqual(GraphLockConflict.Acquired, result.Conflict);
                Assert.IsTrue(prompt.LastWasStale);
                Assert.AreEqual(Environment.ProcessId, info.ProcessId);

                manager.CompleteOpen(graphPath, false);
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
                UserName = "other-user",
                MachineName = "other-machine",
                ProcessId = 123456,
                ProcessStartUtc = DateTime.UtcNow.AddHours(-1),
                DynamoVersion = "4.1.0.1234",
                DynamoMajorMinor = "4.1",
                AcquiredUtc = DateTime.UtcNow.AddHours(-1),
                LastHeartbeatUtc = lastHeartbeatUtc
            });

            return graphPath;
        }

        private sealed class FakePrompt : IGraphLockUserPrompt
        {
            private readonly GraphLockUserDecision decision;

            internal FakePrompt(GraphLockUserDecision decision)
            {
                this.decision = decision;
            }

            internal GraphLockInfo LastExistingLock { get; private set; }

            internal bool LastWasStale { get; private set; }

            public GraphLockUserDecision AskUser(string graphPath, GraphLockInfo existingLock, bool isStale)
            {
                LastExistingLock = existingLock;
                LastWasStale = isStale;
                return decision;
            }
        }
    }
}
