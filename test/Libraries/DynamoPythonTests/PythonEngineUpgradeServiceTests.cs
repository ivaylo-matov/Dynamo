using System;
using System.Collections.Generic;
using System.Linq;
using Dynamo.Engine;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Nodes.CustomNodes;
using Dynamo.Graph.Nodes.NodeLoaders;
using Dynamo.Graph.Notes;
using Dynamo.Graph.Presets;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Models.Migration.Python;
using Dynamo.PythonServices;
using NUnit.Framework;
using PythonNodeModels;

namespace DynamoPythonTests
{
    [TestFixture]
    public class PythonEngineUpgradeServiceTests
    {
        [Test]
        public void DetectPythonUsage_FindsNestedCustomNodes()
        {
            var scenario = CreateNestedScenario();

            var usage = scenario.Service.DetectPythonUsage(scenario.HostWorkspace, IsCPythonNode);

            CollectionAssert.Contains(usage.CustomNodeDefIdsWithPython, scenario.InnerDefinitionId);
            CollectionAssert.Contains(usage.CustomNodeDefIdsWithPython, scenario.OuterDefinitionId);
            Assert.AreEqual(0, usage.DirectPythonNodes.Count());
        }

        [Test]
        public void NestedCustomNodes_AreUpgraded()
        {
            var scenario = CreateNestedScenario();

            var usage = scenario.Service.DetectPythonUsage(scenario.HostWorkspace, IsCPythonNode);

            var visited = new HashSet<Guid>();
            foreach (var defId in usage.CustomNodeDefIdsWithPython)
            {
                UpgradeCustomNodeGraph(defId, scenario, visited);
            }

            Assert.AreEqual(PythonEngineManager.PythonNet3EngineName, scenario.InnerPythonNode.EngineName);
            CollectionAssert.Contains(scenario.Service.TempMigratedCustomDefs, scenario.InnerDefinitionId);
        }

        private static void UpgradeCustomNodeGraph(Guid defId, NestedScenario scenario, HashSet<Guid> visited)
        {
            if (!visited.Add(defId)) return;
            if (!scenario.WorkspaceLookup.TryGetValue(defId, out var workspace)) return;

            var usage = scenario.Service.DetectPythonUsage(workspace, IsCPythonNode);

            if (usage.DirectPythonNodes.Any())
            {
                scenario.Service.TempMigratedCustomDefs.Add(defId);
                scenario.Service.UpgradeNodesInMemory(
                    usage.DirectPythonNodes,
                    workspace,
                    SimpleSetEngine);
            }

            foreach (var nested in usage.CustomNodeDefIdsWithPython)
            {
                UpgradeCustomNodeGraph(nested, scenario, visited);
            }
        }

        private static void SimpleSetEngine(NodeModel node, WorkspaceModel workspace)
        {
            if (node is PythonNodeBase py && py.EngineName == PythonEngineManager.CPython3EngineName)
            {
                py.EngineName = PythonEngineManager.PythonNet3EngineName;
            }
        }

        private static bool IsCPythonNode(NodeModel node)
        {
            return node is PythonNodeBase py &&
                   py.EngineName == PythonEngineManager.CPython3EngineName;
        }

        private static NestedScenario CreateNestedScenario()
        {
            var innerDefId = Guid.NewGuid();
            var innerPythonNode = new PythonNode
            {
                EngineName = PythonEngineManager.CPython3EngineName
            };
            var innerWorkspace = CreateWorkspace(innerDefId, "Inner", new NodeModel[] { innerPythonNode });
            var innerDefinition = new CustomNodeDefinition(innerDefId, "Inner", innerWorkspace.Nodes);

            var outerDefId = Guid.NewGuid();
            var nestedFunction = new Function(innerDefinition, "Inner", string.Empty, string.Empty);
            var outerWorkspace = CreateWorkspace(outerDefId, "Outer", new NodeModel[] { nestedFunction });
            var outerDefinition = new CustomNodeDefinition(outerDefId, "Outer", outerWorkspace.Nodes);

            var hostFunction = new Function(outerDefinition, "Outer", string.Empty, string.Empty);
            var hostWorkspace = CreateWorkspace(Guid.NewGuid(), "Host", new NodeModel[] { hostFunction });

            var lookup = new Dictionary<Guid, CustomNodeWorkspaceModel>
            {
                { innerDefId, innerWorkspace },
                { outerDefId, outerWorkspace }
            };

            var service = new PythonEngineUpgradeService(
                dynamoModel: null,
                pathManager: null,
                functionWorkspaceResolver: guid =>
                {
                    lookup.TryGetValue(guid, out var workspace);
                    return workspace;
                });

            return new NestedScenario
            {
                HostWorkspace = hostWorkspace,
                InnerPythonNode = innerPythonNode,
                InnerDefinitionId = innerDefId,
                OuterDefinitionId = outerDefId,
                Service = service,
                WorkspaceLookup = lookup
            };
        }

        private static CustomNodeWorkspaceModel CreateWorkspace(
            Guid id,
            string name,
            IEnumerable<NodeModel> nodes)
        {
            var info = new WorkspaceInfo(id.ToString(), name, string.Empty, RunType.Manual);
            return new CustomNodeWorkspaceModel(
                new NodeFactory(),
                nodes,
                Enumerable.Empty<NoteModel>(),
                Enumerable.Empty<AnnotationModel>(),
                Enumerable.Empty<PresetModel>(),
                new ElementResolver(),
                info);
        }

        private sealed class NestedScenario
        {
            internal WorkspaceModel HostWorkspace { get; init; }
            internal PythonNode InnerPythonNode { get; init; }
            internal Guid InnerDefinitionId { get; init; }
            internal Guid OuterDefinitionId { get; init; }
            internal PythonEngineUpgradeService Service { get; init; }
            internal Dictionary<Guid, CustomNodeWorkspaceModel> WorkspaceLookup { get; init; }
        }
    }
}
