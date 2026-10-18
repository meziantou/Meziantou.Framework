using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpecUpdate;

/// <summary>
/// Generates src/Meziantou.Framework.WebPlatform/docs/wpt-coverage.md and the pages of its sections (src/Meziantou.Framework.WebPlatform/docs/wpt-coverage/*.md): for each section of the
/// web-platform-tests, the testharness.js tests that run in a window or in a dedicated or shared worker and the reftests (from the manifest
/// of the pinned revision), the tests that are run and pass (from the expectations), and why the other tests do not pass (wpt-skip.json and
/// wpt-reasons.json). The tentative tests (experimental features, not yet required by a specification) and the reftests are reported
/// separately from the stable testharness.js tests.
/// </summary>
internal static partial class WptCoverage
{
    public const string IndexPath = "wpt-coverage.md";

    public const string SectionDirectory = "wpt-coverage";

    private const string Command = "dotnet run --project tools/Meziantou.Framework.WebPlatform.SpecUpdate -- wpt-coverage";

    /// <summary>Generates the pages, by path relative to the docs directory.</summary>
    public static CoverageResult Generate(string manifestPath, string wptProjectDirectory, string commit)
    {
        HashSet<string> wptTests;
        HashSet<string> reftests;
        using (var manifest = WptManifest.Open(manifestPath))
        {
            wptTests = ReadTestharnessTests(manifest);
            // (the print reftests compare the pages of the printed documents)
            reftests = [.. WptManifest.ReadItems(manifest, "reftest").Keys, .. WptManifest.ReadItems(manifest, "print-reftest").Keys];
        }

        var (runTests, runReftests) = ReadExpectations(Path.Combine(wptProjectDirectory, "expectations"));

        // A few tests are not testharness.js tests for the manifest (e.g. the harness is inserted by document.write())
        wptTests.UnionWith(runTests.Keys);
        reftests.UnionWith(runReftests.Keys);
        reftests.ExceptWith(wptTests);

        var invalidReasons = new List<string>();
        using var skipDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(wptProjectDirectory, "wpt-skip.json")));
        var skipped = new ReasonPatterns(ReadReasons(skipDocument.RootElement, "wpt-skip.json", invalidReasons));
        using var reasonsDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(wptProjectDirectory, "wpt-reasons.json")));
        var notRunReasons = new ReasonPatterns(ReadReasons(reasonsDocument.RootElement.GetProperty("notRun"), "wpt-reasons.json notRun", invalidReasons));
        var failingReasons = new ReasonPatterns(ReadReasons(reasonsDocument.RootElement.GetProperty("failing"), "wpt-reasons.json failing", invalidReasons));

        // The reasons of the failing reftests are separate from the ones of the testharness.js tests: a directory can share a
        // reason for its reftests (e.g. a painting feature) that does not apply to its testharness.js tests
        var failingReftestReasons = new ReasonPatterns(ReadReasons(reasonsDocument.RootElement.GetProperty("failingReftests"), "wpt-reasons.json failingReftests", invalidReasons));

        var tests = new List<TestEntry>();
        AddTests(wptTests, runTests, failingReasons, isReftest: false);
        AddTests(reftests, runReftests, failingReftestReasons, isReftest: true);
        tests.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

        var pages = new Writer(tests, commit).Write();
        return new CoverageResult(
            pages,
            [.. tests.Where(t => !t.IsPassing && t.Reasons is null).Select(t => t.Path)],
            [.. notRunReasons.UnusedPatterns.Select(p => "notRun: " + p), .. failingReasons.UnusedPatterns.Select(p => "failing: " + p), .. failingReftestReasons.UnusedPatterns.Select(p => "failingReftests: " + p)],
            invalidReasons);

        void AddTests(HashSet<string> paths, Dictionary<string, TestResult> results, ReasonPatterns failing, bool isReftest)
        {
            foreach (var path in paths.Where(p => p.Contains('/', StringComparison.Ordinal)))
            {
                // The tests of wpt-skip.json are not run, even when they have expectations
                if (skipped.Find(path) is { } skipReason)
                {
                    tests.Add(new TestEntry(path, Result: null, skipReason, isReftest));
                }
                else if (results.TryGetValue(path, out var result))
                {
                    tests.Add(new TestEntry(path, result, result.IsPassing ? null : failing.Find(path), isReftest));
                }
                else
                {
                    tests.Add(new TestEntry(path, Result: null, notRunReasons.Find(path), isReftest));
                }
            }
        }
    }

    /// <summary>
    /// The source files of the testharness.js tests that have a variant running in a window or in a dedicated or shared worker,
    /// or in a service worker for the tests of the service workers (the runner only runs the service worker variants of
    /// <c>service-workers/</c>), not only in a ShadowRealm.
    /// </summary>
    private static HashSet<string> ReadTestharnessTests(JsonDocument manifest)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (path, variants) in WptManifest.ReadItems(manifest, "testharness"))
        {
            // The variants are [url or null, extras]
            var serviceWorkersSupported = path.StartsWith("service-workers/", StringComparison.Ordinal);
            if (variants.Exists(variant => variant[0].ValueKind is JsonValueKind.Null || variant[0].GetString() is var url && !(ShadowRealmUrl().IsMatch(url!) || (!serviceWorkersSupported && ServiceWorkerUrl().IsMatch(url!)))))
            {
                result.Add(path);
            }
        }

        return result;
    }

    /// <summary>
    /// The results of the testharness.js tests and of the reftests listed in the expectations, by source file (the variants of a
    /// file are merged). The result of a reftest (<c>{ "reftest": "FAIL" }</c>) is a test with one subtest, whose harness status
    /// is <c>OK</c> when the comparisons ran (<c>PASS</c> or <c>FAIL</c>).
    /// </summary>
    private static (Dictionary<string, TestResult> Tests, Dictionary<string, TestResult> Reftests) ReadExpectations(string expectationsDirectory)
    {
        var result = new Dictionary<string, TestResult>(StringComparer.Ordinal);
        var reftests = new Dictionary<string, TestResult>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(expectationsDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var test in document.RootElement.EnumerateObject())
            {
                var path = test.Name.Split('?', '#')[0];
                if (test.Value.TryGetProperty("reftest", out var reftestElement))
                {
                    var status = reftestElement.GetString()!;
                    var reftest = new TestResult(1, status is "PASS" ? 0 : 1, status is "PASS" or "FAIL" ? "OK" : status);
                    reftests[path] = reftests.TryGetValue(path, out var previous)
                        ? new TestResult(previous.Total + 1, previous.Failures + reftest.Failures, previous.Harness is "OK" ? reftest.Harness : previous.Harness)
                        : reftest;
                    continue;
                }

                var total = test.Value.GetProperty("total").GetInt32();
                var failures = test.Value.TryGetProperty("failures", out var failuresElement) ? failuresElement.EnumerateObject().Count() : 0;
                var harness = test.Value.TryGetProperty("harness", out var harnessElement) ? harnessElement.GetString()! : "OK";
                result[path] = result.TryGetValue(path, out var existing)
                    ? new TestResult(existing.Total + total, existing.Failures + failures, existing.Harness is "OK" ? harness : existing.Harness)
                    : new TestResult(total, failures, harness);
            }
        }

        return (result, reftests);
    }

    /// <summary>Reads the reasons of a pattern: an array of <c>{ "kind": "...", "features": [...], "description": "..." }</c>. The invalid reasons are added to <paramref name="errors"/>.</summary>
    private static Dictionary<string, IReadOnlyList<Reason>> ReadReasons(JsonElement element, string source, List<string> errors)
    {
        var result = new Dictionary<string, IReadOnlyList<Reason>>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var reasons = new List<Reason>();
            if (property.Value.ValueKind is not JsonValueKind.Array || property.Value.GetArrayLength() == 0)
            {
                errors.Add($"{source}: '{property.Name}': the value must be a non-empty array of reasons");
            }
            else
            {
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (ReadReason(item) is { } error)
                    {
                        errors.Add($"{source}: '{property.Name}': {error}");
                    }
                    else
                    {
                        reasons.Add(new Reason(
                            item.GetProperty("kind").GetString()!,
                            item.TryGetProperty("features", out var features) ? [.. features.EnumerateArray().Select(f => f.GetString()!)] : [],
                            item.GetProperty("description").GetString()!));
                    }
                }
            }

            // The reasons are sorted by kind (in the order of the documentation), so the kinds and the descriptions are listed in a consistent order
            result[property.Name] = [.. reasons.OrderBy(r => Array.FindIndex(Kinds, k => k.Name == r.Kind))];
        }

        return result;

        // Returns the error of an invalid reason
        static string? ReadReason(JsonElement item)
        {
            if (item.ValueKind is not JsonValueKind.Object)
                return "a reason must be an object";

            if (item.EnumerateObject().Select(p => p.Name).FirstOrDefault(name => name is not ("kind" or "features" or "description")) is { } unknownProperty)
                return $"unknown property '{unknownProperty}'";

            if (!item.TryGetProperty("kind", out var kind) || kind.ValueKind is not JsonValueKind.String || !Kinds.Any(k => k.Name == kind.GetString()))
                return $"the kind must be one of {string.Join(", ", Kinds.Select(k => k.Name))}";

            if (kind.GetString() is RequiresKind)
            {
                if (!item.TryGetProperty("features", out var features) || features.ValueKind is not JsonValueKind.Array || features.GetArrayLength() == 0)
                    return $"a {RequiresKind} reason must have a non-empty 'features' array";

                if (features.EnumerateArray().FirstOrDefault(f => f.ValueKind is not JsonValueKind.String || !Features.Any(k => k.Name == f.GetString())) is { ValueKind: not JsonValueKind.Undefined } feature)
                    return $"unknown feature {feature.GetRawText()}, expected one of {string.Join(", ", Features.Select(f => f.Name))}";
            }
            else if (item.TryGetProperty("features", out _))
            {
                return $"only a {RequiresKind} reason has 'features'";
            }

            if (!item.TryGetProperty("description", out var description) || description.ValueKind is not JsonValueKind.String || string.IsNullOrWhiteSpace(description.GetString()))
                return "the description must be a non-empty string";

            return null;
        }
    }

    [GeneratedRegex(@"\.serviceworker(-module)?\.html", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex ServiceWorkerUrl();

    [GeneratedRegex("shadowrealm", RegexOptions.CultureInvariant)]
    private static partial Regex ShadowRealmUrl();

    /// <summary>The generated pages (by path relative to the docs directory), the non-passing tests that have no reason, the reason patterns that match no test, and the errors of the invalid reasons.</summary>
    internal sealed record CoverageResult(IReadOnlyDictionary<string, string> Pages, IReadOnlyList<string> MissingReasons, IReadOnlyList<string> UnusedReasons, IReadOnlyList<string> InvalidReasons);

    private const string RequiresKind = "REQUIRES";

    /// <summary>The kinds of the reasons why a test does not pass, in the order of the documentation.</summary>
    private static readonly (string Name, string Description)[] Kinds =
    [
        ("BUG", "the implementation is broken"),
        ("TEST BUG", "the test is wrong: it does not match the specification"),
        ("JINT", "the JavaScript engine (Jint) does not support a feature that the test uses"),
        ("NOT IMPLEMENTED", "the feature is not implemented yet"),
        (RequiresKind, "the feature cannot be implemented, or the test cannot pass, until other features are implemented (listed in parentheses)"),
        ("TEST RUNNER", "the WPT runner cannot run the test: a `.py` handler of the WPT server is not emulated, or the directory is not fetched"),
        ("OUT OF SCOPE", "the library will not support it"),
        ("FLAKY", "the test passes or fails randomly"),
        ("PERFORMANCE", "the test is too slow to be run by every build"),
    ];

    /// <summary>The features that a <see cref="RequiresKind"/> reason can list.</summary>
    private static readonly (string Name, string Description)[] Features =
    [
        ("animations", "CSS animations and transitions, Web Animations"),
        ("hardware", "devices and OS integration: sensors, cameras, screens, Bluetooth, USB"),
        ("layout", "a layout engine: boxes, scrolling, hit testing"),
        ("media", "audio and video playback, media decoders and encoders"),
        ("rendering", "painting, image decoding, canvas, paint timing"),
        ("service-workers", "service workers"),
        ("user-input", "user input: pointer, keyboard and wheel actions, user activation"),
        ("web-storage", "Web Storage (`localStorage`, `sessionStorage`)"),
        ("workers", "dedicated and shared workers"),
        ("worklets", "worklets"),
    ];

    /// <summary>A reason why a test does not pass. <see cref="Features"/> are the missing features of a <see cref="RequiresKind"/> reason.</summary>
    private sealed record Reason(string Kind, IReadOnlyList<string> Features, string Description);

    private sealed record TestResult(int Total, int Failures, string Harness)
    {
        public bool IsPassing => Harness is "OK" && Failures == 0;
    }

    /// <summary>A test of the manifest. <see cref="Result"/> is null when the test is not run (disabled).</summary>
    private sealed record TestEntry(string Path, TestResult? Result, IReadOnlyList<Reason>? Reasons, bool IsReftest)
    {
        /// <summary>
        /// Whether the test makes assertions that no specification requires yet (proposals, experimental features): its file
        /// name contains <c>.tentative.</c> or it is in a <c>tentative</c> directory (https://web-platform-tests.org/writing-tests/file-names.html).
        /// </summary>
        public bool IsTentative { get; } = Path.Contains(".tentative.", StringComparison.Ordinal) || Path.Split('/').SkipLast(1).Contains("tentative", StringComparer.Ordinal);

        /// <summary>The group of the test in the pages: the reftests are reported separately from the testharness.js tests (stable or tentative).</summary>
        public TestGroup Group => IsReftest ? TestGroup.Reftest : IsTentative ? TestGroup.Tentative : TestGroup.Stable;

        public bool IsDisabled => Result is null;

        public bool IsPassing => Result is { IsPassing: true };
    }

    private enum TestGroup
    {
        Stable,
        Tentative,
        Reftest,
    }

    /// <summary>The reasons of wpt-skip.json and wpt-reasons.json: a pattern is a path, or a prefix followed by '*'. The longest matching pattern wins.</summary>
    private sealed class ReasonPatterns(Dictionary<string, IReadOnlyList<Reason>> reasons)
    {
        private readonly List<string> _patterns = [.. reasons.Keys.OrderByDescending(p => p.Length).ThenBy(p => p, StringComparer.Ordinal)];
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public IEnumerable<string> UnusedPatterns => _patterns.Where(p => !_used.Contains(p)).Order(StringComparer.Ordinal);

        public IReadOnlyList<Reason>? Find(string path)
        {
            foreach (var pattern in _patterns)
            {
                if (pattern.EndsWith('*', StringComparison.Ordinal) ? path.StartsWith(pattern[..^1], StringComparison.Ordinal) : path == pattern)
                {
                    _used.Add(pattern);
                    return reasons[pattern];
                }
            }

            return null;
        }
    }

    private sealed record Stats(int Passing, int Tests, int Disabled)
    {
        public static Stats From(IEnumerable<TestEntry> tests)
        {
            int passing = 0, count = 0, disabled = 0;
            foreach (var test in tests)
            {
                count++;
                if (test.IsDisabled)
                {
                    disabled++;
                }
                else if (test.IsPassing)
                {
                    passing++;
                }
            }

            return new Stats(passing, count, disabled);
        }
    }

    private sealed class Writer(List<TestEntry> tests, string commit)
    {
        private readonly Dictionary<string, string> _pages = new(StringComparer.Ordinal);

        public Dictionary<string, string> Write()
        {
            var sections = tests.GroupBy(t => GetSection(t.Path), StringComparer.Ordinal)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            var output = new StringBuilder();
            void Line(string value = "") => output.Append(value).Append('\n');

            Line("# WPT coverage");
            Line();
            Line($"<!-- Generated by '{Command}'. Do not edit. -->");
            Line();
            Line($"Coverage of the [web-platform-tests](https://github.com/web-platform-tests/wpt) by `tests/Meziantou.Framework.WebPlatform.Wpt`, " +
                $"at the pinned WPT revision [`{commit[..12]}`](https://github.com/web-platform-tests/wpt/tree/{commit}) (`tests/Meziantou.Framework.WebPlatform.SpecData/versions.json`). " +
                "See [web-platform-tests.md](web-platform-tests.md) for how the tests are run. Each section links to the list of its tests, " +
                "with the reason why a test does not pass.");
            Line();
            Line("The tests are split in three groups:");
            Line();
            Line("- **Stable tests**: the testharness.js tests that are not marked as tentative: they test the behaviors defined by a specification (some of these specifications are still drafts or incubations). " +
                "They measure the conformance of the library.");
            Line("- **Tentative tests**: the testharness.js tests of the experimental features and of the proposed changes that no specification requires yet " +
                "(the [WPT convention](https://web-platform-tests.org/writing-tests/file-names.html): `.tentative.` in the file name or a `tentative` directory). " +
                "They may change or be removed, and browsers often do not pass them: they are reported separately and are not part of the stable coverage.");
            Line("- **Reftests**: the [reftests](https://web-platform-tests.org/writing-tests/reftests.html) (tentative ones included), which compare the rendering of a page with the rendering of its references, " +
                "and the [print reftests](https://web-platform-tests.org/writing-tests/print-reftests.html), which compare the pages of the printed documents: " +
                "they measure the layout, the pagination and the painting (see [web-platform-tests.md](web-platform-tests.md#reftests)).");
            Line();
            Line("The columns:");
            Line();
            Line("- **Tests**: the testharness.js test files of the section that run in a window or in a dedicated or shared worker (or in a service worker for the tests of `service-workers`) and the reftests, according to the " +
                "[WPT manifest](https://github.com/web-platform-tests/wpt/releases) of the revision, and the other files that are run (a file with several variants counts once: " +
                "a `.any.js` test passes when it passes in all its globals, a reftest when all its variants pass). " +
                "Visual tests, crash tests, manual tests, WebDriver tests, the tests that only run in a ShadowRealm, and the tests outside of `service-workers` that only run in service workers are not counted: " +
                "the library has no ShadowRealm, and the runner only runs the service worker variants of the tests of the service workers.");
            Line("- **Passing**: the testharness.js tests whose harness status is `OK` and whose subtests all pass with the Jint engine, and the reftests whose rendering matches their references (`expectations/*.json`).");
            Line("- **Disabled**: the tests that are not run: listed in `wpt-skip.json`, not fetched (`tests/Meziantou.Framework.WebPlatform.SpecData/sources.json`) or not part of a test group (`WptTests.cs`).");
            Line("- **Pass rate**: the passing tests among the tests that are run (the tests that are not disabled).");
            Line();
            Line("The pages of the sections give, for each test that does not pass, the **Kind** and the **Description** of the reasons why it does not pass " +
                "(a test can have several reasons). The kinds:");
            Line();
            foreach (var (name, description) in Kinds)
            {
                Line($"- **{(name is RequiresKind ? RequiresKind + "(features)" : name)}**: {description}.");
            }

            Line();
            Line("The features of `REQUIRES`:");
            Line();
            foreach (var (name, description) in Features)
            {
                Line($"- `{name}`: {description}.");
            }

            Line();
            Line($"The reasons come from `wpt-skip.json` and `wpt-reasons.json` (`tests/Meziantou.Framework.WebPlatform.Wpt`: `notRun`, `failing` for the testharness.js tests, `failingReftests` for the reftests): each pattern (a path, or a prefix followed by `*`) " +
                "maps to an array of `{ \"kind\": \"...\", \"features\": [\"...\"], \"description\": \"...\" }` (`features` only for `REQUIRES`). " +
                $"This page is generated: run `{Command}` after updating the expectations, the reasons or the pinned revision.");
            Line();
            Line("## Summary");
            Line();
            Line("| Group | Passing | Tests | Disabled | Pass rate |");
            Line("|---|--:|--:|--:|--:|");
            Line(Row("[Stable](#stable-tests)", Stats.From(tests.Where(t => t.Group is TestGroup.Stable))));
            Line(Row("[Tentative](#tentative-tests)", Stats.From(tests.Where(t => t.Group is TestGroup.Tentative))));
            Line(Row("[Reftests](#reftests)", Stats.From(tests.Where(t => t.Group is TestGroup.Reftest))));
            Line(Row("**All**", Stats.From(tests), bold: true));

            WriteIndexTable(Line, "Stable tests", sections, TestGroup.Stable);
            WriteIndexTable(Line, "Tentative tests", sections, TestGroup.Tentative);
            WriteIndexTable(Line, "Reftests", sections, TestGroup.Reftest);

            foreach (var section in sections)
            {
                WriteSection(GetPagePath(section.Key), section.Key, [.. section]);
            }

            _pages[IndexPath] = output.ToString();
            return _pages;
        }

        private static void WriteIndexTable(Action<string> line, string title, List<IGrouping<string, TestEntry>> sections, TestGroup group)
        {
            var rows = sections.Select(s => (Section: s.Key, Tests: s.Where(t => t.Group == group).ToList())).Where(s => s.Tests.Count > 0).ToList();
            line("");
            line("## " + title);
            line("");
            line("| Section | Passing | Tests | Disabled | Pass rate |");
            line("|---|--:|--:|--:|--:|");
            foreach (var (section, sectionTests) in rows)
            {
                var anchor = group switch
                {
                    TestGroup.Tentative => "#tentative-tests",
                    TestGroup.Reftest => "#reftests",
                    _ => "",
                };
                line(Row($"[`{section}`]({GetPagePath(section)}{anchor})", Stats.From(sectionTests)));
            }

            line(Row($"**Total ({Number(rows.Count)} sections)**", Stats.From(rows.SelectMany(r => r.Tests)), bold: true));
        }

        private void WriteSection(string pagePath, string section, List<TestEntry> sectionTests)
        {
            var output = new StringBuilder();
            void Line(string value = "") => output.Append(value).Append('\n');

            var stableTests = sectionTests.Where(t => t.Group is TestGroup.Stable).ToList();
            var tentativeTests = sectionTests.Where(t => t.Group is TestGroup.Tentative).ToList();
            var reftests = sectionTests.Where(t => t.Group is TestGroup.Reftest).ToList();
            var indexLink = string.Concat(Enumerable.Repeat("../", pagePath.Count(c => c == '/'))) + IndexPath;
            Line($"# WPT coverage: `{section}`");
            Line();
            Line($"<!-- Generated by '{Command}'. Do not edit. -->");
            Line();
            Line($"[WPT coverage]({indexLink}) of the tests of [`{section}`](https://github.com/web-platform-tests/wpt/tree/{commit}/{section}) " +
                $"at the revision `{commit[..12]}`. The tests link to their results in the browsers on [wpt.fyi](https://wpt.fyi). " +
                $"The [tentative tests]({indexLink}#tentative-tests) (experimental features) and the [reftests]({indexLink}#reftests) are listed separately from the stable tests.");
            Line();
            Line("| Group | Passing | Tests | Disabled | Pass rate |");
            Line("|---|--:|--:|--:|--:|");
            Line(Row(stableTests.Count > 0 ? "[Stable](#stable-tests)" : "Stable", Stats.From(stableTests)));
            Line(Row(tentativeTests.Count > 0 ? "[Tentative](#tentative-tests)" : "Tentative", Stats.From(tentativeTests)));
            Line(Row(reftests.Count > 0 ? "[Reftests](#reftests)" : "Reftests", Stats.From(reftests)));
            Line(Row("**All**", Stats.From(sectionTests), bold: true));

            WriteTestTable("Stable tests", stableTests, "Subtests");
            WriteTestTable("Tentative tests", tentativeTests, "Subtests");
            WriteTestTable("Reftests", reftests, "Variants");

            _pages[pagePath] = output.ToString();

            void WriteTestTable(string title, List<TestEntry> tableTests, string countColumn)
            {
                if (tableTests.Count == 0)
                    return;

                Line();
                Line("## " + title);
                Line();
                Line($"| Test | Result | {countColumn} | Kind | Description |");
                Line("|---|---|--:|---|---|");
                foreach (var test in tableTests)
                {
                    var name = test.Path[(section.Length + 1)..];
                    var subtests = test.Result is { } result ? $"{Number(result.Total - result.Failures)} / {Number(result.Total)}" : "–";
                    var (kind, description) = test.IsPassing ? ("", "")
                        : test.Reasons is null ? ("", "*No reason documented*")
                        : (FormatKinds(test.Reasons), Escape(string.Join("; ", test.Reasons.Select(r => r.Description))));
                    Line($"| [`{Escape(name)}`](https://wpt.fyi/results/{test.Path}) | {GetResult(test)} | {subtests} | {kind} | {description} |");
                }
            }
        }

        private static string GetPagePath(string section) => SectionDirectory + "/" + section + ".md";

        /// <summary>The section of a test: its top-level directory, or its first two directories for css and html (as the expectation files).</summary>
        private static string GetSection(string path)
        {
            var segments = path.Split('/');
            return segments[0] is "css" or "html" && segments.Length > 2 ? segments[0] + "/" + segments[1] : segments[0];
        }

        /// <summary>The distinct kinds of the reasons, the <see cref="RequiresKind"/> reasons being merged in <c>REQUIRES(feature1, feature2)</c>.</summary>
        private static string FormatKinds(IReadOnlyList<Reason> reasons) => string.Join(", ", reasons.GroupBy(r => r.Kind, StringComparer.Ordinal).Select(g =>
            g.Key is RequiresKind ? $"{RequiresKind}({string.Join(", ", g.SelectMany(r => r.Features).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))})" : g.Key));

        private static string GetResult(TestEntry test) => test.Result switch
        {
            null => "Disabled",
            { IsPassing: true } => "Pass",
            { Harness: "OK" } => "Fail",
            { Harness: "TIMEOUT" } => "Timeout",
            { Harness: "ERROR" } => "Error",
            { Harness: "PRECONDITION_FAILED" } => "Precondition failed",
            { Harness: var harness } => harness,
        };

        private static string Row(string name, Stats stats, bool bold = false)
        {
            string[] values = [Number(stats.Passing), Number(stats.Tests), Number(stats.Disabled), PassRate(stats)];
            return $"| {name} | " + string.Join(" | ", values.Select(v => bold ? "**" + v + "**" : v)) + " |";
        }

        private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

        private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

        private static string PassRate(Stats stats) => Percent(stats.Passing, stats.Tests - stats.Disabled);

        // A ratio is rounded to an integer, except when it would hide that it is not 0% or 100%
        private static string Percent(int value, int total)
        {
            if (total == 0)
                return "–";

            var ratio = 100.0 * value / total;
            if (value != 0 && ratio < 1)
                return ratio < 0.1 ? "<0.1%" : ratio.ToString("0.0", CultureInfo.InvariantCulture) + "%";

            if (value != total && ratio >= 99.5)
                return (Math.Floor(ratio * 10) / 10).ToString("0.0", CultureInfo.InvariantCulture) + "%";

            return ratio.ToString("0", CultureInfo.InvariantCulture) + "%";
        }
    }
}
