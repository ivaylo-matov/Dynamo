using NUnit.Framework;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DynamoCoreWpf3Tests
{
    [SetUpFixture]
    public class PythonEngineBootstrap
    {
        [OneTimeSetUp]
        public void LoadPythonNet3()
        {
            var bin = TestContext.CurrentContext.TestDirectory;

            // Make sure the files are actually present in the test output folder
            var enginePath = Path.Combine(bin, "DSPythonNet3.dll");
            var runtimePath = Path.Combine(bin, "Python.Runtime.dll");
            Assert.That(File.Exists(enginePath), $"Missing: {enginePath}");
            Assert.That(File.Exists(runtimePath), $"Missing: {runtimePath}");

            // Load dependencies first when possible
            Assembly.LoadFrom(runtimePath);
            Assembly.LoadFrom(enginePath);

            // Prove it worked
            var names = AppDomain.CurrentDomain.GetAssemblies()
                           .Select(a => a?.GetName().Name).ToList();
            Assert.Contains("DSPythonNet3", names);
            Assert.Contains("Python.Runtime", names);
        }
    }
}
