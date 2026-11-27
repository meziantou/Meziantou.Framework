using System.Globalization;
using System.Text;
using Meziantou.Framework.WasmEngine.SpecTests.Runner;

namespace Meziantou.Framework.WasmEngine.SpecTests;

/// <summary>Runs the tests of the official WASI test suite (wasi-testsuite) and compares the results with the expectations (wasi-expectations.txt).</summary>
public sealed class WasiTests
{
    public static TheoryData<string> Tests => [.. WasiTestRunner.GetTests()];

    public static FullPath SummaryFile { get; } = SpecTestPaths.Root / "docs" / "WasmEngine" / "12-Wasi" / "07-Conformance.md";

    static WasiTests()
    {
        // The code compiled to .NET that is not valid fails the tests, instead of being interpreted
        JitOps.ThrowOnInvalidProgram = true;
    }

    /// <summary>Runs a test with its root directory copied on the disk.</summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Tests), DisableDiscoveryEnumeration = true)]
    public void RunHost(string test) => Run(test, WasiMode.Host);

    /// <summary>Runs a test with its root directory copied in memory.</summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Tests), DisableDiscoveryEnumeration = true)]
    public void RunMemory(string test) => Run(test, WasiMode.Memory);

    /// <summary>Runs a test with its root directory copied on the disk, and its functions compiled to .NET code on their first call.</summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Tests), DisableDiscoveryEnumeration = true)]
    public void RunCompiled(string test) => Run(test, WasiMode.Compiled);

    private static void Run(string test, WasiMode mode)
    {
        var failure = WasiTestRunner.Run(test, mode);

        // Windows allows the symbolic links only to the administrators and in the developer mode: a test that fails on the
        // disk of such a machine and passes in memory is skipped, instead of recording a failure that depends on the machine
        if (failure is not null && mode != WasiMode.Memory && !WasiTestRunner.CanCreateSymbolicLinks && !WasiExpectations.IsExpectedToFail(test, mode) && WasiTestRunner.Run(test, WasiMode.Memory) is null)
        {
            global::Xunit.Assert.Skip("This machine does not allow creating symbolic links, and the test fails on the disk: " + failure);
        }

        WasiExpectations.Check(test, mode, failure);
    }

    [Fact]
    public void SummaryIsUpToDate()
    {
        if (!WasiTestRunner.IsAvailable)
        {
            global::Xunit.Assert.Skip("The WASI test suite is not fetched. Run 'dotnet run eng/wasmengine/fetch-tests.cs'.");
        }

        var expected = GenerateSummary();
        var actual = File.Exists(SummaryFile) ? File.ReadAllText(SummaryFile) : "";
        if (expected != actual)
        {
            if (SpecExpectations.UpdateMode)
            {
                File.WriteAllText(SummaryFile, expected);
                return;
            }

            Assert.Fail("docs/WasmEngine/12-Wasi/07-Conformance.md is out of date: run this test with WASM_SPEC_UPDATE_EXPECTATIONS=1.");
        }
    }

    private static string GenerateSummary()
    {
        var tests = WasiTestRunner.GetTests();
        var modes = new[] { WasiMode.Host, WasiMode.Memory, WasiMode.Compiled };
        var sb = new StringBuilder();
        sb.Append("""
            ---
            title: Conformance
            description: The pass rate of the official WASI test suite.
            ---

            The tests of the official WASI test suite ([WebAssembly/wasi-testsuite](https://github.com/WebAssembly/wasi-testsuite))
            are commands compiled from AssemblyScript, C and Rust to `wasm32-wasip1` (WASI Preview 1), and components compiled
            from Rust to `wasm32-wasip3` (WASI 0.3, the `-wasip3` rows). Each test runs with the arguments, the
            environment variables and the root directory (preopened as `/`) of its specification, and passes when its exit
            code and its outputs are the expected ones. Each test runs three times: with a copy of its root directory on the
            disk ([WasiHostFileSystem](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostFileSystem)), with a copy in memory
            ([WasiInMemoryFileSystem](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryFileSystem)), and on the disk
            with the functions compiled to .NET code on their first call.

            The tests of the sockets and of HTTP run on the network of the machine on the disk
            ([WasiHostNetwork](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostNetwork),
            [WasiHostHttpClient](cref:Meziantou.Framework.WasmEngine.Wasi.WasiHostHttpClient), with servers of the test on the
            loopback address), and on a network inside the process in memory
            ([WasiInMemoryNetwork](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryNetwork),
            [WasiInMemoryHttpClient](cref:Meziantou.Framework.WasmEngine.Wasi.WasiInMemoryHttpClient)). The test connects to the
            sockets that the program listens on, and sends requests to the components of the world `wasi:http/service`
            ([HandleHttpRequest](cref:Meziantou.Framework.WasmEngine.Wasi.WasiComponentContext.HandleHttpRequest)).

            This page is generated by the tests of `tests/Meziantou.Framework.WasmEngine.SpecTests`, from the known failures
            listed in `wasi-expectations.txt`.

            | Tests | Count | Disk | Memory | Compiled |
            | --- | ---: | ---: | ---: | ---: |

            """);
        foreach (var group in tests.GroupBy(WasiTestRunner.GetLanguage).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            AppendRow(sb, group.Key, [.. group], modes);
        }

        AppendRow(sb, "**Total**", tests, modes);
        var failures = WasiExpectations.GetFailures();
        if (failures.Count > 0)
        {
            sb.Append("""

                The known failures:

                | Test | Reason |
                | --- | --- |

                """);
            foreach (var (key, reason) in failures)
            {
                sb.Append(CultureInfo.InvariantCulture, $"| `{key}` | {EscapeCell(reason ?? "")} |\n");
            }
        }

        return sb.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendRow(StringBuilder sb, string name, IReadOnlyList<string> tests, WasiMode[] modes)
    {
        sb.Append(CultureInfo.InvariantCulture, $"| {name} | {tests.Count}");
        foreach (var mode in modes)
        {
            var passing = tests.Count(test => !WasiExpectations.IsExpectedToFail(test, mode));
            sb.Append(" | ").Append(WasiExpectations.FormatRate(passing, tests.Count));
        }

        sb.Append(" |\n");
    }

    private static string EscapeCell(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
