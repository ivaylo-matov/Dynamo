using System;
using System.Collections.Generic;
using System.Linq;
using CoreNodeModels;
using Dynamo.Graph;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using NUnit.Framework;
using DynCmd = Dynamo.Models.DynamoModel;

namespace Dynamo.Tests.ModelsTest
{
    [TestFixture]
    class WatchNodeDeletionTests : DynamoModelTestBase
    {
        protected override void GetLibrariesToPreload(List<string> libraries)
        {
            libraries.Add("ProtoGeometry.dll");
            libraries.Add("DesignScriptBuiltin.dll");
            libraries.Add("DSCoreNodes.dll");

            base.GetLibrariesToPreload(libraries);
        }

        [Test]
        [Category("UnitTests")]
        public void DeleteInlineWatchNodeKeepsDownstreamConnections()
        {
            var graph = CreateInlineWatchGraph();

            var preservedConnectorGuids = new HashSet<Guid>
            {
                graph.DownstreamConnectorA.GUID,
                graph.DownstreamConnectorB.GUID
            };

            CurrentDynamoModel.DeleteModelInternal(new List<ModelBase> { graph.WatchNode });

            var workspace = CurrentDynamoModel.CurrentWorkspace;
            Assert.IsNull(workspace.Nodes.FirstOrDefault(node => node.GUID == graph.WatchNode.GUID));

            var remainingConnectors = workspace.Connectors.ToList();
            Assert.AreEqual(2, remainingConnectors.Count);

            Assert.IsTrue(remainingConnectors.All(connector => connector.Start.Owner.GUID == graph.UpstreamNode.GUID));
            Assert.IsTrue(remainingConnectors.Any(connector => connector.End.Owner.GUID == graph.DownstreamNodeA.GUID));
            Assert.IsTrue(remainingConnectors.Any(connector => connector.End.Owner.GUID == graph.DownstreamNodeB.GUID));
            CollectionAssert.AreEquivalent(
                preservedConnectorGuids,
                remainingConnectors.Select(connector => connector.GUID).ToList());
        }

        [Test]
        [Category("UnitTests")]
        public void DeleteInlineWatchNodeUndoRedoRestoresAndReappliesRewiring()
        {
            var graph = CreateInlineWatchGraph();

            CurrentDynamoModel.DeleteModelInternal(new List<ModelBase> { graph.WatchNode });

            var workspace = CurrentDynamoModel.CurrentWorkspace;
            Assert.AreEqual(2, workspace.Connectors.Count());

            workspace.Undo();

            var watchAfterUndo = workspace.Nodes.FirstOrDefault(node => node.GUID == graph.WatchNode.GUID);
            Assert.IsNotNull(watchAfterUndo);
            Assert.AreEqual(3, workspace.Connectors.Count());

            var connectorsAfterUndo = workspace.Connectors.ToList();
            Assert.IsTrue(connectorsAfterUndo.Any(connector =>
                connector.Start.Owner.GUID == graph.UpstreamNode.GUID &&
                connector.End.Owner.GUID == graph.WatchNode.GUID));

            Assert.IsTrue(connectorsAfterUndo.Any(connector =>
                connector.GUID == graph.DownstreamConnectorA.GUID &&
                connector.Start.Owner.GUID == graph.WatchNode.GUID &&
                connector.End.Owner.GUID == graph.DownstreamNodeA.GUID));

            Assert.IsTrue(connectorsAfterUndo.Any(connector =>
                connector.GUID == graph.DownstreamConnectorB.GUID &&
                connector.Start.Owner.GUID == graph.WatchNode.GUID &&
                connector.End.Owner.GUID == graph.DownstreamNodeB.GUID));

            workspace.Redo();

            var connectorsAfterRedo = workspace.Connectors.ToList();
            Assert.IsNull(workspace.Nodes.FirstOrDefault(node => node.GUID == graph.WatchNode.GUID));
            Assert.AreEqual(2, connectorsAfterRedo.Count);

            Assert.IsTrue(connectorsAfterRedo.Any(connector =>
                connector.GUID == graph.DownstreamConnectorA.GUID &&
                connector.Start.Owner.GUID == graph.UpstreamNode.GUID &&
                connector.End.Owner.GUID == graph.DownstreamNodeA.GUID));

            Assert.IsTrue(connectorsAfterRedo.Any(connector =>
                connector.GUID == graph.DownstreamConnectorB.GUID &&
                connector.Start.Owner.GUID == graph.UpstreamNode.GUID &&
                connector.End.Owner.GUID == graph.DownstreamNodeB.GUID));
        }

        [Test]
        [Category("UnitTests")]
        public void DeleteWatchNodeWithoutDownstreamConnectionsDoesNotTriggerAutoRun()
        {
            var workspace = GetHomeWorkspace();
            workspace.RunSettings.RunType = RunType.Manual;

            var graph = CreateWatchSinkGraphWithoutDownstream();

            workspace.RunSettings.RunType = RunType.Automatic;
            var evaluationCountBeforeDelete = workspace.EvaluationCount;

            CurrentDynamoModel.DeleteModelInternal(new List<ModelBase> { graph.WatchNode });

            Assert.AreEqual(evaluationCountBeforeDelete, workspace.EvaluationCount);
        }

        [Test]
        [Category("UnitTests")]
        public void DeleteWatchNodeWithRewiredDownstreamConnectionsDoesNotTriggerAutoRun()
        {
            var workspace = GetHomeWorkspace();
            workspace.RunSettings.RunType = RunType.Manual;

            var graph = CreateInlineWatchGraph();

            workspace.RunSettings.RunType = RunType.Automatic;
            var evaluationCountBeforeDelete = workspace.EvaluationCount;

            CurrentDynamoModel.DeleteModelInternal(new List<ModelBase> { graph.WatchNode });

            Assert.AreEqual(evaluationCountBeforeDelete, workspace.EvaluationCount);
            Assert.IsTrue(graph.DownstreamNodeA.IsModified);
            Assert.IsTrue(graph.DownstreamNodeB.IsModified);
        }

        private (NodeModel UpstreamNode, NodeModel DownstreamNodeA, NodeModel DownstreamNodeB, Watch WatchNode, ConnectorModel DownstreamConnectorA, ConnectorModel DownstreamConnectorB)
            CreateInlineWatchGraph()
        {
            var upstreamNode = CreateCodeBlockNode();
            UpdateCodeBlockNodeContent(upstreamNode, "1;");

            var downstreamNodeA = CreateCodeBlockNode();
            UpdateCodeBlockNodeContent(downstreamNodeA, "x;");

            var downstreamNodeB = CreateCodeBlockNode();
            UpdateCodeBlockNodeContent(downstreamNodeB, "y;");

            var watchNode = new Watch();
            CurrentDynamoModel.ExecuteCommand(new DynCmd.CreateNodeCommand(watchNode, 0, 0, true, false));

            var upstreamToWatch = ConnectorModel.Make(upstreamNode, watchNode, 0, 0);
            var downstreamConnectorA = ConnectorModel.Make(watchNode, downstreamNodeA, 0, 0);
            var downstreamConnectorB = ConnectorModel.Make(watchNode, downstreamNodeB, 0, 0);

            Assert.IsNotNull(upstreamToWatch);
            Assert.IsNotNull(downstreamConnectorA);
            Assert.IsNotNull(downstreamConnectorB);

            return (upstreamNode, downstreamNodeA, downstreamNodeB, watchNode, downstreamConnectorA, downstreamConnectorB);
        }

        private (NodeModel UpstreamNode, Watch WatchNode) CreateWatchSinkGraphWithoutDownstream()
        {
            var upstreamNode = CreateCodeBlockNode();
            UpdateCodeBlockNodeContent(upstreamNode, "1;");

            var watchNode = new Watch();
            CurrentDynamoModel.ExecuteCommand(new DynCmd.CreateNodeCommand(watchNode, 0, 0, true, false));

            var upstreamToWatch = ConnectorModel.Make(upstreamNode, watchNode, 0, 0);
            Assert.IsNotNull(upstreamToWatch);

            return (upstreamNode, watchNode);
        }

        private HomeWorkspaceModel GetHomeWorkspace()
        {
            var homeWorkspace = CurrentDynamoModel.CurrentWorkspace as HomeWorkspaceModel;
            Assert.IsNotNull(homeWorkspace);
            return homeWorkspace;
        }
    }
}
