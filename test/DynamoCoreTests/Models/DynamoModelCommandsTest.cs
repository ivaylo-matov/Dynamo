using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using CoreNodeModels;
using Dynamo.Graph;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Nodes.ZeroTouch;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Selection;
using Dynamo.Utilities;
using NUnit.Framework;
using static Dynamo.Models.DynamoModel;

namespace Dynamo.Tests.ModelsTest
{
    /// <summary>
    /// This test class will contain test method for executing methods in the DynamoModel class
    /// </summary>
    [TestFixture]
    class DynamoModelCommandsTest : DynamoModelTestBase
    {
        private static readonly Guid InlineWatchFixtureWatchNodeGuid =
            Guid.Parse("18670ca53bc84350afecb263e3c44dfa");
        private static readonly Guid InlineWatchFixtureUpstreamNodeGuid =
            Guid.Parse("55224bcc59764cfea08b07931dc75aea");
        private static readonly Guid InlineWatchFixtureDownstreamNodeAGuid =
            Guid.Parse("bf96a26cf0d44bcc9fac21b2233e1bf4");
        private static readonly Guid InlineWatchFixtureDownstreamNodeBGuid =
            Guid.Parse("d87460d55bbb4bc2b3c09841ceaa4335");

        private static readonly Guid InlineWatchFixtureIncomingConnectorGuid =
            Guid.Parse("006159bed10d41db80438a9e4c9d0640");
        private static readonly Guid InlineWatchFixtureDownstreamConnectorAGuid =
            Guid.Parse("7d1ac604ba724e3ba281c3e6742d6aba");
        private static readonly Guid InlineWatchFixtureDownstreamConnectorBGuid =
            Guid.Parse("15ee5859ae2845649ebe0ac8bcc70157");

        /// <summary>
        /// This test method will execute the ForceRunCancelImpl method from the DynamoModel class
        /// </summary>
        [Test]
        [Category("UnitTests")]
        public void ForceRunCancelImplTest()
        {
            //Arrange
            Guid newNodeGuid = Guid.NewGuid();
            var command = new DynamoModel.ForceRunCancelCommand(true, true);
   
            //Act
            CurrentDynamoModel.ExecuteCommand(command);

            //Assert
            Assert.IsNotNull(command);
        }

        /// <summary>
        /// This test method will execute the CreateAndConnectNodeImpl method from the DynamoModel class
        /// </summary>
        [Test]
        [Category("UnitTests")]
        public void CreateAndConnectNodeImplTest()
        {
            //Arrange
            Guid newNodeGuid = Guid.NewGuid();
            //This guid belongs to a CoreNodeModels.CreateList node inside the .dyn file
            Guid existingNodeGuid = new Guid("81c94fd0-35a0-4680-8535-00aff41192d3");
            double x = 100;
            double y = 190;

            string openPath = Path.Combine(TestDirectory, @"core\DetailedPreviewMargin_Test.dyn");
            RunModel(openPath);

            //This command is created with CreateAsDownstreamNode flag as true
            var cmdOne = new DynamoModel.CreateAndConnectNodeCommand(newNodeGuid, existingNodeGuid,
                "CoreNodeModels.CreateList", 0, 0, x, y, true, false);

            //This command is created with CreateAsDownstreamNode flag as false
            var cmdTwo = new DynamoModel.CreateAndConnectNodeCommand(newNodeGuid, existingNodeGuid,
                "CoreNodeModels.CreateList", 0, 0, x, y, false, false);

            //Act
            //This will execute the method CreateAndConnectNodeImpl for both commands
            CurrentDynamoModel.ExecuteCommand(cmdOne);
            CurrentDynamoModel.ExecuteCommand(cmdTwo);

            //Assert
            Assert.IsNotNull(cmdOne);
            Assert.IsNotNull(cmdTwo);
        }

        /// <summary>
        /// This test method will execute the method GetNodeFromCommand from the DynamoModel class
        /// The method has several flows then this test method will follow several flows
        /// </summary>
        [Test]
        [Category("UnitTests")]
        public void GetNodeFromCommandTest()
        {
            //Arrange
            Guid newNodeGuid = Guid.NewGuid();

            var command = new DynamoModel.CreateNodeCommand(newNodeGuid.ToString(), "NodeName1", 0, 0, true, true);
            var command2 = new DynamoModel.CreateNodeCommand(newNodeGuid.ToString(), "CoreNodeModels.Watch", 0, 0, true, true);

            //This will execute the proxy section code in the GetNodeFromCommand() method
            var commandProxy = new DynamoModel.CreateProxyNodeCommand(newNodeGuid.ToString(), "CoreNodeModels.Watch", 0, 0, true, true,"ProxyCommand1",0,0);

            //Assert
            //Calling the ExecuteCommand internally executes the GetNodeFromCommand() method
            Assert.Throws<Exception>( () => CurrentDynamoModel.ExecuteCommand(command));
            CurrentDynamoModel.ExecuteCommand(command2);
            Assert.Throws<Exception>(() => CurrentDynamoModel.ExecuteCommand(commandProxy));
            
        }

        /// <summary>
        /// This test method will execute the method GetNodeFromCommand from the DynamoModel class
        /// It will follow the flow for when there is duplicate guid/name in the workspace
        /// </summary> 
        [Test]
        [Category("UnitTests")]
        public void GetNodeFromCommand_DuplicatedGuidsTest()
        {
            //Arrange
            //This guid belongs to a CoreNodeModels.CreateList node inside the .dyn file
            Guid existingNodeGuid = new Guid("81c94fd0-35a0-4680-8535-00aff41192d3");

            string openPath = Path.Combine(TestDirectory, @"core\DetailedPreviewMargin_Test.dyn");
            RunModel(openPath);

            //When a CreateNodeCommand is executed internally calls the GetNodeFromCommand() method
            var command = new DynamoModel.CreateNodeCommand(existingNodeGuid.ToString(), "List.Create", 0, 0, true, true);  

            //Act
            CurrentDynamoModel.ExecuteCommand(command);

            //Assert
            //Validating that the NodeCommand was successfully created.
            Assert.IsNotNull(command);
            Assert.AreEqual(command.Name, "List.Create");
            Assert.AreEqual(command.ModelGuid.ToString(), existingNodeGuid.ToString());
        }

        /// <summary>
        /// This test method will execute the method GetNodeFromCommand from the DynamoModel class
        /// It will follow the flow for when the node provided is a XMl Element with one child
        /// </summary> 
        [Test]
        [Category("UnitTests")]
        public void GetNodeFromCommand_XMLTest()
        {
            //Arrange
            XmlDocument xmlDocument = new XmlDocument();
            XmlElement elemTest = xmlDocument.CreateElement("TestCommand");
            var helper = new XmlElementHelper(elemTest);
            //DeserializeCore method is looking for the attributes ShowErrors and CancelRun, then we need to set them up before calling the method.
            helper.SetAttribute("X", 100);
            helper.SetAttribute("Y", 100);
            helper.SetAttribute("DefaultPosition", true);
            helper.SetAttribute("TransformCoordinates", true);
            helper.SetAttribute("NodeName", "TestNode");
            helper.SetAttribute("type", "Dynamo.Graph.Nodes.CodeBlockNodeModel");


            XmlElement elemTestChild = xmlDocument.CreateElement("TestCommandChild");
            var helper2 = new XmlElementHelper(elemTestChild);
            helper2.SetAttribute("type", "Dynamo.Graph.Nodes.CodeBlockNodeModel");
            helper2.SetAttribute("ShouldFocus", true);
            helper2.SetAttribute("CodeText", "text");
            elemTest.AppendChild(elemTestChild);

            //Act
            var createNodeCommand = DynamoModel.CreateNodeCommand.DeserializeCore(elemTest);
            //Calling Execute method will call internally the GetNodeFromCommand method
            createNodeCommand.Execute(CurrentDynamoModel);

            //Assert
            //Validating that the creageNodeCommand was successful
            Assert.IsNotNull(createNodeCommand);
            Assert.AreEqual(createNodeCommand.NodeXml.Name, "TestCommandChild");
        }

        /// <summary>
        /// This test method will execute the SelectModelImpl method from the DynamoModel class
        /// </summary>
        [Test]
        [Category("UnitTests")]
        public void SelectModelImplTest()
        {
            //Arrange
            string openPath = Path.Combine(TestDirectory, @"core\DetailedPreviewMargin_Test.dyn");
            RunModel(openPath);

            //This will add a new DSFunction node to the current workspace
            var addNode = new DSFunction(CurrentDynamoModel.LibraryServices.GetFunctionDescriptor("+"));
            CurrentDynamoModel.CurrentWorkspace.AddAndRegisterNode(addNode, false);

            //Select the node created (DSFunction)
            DynamoSelection.Instance.Selection.Add(addNode);
            var ids = DynamoSelection.Instance.Selection.OfType<NodeModel>().Select(x => x.GUID).ToList();
            var selectCommand = new DynamoModel.SelectModelCommand(ids,ModifierKeys.Shift);

            //Act
            CurrentDynamoModel.ExecuteCommand(selectCommand);

            //Assert
            Assert.Greater(ids.Count, 0); //At least one guid was found
            Assert.IsNotNull(selectCommand);
        }

        /// <summary>
        /// This test method will execute the SelectModelImpl method from the DynamoModel class and does not crash
        /// </summary>
        [Test]
        [Category("UnitTests")]
        public void SelectModelImplShouldNotCrashTest()
        {
            //Assert
            Assert.DoesNotThrow(
                () => {
                    //Insert a node GUID that does not exist in workspace
                    var ids = new System.Collections.Generic.List<Guid>() { Guid.NewGuid() };
                    var selectCommand = new DynamoModel.SelectModelCommand(ids, ModifierKeys.Shift);

                    //Act
                    CurrentDynamoModel.ExecuteCommand(selectCommand);
                });
        }

        /// <summary>
        /// This test method will execute the BeginDuplicateConnection method from the DynamoModel class
        /// </summary>
        [Test]
        [Category("UnitTests")]
        public void BeginDuplicateConnectionTest()
        {
            //Arrange
            string openPath = Path.Combine(TestDirectory, @"core\DetailedPreviewMargin_Test.dyn");
            RunModel(openPath);

            Guid id = Guid.NewGuid();
            //This guid belongs to a CoreNodeModels.CreateList node inside the .dyn file
            Guid existingNodeGuid = new Guid("81c94fd0-35a0-4680-8535-00aff41192d3");

            //Connection Command with one Input port using a new guid
            var connectionCommand = new DynamoModel.MakeConnectionCommand(id, 0,PortType.Input, DynamoModel.MakeConnectionCommand.Mode.BeginDuplicateConnection);

            //Connection Command with one Output port using a new guid
            var connectionCommand2 = new DynamoModel.MakeConnectionCommand(id, 0, PortType.Output, DynamoModel.MakeConnectionCommand.Mode.BeginDuplicateConnection);

            var addNode = new DSFunction(CurrentDynamoModel.LibraryServices.GetFunctionDescriptor("+"));
            CurrentDynamoModel.CurrentWorkspace.AddAndRegisterNode(addNode, false);
            //Connection Command using a guid from a DSFunction added previosuly
            var connectionCommand3 = new DynamoModel.MakeConnectionCommand(addNode.GUID, 0, PortType.Input, DynamoModel.MakeConnectionCommand.Mode.BeginDuplicateConnection);

            //Act
            //The ExecuteCommand method internally will execute the BeginDuplicateConnectionTest method
            CurrentDynamoModel.ExecuteCommand(connectionCommand);
            CurrentDynamoModel.ExecuteCommand(connectionCommand2);
            CurrentDynamoModel.ExecuteCommand(connectionCommand3);

            //Assert
            //Validating that the commands were created successfully
            Assert.IsNotNull(connectionCommand);
            Assert.IsNotNull(connectionCommand2);
            Assert.IsNotNull(connectionCommand3);
        }

        /// <summary>
        /// This test method will execute the next methods from the DynamoModel class
        /// private void UngroupModelImpl(UngroupModelCommand command)
        /// private void AddToGroupImpl(AddModelToGroupCommand command)
        /// </summary>
        [Test]
        [Category("UnitTests")]
        public void UngroupGrouplImplTest()
        {
            //Arrange
            var guid1 = Guid.Empty;
            var command1 = new AddModelToGroupCommand(guid1);
            var command2 = new UngroupModelCommand(guid1);

            //Act
            //Internally this will execute the AddToGroupImpl method
            command1.Execute(CurrentDynamoModel);
            //Internally this will execute the UngroupModelImpl method
            command2.Execute(CurrentDynamoModel);

            //Assert
            //Verify that the command was created successfully and that the guids match
            Assert.IsNotNull(command1);
            Assert.AreEqual(command1.ModelGuid, guid1);
            Assert.IsNotNull(command2);
            Assert.AreEqual(command2.ModelGuid, guid1);
        }

        /// <summary>
        /// This test method will execute the next methods from the DynamoModel class
        /// void CreatePresetStateImpl(AddPresetCommand command)
        /// </summary>
        [Test]
        [Category("UnitTests")]
        public void CreatePresetStateImplTest()
        {
            //Arrange
            string openPath = Path.Combine(TestDirectory, @"core\DetailedPreviewMargin_Test.dyn");
            RunModel(openPath);
            var guidState = Guid.NewGuid();

            //This will add a new DSFunction node to the current workspace
            var addNode = new DSFunction(CurrentDynamoModel.LibraryServices.GetFunctionDescriptor("+"));
            CurrentDynamoModel.CurrentWorkspace.AddAndRegisterNode(addNode, false);

            //Select the node created (DSFunction)
            DynamoSelection.Instance.Selection.Add(addNode);
            var ids = DynamoSelection.Instance.Selection.OfType<NodeModel>().Select(x => x.GUID).ToList();
            var addPresetCommand = new AddPresetCommand("PresetName", "Preset Description", ids);

            //Act
            addPresetCommand.Execute(CurrentDynamoModel);

            //Assert
            //Validates that the AddPresetCommand was created correctly
            Assert.IsNotNull(addPresetCommand);
            Assert.AreEqual(addPresetCommand.PresetStateName, "PresetName");
            Assert.AreEqual(addPresetCommand.PresetStateDescription, "Preset Description");
        }

        [Test]
        [Category("UnitTests")]
        public void DeleteInlineWatchNode_RewiresAndSuppressesAutoRun_FromFixture()
        {
            OpenInlineWatchDeleteFixtureGraph();
            var workspace = GetHomeWorkspace();

            workspace.RunSettings.RunType = RunType.Automatic;
            var evaluationCountBeforeDelete = workspace.EvaluationCount;

            var watchNode = GetNode<Watch>(InlineWatchFixtureWatchNodeGuid);
            CurrentDynamoModel.DeleteModelInternal(new List<ModelBase> { watchNode });

            Assert.AreEqual(evaluationCountBeforeDelete, workspace.EvaluationCount);
            Assert.IsNull(workspace.Nodes.FirstOrDefault(node => node.GUID == InlineWatchFixtureWatchNodeGuid));

            var connectorA = GetConnector(InlineWatchFixtureDownstreamConnectorAGuid);
            var connectorB = GetConnector(InlineWatchFixtureDownstreamConnectorBGuid);

            Assert.AreEqual(InlineWatchFixtureUpstreamNodeGuid, connectorA.Start.Owner.GUID);
            Assert.AreEqual(InlineWatchFixtureDownstreamNodeAGuid, connectorA.End.Owner.GUID);
            Assert.AreEqual(InlineWatchFixtureUpstreamNodeGuid, connectorB.Start.Owner.GUID);
            Assert.AreEqual(InlineWatchFixtureDownstreamNodeBGuid, connectorB.End.Owner.GUID);
        }

        [Test]
        [Category("UnitTests")]
        public void DeleteInlineWatchNode_RecreatesIncomingPinsOnFirstDownstream_FromFixture()
        {
            OpenInlineWatchDeleteFixtureGraph();
            var watchNode = GetNode<Watch>(InlineWatchFixtureWatchNodeGuid);

            var incomingConnector = GetConnector(InlineWatchFixtureIncomingConnectorGuid);
            incomingConnector.AddPin(new ConnectorPinModel(120.0, 220.0, Guid.NewGuid(), incomingConnector.GUID));
            incomingConnector.AddPin(new ConnectorPinModel(180.0, 260.0, Guid.NewGuid(), incomingConnector.GUID));

            CurrentDynamoModel.DeleteModelInternal(new List<ModelBase> { watchNode });

            var connectorA = GetConnector(InlineWatchFixtureDownstreamConnectorAGuid);
            var connectorB = GetConnector(InlineWatchFixtureDownstreamConnectorBGuid);

            Assert.AreEqual(2, connectorA.ConnectorPinModels.Count);
            Assert.AreEqual(0, connectorB.ConnectorPinModels.Count);
            Assert.IsTrue(connectorA.ConnectorPinModels.All(pin => pin.ConnectorId == connectorA.GUID));
            CollectionAssert.AreEquivalent(
                new[] { (120.0, 220.0), (180.0, 260.0) },
                connectorA.ConnectorPinModels.Select(pin => (pin.X, pin.Y)).ToList());
        }

        [Test]
        [Category("UnitTests")]
        public void UndoDeleteInlineWatchNode_RestoresWatchDataAndConnections_FromFixture()
        {
            OpenInlineWatchDeleteFixtureGraph();
            var workspace = GetHomeWorkspace();
            workspace.RunSettings.RunType = RunType.Manual;
            BeginRun();

            var evaluationCountAfterRun = workspace.EvaluationCount;
            var watchNode = GetNode<Watch>(InlineWatchFixtureWatchNodeGuid);
            CurrentDynamoModel.DeleteModelInternal(new List<ModelBase> { watchNode });

            workspace.Undo();

            Assert.AreEqual(evaluationCountAfterRun, workspace.EvaluationCount);

            var restoredWatch = GetNode<Watch>(InlineWatchFixtureWatchNodeGuid);
            Assert.IsTrue(restoredWatch.HasRunOnce);
            Assert.IsNotNull(restoredWatch.CachedValue);

            var incomingConnector = GetConnector(InlineWatchFixtureIncomingConnectorGuid);
            var connectorA = GetConnector(InlineWatchFixtureDownstreamConnectorAGuid);
            var connectorB = GetConnector(InlineWatchFixtureDownstreamConnectorBGuid);

            Assert.AreEqual(InlineWatchFixtureUpstreamNodeGuid, incomingConnector.Start.Owner.GUID);
            Assert.AreEqual(InlineWatchFixtureWatchNodeGuid, incomingConnector.End.Owner.GUID);
            Assert.AreEqual(InlineWatchFixtureWatchNodeGuid, connectorA.Start.Owner.GUID);
            Assert.AreEqual(InlineWatchFixtureWatchNodeGuid, connectorB.Start.Owner.GUID);
        }

        private void OpenInlineWatchDeleteFixtureGraph()
        {
            OpenModel(@"core\watch\DeleteInlineWatch_RewireFixture.dyn");
        }

        private HomeWorkspaceModel GetHomeWorkspace()
        {
            var workspace = CurrentDynamoModel.CurrentWorkspace as HomeWorkspaceModel;
            Assert.IsNotNull(workspace);
            return workspace;
        }

        private T GetNode<T>(Guid nodeGuid) where T : NodeModel
        {
            var node = CurrentDynamoModel.CurrentWorkspace.Nodes.FirstOrDefault(n => n.GUID == nodeGuid) as T;
            Assert.IsNotNull(node);
            return node;
        }

        private ConnectorModel GetConnector(Guid connectorGuid)
        {
            var connector = CurrentDynamoModel.CurrentWorkspace.Connectors.FirstOrDefault(c => c.GUID == connectorGuid);
            Assert.IsNotNull(connector);
            return connector;
        }
    }
}
