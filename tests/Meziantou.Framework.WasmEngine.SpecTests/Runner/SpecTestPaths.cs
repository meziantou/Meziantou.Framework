namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

internal static class SpecTestPaths
{
    public static FullPath Root { get; } = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    /// <summary>The directory of this project: the pin of the test suite and the expectations.</summary>
    public static FullPath Project { get; } = Root / "tests" / "Meziantou.Framework.WasmEngine.SpecTests";

    /// <summary>The fetched test suite. Run 'dotnet run eng/wasmengine/fetch-tests.cs' to download it.</summary>
    public static FullPath TestSuite { get; } = Root / "tests" / "Meziantou.Framework.WasmEngine.External" / "testsuite";

    public static FullPath ExpectationsFile { get; } = Project / "expectations.txt";

    /// <summary>The fetched tests of the component model (https://github.com/WebAssembly/component-model/tree/main/test).</summary>
    public static FullPath ComponentModelTests { get; } = Root / "tests" / "Meziantou.Framework.WasmEngine.External" / "component-model" / "test";

    public static FullPath ComponentExpectationsFile { get; } = Project / "component-expectations.txt";

    /// <summary>The page of the documentation that shows the pass rate of the component model tests.</summary>
    public static FullPath ComponentSummaryFile { get; } = Root / "docs" / "WasmEngine" / "13-Components" / "05-Conformance.md";

    public static bool IsComponentModelAvailable => Directory.Exists(ComponentModelTests) && File.Exists(ComponentModelTests / "README.md");

    /// <summary>The page of the documentation that shows the pass rate per script.</summary>
    public static FullPath SummaryFile { get; } = Root / "docs" / "WasmEngine" / "09-Conformance.md";

    public static bool IsAvailable => Directory.Exists(TestSuite) && File.Exists(TestSuite / "i32.wast");

    /// <summary>The scripts of the test suite, as paths relative to its root with '/' separators.</summary>
    public static IReadOnlyList<string> GetScripts() => IsAvailable ? GetScripts(TestSuite) : [];

    /// <summary>The scripts of the component model tests, as paths relative to their root with '/' separators.</summary>
    public static IReadOnlyList<string> GetComponentScripts() => IsComponentModelAvailable ? GetScripts(ComponentModelTests) : [];

    private static string[] GetScripts(FullPath root)
    {
        return Directory.EnumerateFiles(root, "*.wast", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
