namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

internal static class Test262Paths
{
    public static FullPath Root { get; } = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    /// <summary>The directory of this project: the pins, the skip list, the excluded features and the expectations.</summary>
    public static FullPath Project { get; } = Root / "tests" / "Meziantou.Framework.JavaScriptEngine.Test262";

    /// <summary>The fetched test suites. Run 'dotnet run eng/javascriptengine/fetch-tests.cs' to download them.</summary>
    public static FullPath External { get; } = Root / "tests" / "Meziantou.Framework.JavaScriptEngine.External";

    public static FullPath Test262 { get; } = External / "test262";

    public static FullPath Tests { get; } = Test262 / "test";

    public static FullPath Harness { get; } = Test262 / "harness";

    public static FullPath ParserTests { get; } = External / "test262-parser-tests";

    public static FullPath Expectations { get; } = Project / "expectations";

    public static FullPath SkipFile { get; } = Project / "test262-skip.txt";

    public static FullPath FeaturesFile { get; } = Project / "test262-features.txt";

    /// <summary>The page of the documentation that shows the pass rate per area.</summary>
    public static FullPath SummaryFile { get; } = Root / "docs" / "JavaScriptEngine" / "10-Conformance.md";

    public static bool IsAvailable => Directory.Exists(Tests) && Directory.Exists(Harness);
}
