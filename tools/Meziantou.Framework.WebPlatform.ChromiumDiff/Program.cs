using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ChromiumDiff;
using Meziantou.Framework.WebPlatform;
using Microsoft.Playwright;

// Differential testing of the HTML parser: each input of the corpus is parsed by Chromium (Playwright) and by the
// library, and the trees are compared (html5lib format). The differences that are deviations of Chromium from the HTML
// Standard are listed with their reason in known-differences.json.
//
// Usage:
//   ChromiumDiff install [--with-deps]                installs the Chromium build of Playwright (and its system dependencies)
//   ChromiumDiff [--filter <text>] [--pages]          compares the trees of the corpus: the tree construction tests, the
//                [--corpus <dir>] [--report <file>]   pages of pages.txt (--pages, downloaded once) and the files of the
//                [--exclude-nul]                      --corpus directories (without the inputs that contain U+0000)
//   ChromiumDiff --layout [--filter <text>]           compares the layout of the pages of pages.txt (with --pages or
//                [--pages] [--corpus <dir>]           without --corpus) and of the HTML files of the --corpus directories
//                [--record] [--tolerance <px>]        (see LayoutComparison.cs); --record records the subresources of
//                [--viewport <width>x<height>]        the pages again
//                [--max-differences <n>]               --inspect prints the computed values of the elements that
//                [--inspect <selector>]               match the selector in both (the --properties, or a default
//                [--properties <name,name...>]        list), to triage the differences
//                [--report <file>] [--json <file>]
var root = FindRoot();
if (args is ["install", .. var installOptions])
    return Microsoft.Playwright.Program.Main(["install", .. installOptions, "chromium"]);

string? filter = null;
string? reportPath = null;
var pages = false;
var excludeNul = false;
var layout = false;
var record = false;
var tolerance = 1.0;
var viewportWidth = 1280;
var viewportHeight = 800;
var maxDifferences = 10;
string? jsonReportPath = null;
string? inspect = null;
string[]? inspectedProperties = null;
var corpusDirectories = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--filter":
            filter = args[++i];
            break;
        case "--corpus":
            corpusDirectories.Add(args[++i]);
            break;
        case "--pages":
            pages = true;
            break;
        case "--exclude-nul":
            // Chromium handles U+0000 in its input preprocessor: it drops the NULs in the data state (e.g. a NUL before
            // the DOCTYPE does not switch to quirks mode) and replaces them by U+FFFD in the other states (e.g. "<\0" in a
            // text is "<\uFFFD" instead of "<"). The fuzzing corpora have many NULs.
            excludeNul = true;
            break;
        case "--report":
            reportPath = args[++i];
            break;
        case "--layout":
            layout = true;
            break;
        case "--record":
            record = true;
            break;
        case "--tolerance":
            tolerance = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--viewport":
            var size = args[++i].Split('x');
            viewportWidth = int.Parse(size[0], System.Globalization.CultureInfo.InvariantCulture);
            viewportHeight = int.Parse(size[1], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--max-differences":
            maxDifferences = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--inspect":
            inspect = args[++i];
            break;
        case "--properties":
            inspectedProperties = args[++i].Split(',');
            break;
        case "--json":
            jsonReportPath = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument '{args[i]}'");
            return 1;
    }
}

if (layout)
{
    var layoutOptions = new LayoutOptions
    {
        Filter = filter,
        ReportPath = reportPath,
        JsonReportPath = jsonReportPath,
        Pages = pages || corpusDirectories.Count == 0,
        Record = record,
        Width = viewportWidth,
        Height = viewportHeight,
        Tolerance = tolerance,
        MaxDifferences = maxDifferences,
        Inspect = inspect,
        InspectedProperties = inspectedProperties ?? LayoutOptions.DefaultInspectedProperties,
    };

    layoutOptions.CorpusDirectories.AddRange(corpusDirectories);
    return await LayoutComparison.RunAsync(root, layoutOptions);
}

var knownDifferencesPath = Path.Combine(root, "tools", "Meziantou.Framework.WebPlatform.ChromiumDiff", "known-differences.json");
var knownDifferences = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(knownDifferencesPath)) ?? [];

var inputs = Corpus.Load(root, pages, corpusDirectories)
    .Where(input => filter is null || input.Id.Contains(filter, StringComparison.Ordinal))
    .Where(input => !excludeNul || !input.Html.Contains('\0', StringComparison.Ordinal))
    .ToList();
Console.WriteLine($"{inputs.Count} input(s)");

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
Console.WriteLine($"Chromium {browser.Version}");

// The scripts are disabled, so the scripting flag of the parser is disabled as in the library (noscript elements are
// parsed as normal elements), and the document is the result of the parser only
var context = await browser.NewContextAsync(new BrowserNewContextOptions { JavaScriptEnabled = false });
var page = await context.NewPageAsync();
var dumpScript = ReadResource("dump.js");
byte[] currentInput = [];
string? currentUrl = null;
await context.RouteAsync("**/*", async route =>
{
    if (route.Request.Url == currentUrl && route.Request.IsNavigationRequest)
    {
        await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "text/html; charset=utf-8", BodyBytes = currentInput });
    }
    else
    {
        // The subresources and the other navigations (e.g. a meta refresh, an iframe) are not loaded
        await route.AbortAsync();
    }
});

var differences = new List<(Input Input, string Diff)>();
var stopwatch = Stopwatch.StartNew();
var index = 0;
foreach (var input in inputs)
{
    index++;
    currentInput = Encoding.UTF8.GetBytes(input.Html);
    currentUrl = $"https://chromium-diff.test/{index}";
    await page.GotoAsync(currentUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
    var chromium = await page.EvaluateAsync<string>(dumpScript);

    var document = HtmlDocumentParser.ParseDocument(input.Html);
    var webPlatform = TreeDumper.Dump(document);
    if (!string.Equals(chromium, webPlatform, StringComparison.Ordinal))
    {
        differences.Add((input, TreeDumper.Diff(chromium, webPlatform)));
    }
}

Console.WriteLine($"Compared {inputs.Count} input(s) in {stopwatch.Elapsed.TotalSeconds:F1} s");

var report = new StringBuilder();
var unexpected = differences.Where(d => !knownDifferences.ContainsKey(d.Input.Id)).ToList();
foreach (var (input, diff) in unexpected)
{
    report.Append("UNEXPECTED DIFFERENCE ").Append(input.Id).Append('\n');
    report.Append("  input: ").Append(JsonSerializer.Serialize(input.Html.Length > 500 ? input.Html[..500] + "…" : input.Html)).Append('\n');
    report.Append(diff).Append('\n');
}

// A known difference that is no longer different must be removed from the list (only for the inputs that were compared)
var comparedIds = inputs.Select(input => input.Id).ToHashSet(StringComparer.Ordinal);
var differentIds = differences.Select(d => d.Input.Id).ToHashSet(StringComparer.Ordinal);
var stale = knownDifferences.Keys.Where(id => comparedIds.Contains(id) && !differentIds.Contains(id)).Order(StringComparer.Ordinal).ToList();
foreach (var id in stale)
{
    report.Append("STALE KNOWN DIFFERENCE ").Append(id).Append(" (the trees are now the same: remove it from known-differences.json)\n");
}

Console.Write(report);
if (reportPath is not null)
{
    await File.WriteAllTextAsync(reportPath, report.ToString());
}

Console.WriteLine($"{differences.Count} difference(s): {differences.Count - unexpected.Count} known, {unexpected.Count} unexpected; {stale.Count} stale known difference(s)");
return unexpected.Count == 0 && stale.Count == 0 ? 0 : 1;

static string ReadResource(string name)
{
    using var stream = typeof(TreeDumper).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Resource '{name}' not found");
    using var reader = new StreamReader(stream);
    return reader.ReadToEnd();
}

static string FindRoot()
{
    var directory = AppContext.BaseDirectory;
    while (directory is not null && !File.Exists(Path.Combine(directory, "Meziantou.Framework.slnx")))
    {
        directory = Path.GetDirectoryName(directory);
    }

    return directory ?? Directory.GetCurrentDirectory();
}
