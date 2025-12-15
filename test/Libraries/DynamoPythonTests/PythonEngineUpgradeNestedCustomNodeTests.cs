using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using CoreNodeModels;
using Dynamo.Core;
using Dynamo.Models;
using Dynamo.PythonServices;
using Dynamo.Wpf.Extensions;
using DynamoUtilities;
using NUnit.Framework;

namespace DynamoPythonTests
{
    [TestFixture]
    public class PythonEngineUpgradeNestedCustomNodeTests : DynamoCoreWpfTests.DynamoTestUIBase
    {
        // Stable IDs used by the test graphs in test/core/python
        private static readonly Guid ChildCustomNodeId = Guid.Parse("d1b1a0c3-9ad3-4e91-a424-8aa800fa4ef2");

        private const string WatchNodeId = "3b2be8477f5a4ec5a6dc23d9f88a7b7e";

        protected override void GetLibrariesToPreload(List<string> libraries)
        {
            // Keep this minimal: we want CPython3 nodes in files to be upgraded to PythonNet3 at open time.
            libraries.Add("DesignScriptBuiltin.dll");
            libraries.Add("DSCoreNodes.dll");
            base.GetLibrariesToPreload(libraries);
        }

        [Test, Apartment(ApartmentState.STA)]
        public void NestedCPythonCustomNodes_AreAutoMigratedToPythonNet3_AndEvaluateTo20()
        {
            // Ensure DynamoView is "Loaded" so view extensions get their Loaded() callback.
            RaiseLoadedEvent(View);
            DispatcherUtil.DoEvents();

            EnsurePythonMigrationViewExtensionIsLoaded();
            EnsurePythonNet3EngineIsLoaded();

            // Load custom node definitions into the manager.
            var testDir = GetTestDirectory(ExecutingDirectory);
            var pythonDir = Path.Combine(testDir, "core", "python");
            var childPath = Path.Combine(pythonDir, "CNWithCPython_Child.dyf");
            var parentPath = Path.Combine(pythonDir, "CNWithCPython_Parent.dyf");

            Assert.IsTrue(File.Exists(childPath), "Missing test file: " + childPath);
            Assert.IsTrue(File.Exists(parentPath), "Missing test file: " + parentPath);

            CustomNodeInfo info;
            Assert.IsTrue(ViewModel.Model.CustomNodeManager.AddUninitializedCustomNode(childPath, true, out info));
            Assert.IsTrue(ViewModel.Model.CustomNodeManager.AddUninitializedCustomNode(parentPath, true, out info));

            // Open the graph that contains nested custom nodes.
            Open(@"core\python\WithNestedCPythonCustomNodes.dyn");
            DispatcherUtil.DoEvents();

            // Assert that the nested CPython3 node inside the child custom node workspace was upgraded in memory.
            Assert.IsTrue(Model.CustomNodeManager.TryGetFunctionWorkspace(ChildCustomNodeId, true, out var childWs));
            var pyNodes = childWs.Nodes.OfType<PythonNodeModels.PythonNodeBase>().ToList();
            Assert.AreEqual(1, pyNodes.Count, "Expected exactly one Python node in CNWithCPython_Child.");
            Assert.AreEqual(PythonEngineManager.PythonNet3EngineName, pyNodes[0].EngineName);

            Run();
            DispatcherUtil.DoEvents();

            // Assert watch value.
            var watch = Model.CurrentWorkspace.NodeFromWorkspace<Watch>(WatchNodeId);
            Assert.IsNotNull(watch);

            // Watch values are typically strings/numbers; normalize to a string for stable comparison.
            Assert.AreEqual("20", watch.CachedValue?.ToString());
        }

        private void EnsurePythonMigrationViewExtensionIsLoaded()
        {
            // DynamoView stores its ViewExtensionManager in an internal field; use reflection
            // so this test remains in the DynamoPythonTests assembly.
            var veManagerField = View.GetType().GetField("viewExtensionManager", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(veManagerField, "Could not find DynamoView.viewExtensionManager field via reflection.");

            var veManager = veManagerField.GetValue(View) as IViewExtensionManager;
            Assert.IsNotNull(veManager, "Could not access DynamoView.viewExtensionManager as IViewExtensionManager.");

            if (veManager.ViewExtensions.Any(e => e?.Name == "Python Migration"))
            {
                return;
            }

            // Find the extension definition XML in the configured view extension directories.
            var candidatePaths = new List<string>();
            foreach (var dir in Model.PathManager.ViewExtensionsDirectories)
            {
                var direct = Path.Combine(dir, "PythonMigration_ViewExtensionDefinition.xml");
                if (File.Exists(direct))
                {
                    candidatePaths.Add(direct);
                    continue;
                }

                // Some builds place view extensions in subfolders.
                if (Directory.Exists(dir))
                {
                    candidatePaths.AddRange(Directory.GetFiles(dir, "PythonMigration_ViewExtensionDefinition.xml", SearchOption.AllDirectories));
                }
            }

            var definitionPath = candidatePaths.FirstOrDefault();
            Assert.IsFalse(string.IsNullOrEmpty(definitionPath), "Could not locate PythonMigration_ViewExtensionDefinition.xml in any ViewExtensionsDirectories.");

            // Load and add the view extension via the manager's internal loader.
            var loaderProp = veManager.GetType().GetProperty("ExtensionLoader", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(loaderProp, "Could not find ExtensionLoader on the view extension manager via reflection.");

            var loader = loaderProp.GetValue(veManager);
            Assert.IsNotNull(loader, "Could not get view extension loader instance.");

            var loadMethod = loader.GetType().GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, binder: null, types: new[] { typeof(string) }, modifiers: null);
            Assert.IsNotNull(loadMethod, "Could not find ViewExtensionLoader.Load(string) via reflection.");

            var ext = loadMethod.Invoke(loader, new object[] { definitionPath }) as IViewExtension;
            Assert.IsNotNull(ext, "Failed to load Python Migration view extension from: " + definitionPath);

            veManager.Add(ext);

            Assert.IsTrue(veManager.ViewExtensions.Any(e => e?.Name == "Python Migration"));
        }

        private static void EnsurePythonNet3EngineIsLoaded()
        {
            // Some environments load engines via built-in packages; in test hosts we may need to force-load the engine.
            if (PythonEngineManager.Instance.AvailableEngines.Any(e => e?.Name == PythonEngineManager.PythonNet3EngineName))
            {
                return;
            }

            var enginePath = Path.Combine(PathManager.BuiltinPackagesDirectory, @"PythonNet3Engine\extra\DSPythonNet3.dll");
            if (!File.Exists(enginePath))
            {
                // Fall back to probing next to the test binaries.
                var local = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "DSPythonNet3.dll");
                enginePath = local;
            }

            Assert.IsTrue(File.Exists(enginePath), "PythonNet3 engine assembly not found: " + enginePath);

            var asm = Assembly.LoadFrom(enginePath);
            var loadMethod = typeof(PythonEngineManager).GetMethod(
                "LoadPythonEngine",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(IEnumerable<Assembly>) },
                modifiers: null);

            Assert.IsNotNull(loadMethod, "Could not find PythonEngineManager.LoadPythonEngine via reflection.");
            loadMethod.Invoke(PythonEngineManager.Instance, new object[] { new[] { asm } });

            Assert.IsTrue(PythonEngineManager.Instance.AvailableEngines.Any(e => e?.Name == PythonEngineManager.PythonNet3EngineName));
        }
    }
}
