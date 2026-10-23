using ChromiumDiff;

// Differential testing of the HTML parser: each input of the corpus is parsed by Chromium (Playwright) and by the
// library, as a document and as a fragment in many context elements, with the scripting disabled and enabled, and the
// trees (html5lib format), the serializations and the trees of the serializations are compared (see TreeComparison.cs).
// The differences that are deviations of Chromium from the HTML Standard are listed with their reason in
// known-differences.json.
//
// Usage:
//   ChromiumDiff install [--with-deps]                installs the Chromium build of Playwright (and its system dependencies)
//   ChromiumDiff [--filter <text>] [--pages]          compares the trees of the corpus: the tree construction tests and the
//                [--corpus <dir>] [--report <file>]   seeds of the fuzzing target (unless --no-tests), the pages of
//                [--exclude-nul] [--no-tests]         pages.txt (--pages, downloaded once, only parsed as documents), the
//                [--generate <count>] [--seed <n>]    files of the --corpus directories (without the inputs that contain
//                [--documents | --fragments]          U+0000 with --exclude-nul), and <count> generated inputs; only as
//                [--context <name>]...                documents or as fragments, in the given contexts (e.g. "td",
//                [--parallelism <n>]                  "svg path", "body quirks"); --browser compares with
//                [--browser chromium|firefox|webkit]  another browser, to triage the differences; --minimize
//                [--minimize]                         reduces the input of each unexpected difference
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
var tests = true;
var generate = 0;
var seed = 1;
var documents = true;
var fragments = true;
HashSet<string>? contexts = null;
int? parallelism = null;
var browser = "chromium";
var minimize = false;
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
        case "--no-tests":
            tests = false;
            break;
        case "--generate":
            generate = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--seed":
            seed = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
            break;
        case "--documents":
            fragments = false;
            break;
        case "--fragments":
            documents = false;
            break;
        case "--context":
            (contexts ??= new HashSet<string>(StringComparer.Ordinal)).Add(args[++i]);
            break;
        case "--minimize":
            minimize = true;
            break;
        case "--browser":
            browser = args[++i];
            break;
        case "--parallelism":
            parallelism = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
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

var treeOptions = new TreeComparisonOptions
{
    Filter = filter,
    ReportPath = reportPath,
    Tests = tests,
    Pages = pages,
    ExcludeNul = excludeNul,
    Generate = generate,
    Seed = seed,
    Documents = documents,
    Fragments = fragments,
    Contexts = contexts,
    Browser = browser,
    Minimize = minimize,
};

if (parallelism is { } value)
{
    treeOptions.Parallelism = value;
}

treeOptions.CorpusDirectories.AddRange(corpusDirectories);
return await TreeComparison.RunAsync(root, treeOptions);

static string FindRoot()
{
    var directory = AppContext.BaseDirectory;
    while (directory is not null && !File.Exists(Path.Combine(directory, "Meziantou.Framework.slnx")))
    {
        directory = Path.GetDirectoryName(directory);
    }

    return directory ?? Directory.GetCurrentDirectory();
}
