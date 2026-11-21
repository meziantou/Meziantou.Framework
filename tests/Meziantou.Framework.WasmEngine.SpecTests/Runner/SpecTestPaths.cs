namespace Meziantou.Framework.WasmEngine.SpecTests.Runner;

internal static class SpecTestPaths
{
    public static FullPath Root { get; } = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    /// <summary>The directory of this project: the pin of the test suite and the expectations.</summary>
    public static FullPath Project { get; } = Root / "tests" / "Meziantou.Framework.WasmEngine.SpecTests";

    /// <summary>The fetched test suite. Run 'dotnet run eng/wasmengine/fetch-tests.cs' to download it.</summary>
    public static FullPath TestSuite { get; } = Root / "tests" / "Meziantou.Framework.WasmEngine.External" / "testsuite";

    public static FullPath ExpectationsFile { get; } = Project / "expectations.txt";

    /// <summary>The page of the documentation that shows the pass rate per script.</summary>
    public static FullPath SummaryFile { get; } = Root / "docs" / "WasmEngine" / "09-Conformance.md";

    public static bool IsAvailable => Directory.Exists(TestSuite) && File.Exists(TestSuite / "i32.wast");

    /// <summary>The scripts of the test suite, as paths relative to its root with '/' separators.</summary>
    public static IReadOnlyList<string> GetScripts()
    {
        if (!IsAvailable)
            return [];

        return Directory.EnumerateFiles(TestSuite, "*.wast", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(TestSuite, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
