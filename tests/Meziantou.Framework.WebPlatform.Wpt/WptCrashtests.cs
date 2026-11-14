using Meziantou.Framework.WebPlatform.Wpt.Runner;

namespace Meziantou.Framework.WebPlatform.Wpt;

/// <summary>
/// Runs the crash tests of the fetched directories of the web-platform-tests (https://web-platform-tests.org/writing-tests/crashtest.html):
/// each page is loaded and rendered, and passes when this completes without an exception of the library or a timeout.
/// <c>WPT_CRASHTESTS=0</c> skips them.
/// </summary>
public sealed class WptCrashtests
{
    // The groups split the crash tests by area, so a filtered run (e.g. --filter-method) can select one. Every crash test of
    // a fetched directory belongs to exactly one group.
    private static readonly string[] OtherExcludedDirectories = ["css", "html"];

    public static TheoryData<string> CssTests => GetTests("css");

    public static TheoryData<string> HtmlTests => GetTests("html");

    public static TheoryData<string> OtherTests => GetTests("", OtherExcludedDirectories);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssTests))]
    public void Css(string test) => Run("css", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlTests))]
    public void Html(string test) => Run("html", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(OtherTests))]
    public void Other(string test) => Run("", test, OtherExcludedDirectories);

    private static TheoryData<string> GetTests(string directory, params string[] excludedDirectories)
    {
        var data = new TheoryData<string>();
        if (!WptCrashtestDiscovery.IsEnabled)
            return data;

        // WPT_FILTER restricts the tests to the ones whose id contains the value, or one of the values separated by '|'
        var filters = Environment.GetEnvironmentVariable("WPT_FILTER") is { Length: > 0 } filterValue ? filterValue.Split('|', StringSplitOptions.RemoveEmptyEntries) : null;
        foreach (var test in Discover(directory, excludedDirectories).Values)
        {
            if (filters is not null && !Array.Exists(filters, filter => test.Id.Contains(filter, StringComparison.Ordinal)))
                continue;

            data.Add(test.Id);
        }

        return data;
    }

    // The crash tests of a group are discovered once, when the test cases are enumerated
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Dictionary<string, WptCrashtest>>> DiscoveredTests = new(StringComparer.Ordinal);

    private static Dictionary<string, WptCrashtest> Discover(string directory, string[] excludedDirectories)
        => DiscoveredTests.GetOrAdd(directory + "|" + string.Join("|", excludedDirectories), _ => new(() => WptCrashtestDiscovery.Discover(directory, excludedDirectories).ToDictionary(t => t.Id, StringComparer.Ordinal))).Value;

    private static void Run(string directory, string id, params string[] excludedDirectories)
    {
        var test = Discover(directory, excludedDirectories)[id];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = WptCrashtestRunner.Run(test);
        WptReport.Append(test, result, stopwatch.Elapsed);
        WptExpectations.Check(test, result);
    }
}
