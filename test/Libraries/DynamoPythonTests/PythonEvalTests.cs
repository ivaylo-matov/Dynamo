using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using System.Linq;
using System.IO;
using Dynamo;
using Dynamo.PythonServices;
using Dynamo.PythonServices.EventHandlers;
using DSPythonNet3;
using System.Diagnostics;

namespace DSPythonTests
{
    public class PythonEvalTests : UnitTestBase
    {
        [OneTimeSetUp]
        public void LoadPythonNet3()
        {
            //var loadDir_ = @"C:\__Work\GitHub\Dynamo\bin\AnyCPU\Debug\Built-In Packages\packages\PythonNet3Engine\extra";

            //// Optional but harmless: ensure siblings resolve from the same folder
            //AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            //{
            //    var name = new System.Reflection.AssemblyName(e.Name).Name + ".dll";
            //    var p = System.IO.Path.Combine(loadDir_, name);
            //    return System.IO.File.Exists(p) ? System.Reflection.Assembly.LoadFrom(p) : null;
            //};

            //// Only need to load the engine; its deps will resolve from 'extra'
            //System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(loadDir_, "DSPythonNet3.dll"));






            var loadDir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "PythonNet3Engine", "extra");

            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                var name = new System.Reflection.AssemblyName(e.Name).Name + ".dll";
                var p = System.IO.Path.Combine(loadDir, name);
                return System.IO.File.Exists(p) ? System.Reflection.Assembly.LoadFrom(p) : null;
            };

            // IMPORTANT: ensure no other Python.Runtime loaded first
            var already = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Python.Runtime");
            if (already != null)
                throw new InvalidOperationException($"Another Python.Runtime is already loaded: {already.FullName} @ {already.Location}");

            // Load dependencies first (locks versions)
            foreach (var d in new[] {
                "Python.Runtime.dll",
                "Python.Included.dll",
                "Python.Deployment.dll",
                "DSPythonNet3Wheels.dll",
                "DSPythonNet3Extension.dll"
            })
            {
                var path = System.IO.Path.Combine(loadDir, d);
                if (System.IO.File.Exists(path)) System.Reflection.Assembly.LoadFrom(path);
            }

            // Load the engine
            System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(loadDir, "DSPythonNet3.dll"));
        }



        public delegate object PythonEvaluatorDelegate(string code, IList bindingNames, IList bindingValues);

        public IEnumerable<PythonEvaluatorDelegate> Evaluators = new List<PythonEvaluatorDelegate> {
            DSPythonNet3.DSPythonNet3Evaluator.EvaluatePythonScript
        };

        [Test]
        [Category("UnitTests")]
        public void EvaluatorWorks()
        {
            foreach (var pythonEvaluator in Evaluators)
            {
                var empty = new ArrayList();
                var output = pythonEvaluator("OUT = 0", empty, empty);
                Assert.AreEqual(0, output);
            }
        }

        [Test]
        [Category("UnitTests")]
        public void BindingsWork()
        {
            const string expected = "Hi!";

            var names = new ArrayList { "test" };
            var vals = new ArrayList { expected };

            foreach (var pythonEvaluator in Evaluators)
            {
                var output = pythonEvaluator(
                    "OUT = test",
                    names,
                    vals
                );

                Assert.AreEqual(expected, output);
            }
        }

        [Test]
        [Category("UnitTests")]
        public void DataMarshaling_Output()
        {
            //const string script = "OUT = ['', ' ', '  ']";

            //// Using the CPython Evaluator
            //var marshaler = CPythonEvaluator.OutputMarshaler;
            //marshaler.RegisterMarshaler((string s) => s.Length);

            //var output = DSCPython.CPythonEvaluator.EvaluatePythonScript(
            //    script,
            //    new ArrayList(),
            //    new ArrayList());

            //Assert.AreEqual(new[] { 0, 1, 2 }, output);

            //marshaler.UnregisterMarshalerOfType<string>();


        }

        [Test]
        [Category("UnitTests")]
        public void DataMarshaling_Input()
        {
            //const string script = "OUT = sum(IN)";

            //var marshaler = DSCPython.CPythonEvaluator.InputMarshaler;
            //marshaler.RegisterMarshaler((string s) => s.Length);

            //var output = DSCPython.CPythonEvaluator.EvaluatePythonScript(
            //    script,
            //    new ArrayList { "IN" },
            //    new ArrayList { new ArrayList { " ", "  " } });

            //Assert.AreEqual(3, output);

            //marshaler.UnregisterMarshalerOfType<string>();
        }


        [Test]
        public void SliceOperator_Output()
        {
            //var names = new ArrayList { "indx" };
            //var vals = new ArrayList { 3 };

            //foreach (var pythonEvaluator in Evaluators)
            //{
            //    var output = pythonEvaluator(
            //    "OUT = [1,2,3,4,5,6,7][indx:indx+2]",
            //    names,
            //    vals);

            //    var expected = new ArrayList { 4, 5 };

            //    Assert.AreEqual(expected, output);
            //}
        }

        [Test]
        public void CPythonEngineIncludesTraceBack()
        {
//            var code = @"
//# extra line
//1/0
//# extra line
//";
//            try
//            {
//                DSCPython.CPythonEvaluator.EvaluatePythonScript(code, new ArrayList(), new ArrayList());
//                Assert.Fail("An exception was expected");
//            }
//            catch (Exception exc)
//            {
//                // Trace back is expected here. Line is 3 due to the line break from the code variable declaration
//                Assert.AreEqual(@"ZeroDivisionError : division by zero ['  File ""<string>"", line 3, in <module>\n']", exc.Message);
//            }

//            code = @"
//# extra line
//print 'hello'
//# extra line
//";
//            try
//            {
//                DSCPython.CPythonEvaluator.EvaluatePythonScript(code, new ArrayList(), new ArrayList());
//                Assert.Fail("An exception was expected");
//            }
//            catch (Exception exc)
//            {
//                // Trace back is not available for this call, but we still get a reasonable message back
//                Assert.AreEqual(@"SyntaxError : ('invalid syntax', ('<string>', 3, 7, ""print 'hello'\n""))", exc.Message);
//            }
        }

        [Test]
        public void CPythonEngineWithErrorRaisesCorrectEvent()
        {

            //var count = 0;
            //EvaluationFinishedEventHandler handler = (state, scope, codeString, bindings) =>
            //{
            //    count = count + 1;
            //    if (count == 1)
            //    {
            //        Assert.AreEqual(EvaluationState.Success, state);
            //    }
            //    else if (count == 2)
            //    {
            //        Assert.AreEqual(EvaluationState.Failed, state);
            //    }
            //};

            ////CPythonEvaluator.Instance.EvaluationFinished += handler;

            //var code = @"1";
            //try
            //{
            //    DSCPython.CPythonEvaluator.EvaluatePythonScript(code, new ArrayList(), new ArrayList());
            //}
            //finally
            //{
            //    Assert.AreEqual(1, count);
            //}

            //code = @"1/a";
            //try
            //{
            //    DSCPython.CPythonEvaluator.EvaluatePythonScript(code, new ArrayList(), new ArrayList());
            //}
            //catch
            //{
            //    //we anticipate an undefined var error.
            //}
            //finally
            //{
            //    //CPythonEvaluator.Instance.EvaluationFinished -= handler;
            //    //Assert.AreEqual(2, count);
            //}
        }

        [Test]
        public void OutputPythonObjectDoesNotThrow()
        {
//            var code = @"
//import weakref

//class myobj:
//    def __str__(self):
//        return 'I am a myobj'

//o = myobj()
//wr = weakref.ref(o)

//OUT = wr
//";
//            var empty = new ArrayList();
//            Assert.DoesNotThrow(() =>
//            {
//                Assert.IsTrue(DSCPython.CPythonEvaluator.EvaluatePythonScript(code, empty, empty).ToString().Contains("weakref at"));

//            });
        }

        [Test]
        public void OutputPythonObjectHasProperToString()
        {
//            var code = @"

//class myobj:
//    def __str__(self):
//        return 'I am a myobj'

//o = myobj()
//OUT = o
//";
//            var empty = new ArrayList();
//            Assert.DoesNotThrow(() =>
//            {
//                Assert.AreEqual("I am a myobj", DSCPython.CPythonEvaluator.EvaluatePythonScript(code, empty, empty).ToString());

//            });
        }

        [Test]
        public void PythonObjectWithDynamoSkipisNotMarshaled()
        {
//            var code = @"

//class iterable:
//    def __str__(self):
//        return 'I want to participate in conversion'
//    def __iter__(self):
//        return iter([0,1,2,3])
//    def __getitem__(self,key):
//        return key

//o = iterable()
//OUT = o
//";

//            var code2 = @"

//class notiterable:
//    def __dynamoskipconversion__(self):
//        pass
//    def __str__(self):
//        return 'I want to skip in conversion'
//    def __iter__(self):
//        return iter([0,1,2,3])
//    def __getitem__(self,key):
//        return key

//o = notiterable()
//OUT = o
//";
//            var empty = new ArrayList();
//            var result1 = DSCPython.CPythonEvaluator.EvaluatePythonScript(code, empty, empty);
//            var result2 = DSCPython.CPythonEvaluator.EvaluatePythonScript(code2, empty, empty);
//            Assert.IsInstanceOf(typeof(IList), result1);
//            Assert.IsTrue(new List<object>() { 0L, 1L, 2L, 3L }
//                .SequenceEqual((IEnumerable<Object>)result1));
//            Assert.IsInstanceOf(typeof(DSCPython.DynamoCPythonHandle), result2);

        }

        [Test]
        public void NonListIterablesCanBeOutput()
        {
//            var code = @"
//s = { 'hello' }
//fs = frozenset({ 'world' })
//d = { 'one': 1 }
//dk = d.keys()
//dv = d.values()
//di = d.items()

//OUT = s,fs,dk,dv,di
//";
//            var expected = new ArrayList
//            {
//                new ArrayList { "hello" },
//                new ArrayList { "world" },
//                new ArrayList { "one" },
//                new ArrayList { 1 },
//                new ArrayList { new ArrayList { "one", 1 } }
//            };
//            var empty = new ArrayList();
//            foreach (var pythonEvaluator in Evaluators)
//            {
//                var output = pythonEvaluator(code, empty, empty);
//                Assert.AreEqual(expected, output);
            //}
        }
        [Test]
        public void ImportedLibrariesReloadedOnNewEvaluation()
        {

//            var modName = "reload_test1";

//            var tempPath = Path.Combine(TempFolder, $"{modName}.py");

//            //clear file.
//            File.WriteAllText(tempPath, "value ='Hello World!'\n");
//            try
//            {
//                var script = $@"import sys
//sys.path.append(r'{Path.GetDirectoryName(tempPath)}')
//import {modName}
//OUT = {modName}.value";

//                var output = DSCPython.CPythonEvaluator.EvaluatePythonScript(
//                   script,
//                   new ArrayList { "IN" },
//                   new ArrayList { new ArrayList { " ", "  " } });

//                Assert.AreEqual("Hello World!", output);

//                //now modify the file.
//                File.AppendAllLines(tempPath, new string[] { "value ='bye'" });

//                //mock raise event
//                CPythonEvaluator.RequestPythonResetHandler(PythonEngineManager.CPython3EngineName);

//                output = CPythonEvaluator.EvaluatePythonScript(
//                 script,
//                 new ArrayList { "IN" },
//                 new ArrayList { new ArrayList { " ", "  " } });
//                Assert.AreEqual("bye", output);

//            }
//            finally
//            {
//                File.Delete(tempPath);
//            }
        }


    }
}
