using Meziantou.Framework.WebPlatform.Wpt.Runner;

namespace Meziantou.Framework.WebPlatform.Wpt;

/// <summary>
/// Runs the reftests of the fetched directories of the web-platform-tests (see src/Meziantou.Framework.WebPlatform/docs/web-platform-tests.md#reftests): the test
/// and its references are rendered by the Imaging backend and their pixels are compared. <c>WPT_REFTESTS=0</c> skips them.
/// </summary>
public sealed class WptReftests
{
    // The groups split the reftests by area, so a filtered run (e.g. --filter-method) can select one. Every reftest of a
    // fetched directory belongs to exactly one group.
    private static readonly string[] CssGroups = ["css/CSS2", "css/css-flexbox", "css/css-grid"];
    private static readonly string[] OtherExcludedDirectories = ["css", "html", "svg", "mathml"];

    public static TheoryData<string> Css2Tests => GetTests("css/CSS2");

    public static TheoryData<string> CssFlexboxTests => GetTests("css/css-flexbox");

    public static TheoryData<string> CssGridTests => GetTests("css/css-grid");

    public static TheoryData<string> CssTests => GetTests("css", CssGroups);

    public static TheoryData<string> HtmlTests => GetTests("html");

    public static TheoryData<string> SvgTests => GetTests("svg");

    public static TheoryData<string> MathMLTests => GetTests("mathml");

    public static TheoryData<string> OtherTests => GetTests("", OtherExcludedDirectories);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Css2Tests))]
    public void Css2(string test) => Run("css/CSS2", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssFlexboxTests))]
    public void CssFlexbox(string test) => Run("css/css-flexbox", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssGridTests))]
    public void CssGrid(string test) => Run("css/css-grid", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(CssTests))]
    public void Css(string test) => Run("css", test, CssGroups);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(HtmlTests))]
    public void Html(string test) => Run("html", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SvgTests))]
    public void Svg(string test) => Run("svg", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(MathMLTests))]
    public void MathML(string test) => Run("mathml", test);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(OtherTests))]
    public void Other(string test) => Run("", test, OtherExcludedDirectories);

    private static TheoryData<string> GetTests(string directory, params string[] excludedDirectories)
    {
        var data = new TheoryData<string>();
        if (!WptReftestDiscovery.IsEnabled)
            return data;

        // WPT_FILTER restricts the tests to the ones whose id contains the value, or one of the values separated by '|' (useful
        // while debugging, or to update the expectations of some tests)
        var filters = Environment.GetEnvironmentVariable("WPT_FILTER") is { Length: > 0 } filterValue ? filterValue.Split('|', StringSplitOptions.RemoveEmptyEntries) : null;
        foreach (var test in Discover(directory, excludedDirectories).Values)
        {
            if (filters is not null && !Array.Exists(filters, filter => test.Id.Contains(filter, StringComparison.Ordinal)))
                continue;

            data.Add(test.Id);
        }

        return data;
    }

    // The reftests of a group are discovered once, when the test cases are enumerated
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Dictionary<string, WptReftest>>> DiscoveredTests = new(StringComparer.Ordinal);

    private static Dictionary<string, WptReftest> Discover(string directory, string[] excludedDirectories)
        => DiscoveredTests.GetOrAdd(directory + "|" + string.Join("|", excludedDirectories), _ => new(() => WptReftestDiscovery.Discover(directory, excludedDirectories).ToDictionary(t => t.Id, StringComparer.Ordinal))).Value;

    private static void Run(string directory, string id, params string[] excludedDirectories)
    {
        var test = Discover(directory, excludedDirectories)[id];
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = WptReftestRunner.Run(test, expectedToPass: !WptExpectations.UpdateMode && WptExpectations.GetExpectedStatus(test) == "PASS");
        WptReport.Append(test, result, stopwatch.Elapsed);

        // The expectations are the results of the default engine (Meziantou.Framework.JavaScriptEngine)
        if (WptEngine.IsDefault)
        {
            WptExpectations.Check(test, result);
        }
    }
}
