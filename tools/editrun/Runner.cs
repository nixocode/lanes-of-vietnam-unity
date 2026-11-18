using System;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using NUnit.Framework;

// The EditMode suite outside Unity (PLAN §12.73a): every [Test] and [TestCase] of SimTests.cs by reflection, a line
// each, and the count. `tools/editrun/run.sh [filter]`. The suite has no [SetUp], [TearDown] or [TestCaseSource]; a
// method with a [TestCaseSource] is reported SKIP rather than passed silently.
public static class Runner
{
    public static int Main(string[] args)
    {
        string filter = args.Length > 0 ? args[0] : null;
        int pass = 0, fail = 0, skip = 0;
        foreach (var type in typeof(Runner).Assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(TestFixtureAttribute), true).Any() || t.GetMethods().Any(m => m.GetCustomAttributes(typeof(TestAttribute), true).Any() || m.GetCustomAttributes(typeof(TestCaseAttribute), true).Any())))
        {
            foreach (var mi in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (filter != null && !mi.Name.Contains(filter)) continue;
                var cases = mi.GetCustomAttributes(typeof(TestCaseAttribute), true).Cast<TestCaseAttribute>().ToList();
                bool isTest = mi.GetCustomAttributes(typeof(TestAttribute), true).Any();
                var sources = mi.GetCustomAttributes(typeof(TestCaseSourceAttribute), true).Any();
                if (sources) { Console.WriteLine($"SKIP {mi.Name} (TestCaseSource)"); skip++; continue; }
                if (!isTest && cases.Count == 0) continue;
                var runs = cases.Count > 0 ? cases.Select(c => c.Arguments).ToList() : new System.Collections.Generic.List<object[]> { new object[0] };
                foreach (var argv in runs)
                {
                    string name = mi.Name + (argv.Length > 0 ? "(" + string.Join(",", argv) + ")" : "");
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var inst = mi.IsStatic ? null : Activator.CreateInstance(type);
                        var ps = mi.GetParameters();
                        var call = argv.Select((v, i) => v == null ? null : Convert.ChangeType(v, ps[i].ParameterType.IsEnum ? Enum.GetUnderlyingType(ps[i].ParameterType) : ps[i].ParameterType)).ToArray();
                        for (int i = 0; i < call.Length; i++) if (ps[i].ParameterType.IsEnum && call[i] != null) call[i] = Enum.ToObject(ps[i].ParameterType, call[i]);
                        mi.Invoke(inst, call);
                        pass++;
                        Console.WriteLine($"PASS {name} ({sw.Elapsed.TotalSeconds:F1}s)");
                    }
                    catch (TargetInvocationException e) when (e.InnerException is IgnoreException || e.InnerException is InconclusiveException)
                    {
                        skip++; Console.WriteLine($"SKIP {name}: {e.InnerException.Message.Split('\n')[0]}");
                    }
                    catch (TargetInvocationException e) when (e.InnerException is SuccessException)
                    {
                        pass++; Console.WriteLine($"PASS {name}");
                    }
                    catch (TargetInvocationException e)
                    {
                        fail++; Console.WriteLine($"FAIL {name}: {e.InnerException?.GetType().Name}: {e.InnerException?.Message.Trim().Replace("\n", " | ")}");
                    }
                    catch (Exception e)
                    {
                        fail++; Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message}");
                    }
                }
            }
        }
        Console.WriteLine($"editrun: {pass} passed, {fail} failed, {skip} skipped");
        return fail == 0 ? 0 : 1;
    }
}
