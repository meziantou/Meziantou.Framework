namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

internal static class Test262Paths
{
    public static string Root { get; } = FullPath.FromPath(AppContext.BaseDirectory).FindRequiredGitRepositoryRoot();

    /// <summary>The directory of this project: the pins, the skip list, the excluded features and the expectations.</summary>
    public static string Project { get; } = Path.Combine(Root, "tests", "Meziantou.Framework.JavaScriptEngine.Test262");

    /// <summary>The fetched test suites. Run 'dotnet run eng/javascriptengine/fetch-tests.cs' to download them.</summary>
    public static string External { get; } = Path.Combine(Root, "tests", "Meziantou.Framework.JavaScriptEngine.External");

    public static string Test262 { get; } = Path.Combine(External, "test262");

    public static string Tests { get; } = Path.Combine(Test262, "test");

    public static string Harness { get; } = Path.Combine(Test262, "harness");

    public static string ParserTests { get; } = Path.Combine(External, "test262-parser-tests");

    public static string Expectations { get; } = Path.Combine(Project, "expectations");

    public static string SkipFile { get; } = Path.Combine(Project, "test262-skip.txt");

    public static string FeaturesFile { get; } = Path.Combine(Project, "test262-features.txt");

    public static string SummaryFile { get; } = Path.Combine(Project, "summary.md");

    public static bool IsAvailable => Directory.Exists(Tests) && Directory.Exists(Harness);
}
