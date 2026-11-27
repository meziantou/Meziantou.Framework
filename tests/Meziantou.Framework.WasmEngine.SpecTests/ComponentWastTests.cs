using Meziantou.Framework.WasmEngine.SpecTests.Runner;

namespace Meziantou.Framework.WasmEngine.SpecTests;

/// <summary>Runs the scripts of the tests of the component model and compares the results with the expectations (component-expectations.txt).</summary>
public sealed class ComponentWastTests
{
    public static TheoryData<string> Scripts => [.. SpecTestPaths.GetComponentScripts()];

    static ComponentWastTests()
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

    private static void Run(string script, WastMode mode)
    {
        using var runner = new ComponentWastRunner(File.ReadAllText(SpecTestPaths.ComponentModelTests / script), mode);
        runner.Run();
        SpecExpectations.Components.Check(script, runner.Failures, update: mode == WastMode.Interpreted);
    }

    [Fact]
    public void SummaryIsUpToDate()
    {
        if (!SpecTestPaths.IsComponentModelAvailable)
        {
            global::Xunit.Assert.Skip("The tests of the component model are not fetched. Run 'dotnet run eng/wasmengine/fetch-tests.cs'.");
        }

        var expected = SpecSummary.GenerateComponents();
        var actual = File.Exists(SpecTestPaths.ComponentSummaryFile) ? File.ReadAllText(SpecTestPaths.ComponentSummaryFile) : "";
        if (expected != actual)
        {
            if (SpecExpectations.UpdateMode)
            {
                SpecSummary.WriteComponents();
                return;
            }

            Assert.Fail("docs/WasmEngine/13-Components/05-Conformance.md is out of date: run this test with WASM_SPEC_UPDATE_EXPECTATIONS=1.");
        }
    }
}
