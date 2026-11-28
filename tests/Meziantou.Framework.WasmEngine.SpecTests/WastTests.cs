using Meziantou.Framework.WasmEngine.SpecTests.Runner;

namespace Meziantou.Framework.WasmEngine.SpecTests;

/// <summary>Runs the scripts of the official WebAssembly test suite and compares the results with the expectations (expectations.txt).</summary>
public sealed class WastTests
{
    public static TheoryData<string> Scripts => [.. SpecTestPaths.GetScripts()];

    static WastTests()
    {
        // The code compiled to .NET that is not valid fails the tests, instead of being interpreted
        JitOps.ThrowOnInvalidProgram = true;
    }

    /// <summary>Runs a script with the interpreter.</summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Scripts), DisableDiscoveryEnumeration = true)]
    public void RunInterpreted(string script) => Run(script, WastMode.Interpreted);

    /// <summary>Runs a script with the functions compiled to .NET code on their first call: the known failures are the same.</summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Scripts), DisableDiscoveryEnumeration = true)]
    public void RunCompiled(string script) => Run(script, WastMode.Compiled);

    /// <summary>Runs a script with the interpreter, the calls continuing in .NET code from their first loop iteration (OSR): the known failures are the same.</summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Scripts), DisableDiscoveryEnumeration = true)]
    public void RunOsr(string script) => Run(script, WastMode.Osr);

    private static void Run(string script, WastMode mode)
    {
        using var runner = new WastRunner(File.ReadAllText(SpecTestPaths.TestSuite / script), mode);
        runner.Run();
        if (mode != WastMode.Interpreted && SpecExpectations.UpdateMode)
        {
            // The expectations are the failures of the interpreter
            SpecExpectations.Core.Check(script, runner.Failures, update: false);
            return;
        }

        SpecExpectations.Core.Check(script, runner.Failures);
    }

    [Fact]
    public void SummaryIsUpToDate()
    {
        if (!SpecTestPaths.IsAvailable)
        {
            global::Xunit.Assert.Skip("The test suite is not fetched. Run 'dotnet run eng/wasmengine/fetch-tests.cs'.");
        }

        var expected = SpecSummary.Generate();
        var actual = File.Exists(SpecTestPaths.SummaryFile) ? File.ReadAllText(SpecTestPaths.SummaryFile) : "";
        if (expected != actual)
        {
            if (SpecExpectations.UpdateMode)
            {
                SpecSummary.Write();
                return;
            }

            Assert.Fail("docs/WasmEngine/09-Conformance.md is out of date: run this test with WASM_SPEC_UPDATE_EXPECTATIONS=1.");
        }
    }
}
