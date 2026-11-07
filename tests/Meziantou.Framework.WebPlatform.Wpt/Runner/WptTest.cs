using System.Text.RegularExpressions;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

internal enum WptTestKind
{
    Html,
    Xml,
    AnyJs,
    WindowJs,

    /// <summary>A <c>.any.js</c> test run in a dedicated worker (<c>.any.worker.html</c>).</summary>
    AnyJsDedicatedWorker,

    /// <summary>A <c>.any.js</c> test run in a module dedicated worker (<c>.any.worker-module.html</c>).</summary>
    AnyJsDedicatedWorkerModule,

    /// <summary>A <c>.any.js</c> test run in a shared worker (<c>.any.sharedworker.html</c>).</summary>
    AnyJsSharedWorker,

    /// <summary>A <c>.any.js</c> test run in a module shared worker (<c>.any.sharedworker-module.html</c>).</summary>
    AnyJsSharedWorkerModule,

    /// <summary>A <c>.worker.js</c> test run in a dedicated worker (<c>.worker.html</c>).</summary>
    WorkerJs,

    /// <summary>A <c>.any.js</c> test run in a service worker (<c>.any.serviceworker.html</c>).</summary>
    AnyJsServiceWorker,

    /// <summary>A <c>.any.js</c> test run in a module service worker (<c>.any.serviceworker-module.html</c>).</summary>
    AnyJsServiceWorkerModule,
}

/// <summary>A testharness.js test of the web-platform-tests (a file, and a variant when the test has several).</summary>
internal sealed record WptTest(string RelativePath, WptTestKind Kind, string Variant)
{
    /// <summary>
    /// The identifier of the test in the expectations: the path of the file and the variant, and the global of a <c>.any.js</c>
    /// test run in a worker after a '#' (e.g. <c>url/url-constructor.any.js#dedicatedworker</c>).
    /// </summary>
    public string Id => GlobalName is { } global ? RelativePath + "#" + global + Variant : RelativePath + Variant;

    /// <summary>The name of the global of a <c>.any.js</c> test run in a worker (https://web-platform-tests.org/writing-tests/testharness.html#specifying-a-test-scope), or null.</summary>
    public string? GlobalName => Kind switch
    {
        WptTestKind.AnyJsDedicatedWorker => "dedicatedworker",
        WptTestKind.AnyJsDedicatedWorkerModule => "dedicatedworker-module",
        WptTestKind.AnyJsSharedWorker => "sharedworker",
        WptTestKind.AnyJsSharedWorkerModule => "sharedworker-module",
        WptTestKind.AnyJsServiceWorker => "serviceworker",
        WptTestKind.AnyJsServiceWorkerModule => "serviceworker-module",
        _ => null,
    };

    /// <summary>Whether the test runs in a worker.</summary>
    public bool IsWorker => GlobalName is not null || Kind == WptTestKind.WorkerJs;

    public string FilePath => Path.Combine(WptPaths.Wpt, RelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The URL of the test page, as served by wptserve.</summary>
    public string Url
    {
        get
        {
            var path = Kind switch
            {
                WptTestKind.AnyJs => RelativePath[..^".any.js".Length] + ".any.html",
                WptTestKind.WindowJs => RelativePath[..^".window.js".Length] + ".window.html",
                WptTestKind.AnyJsDedicatedWorker => RelativePath[..^".any.js".Length] + ".any.worker.html",
                WptTestKind.AnyJsDedicatedWorkerModule => RelativePath[..^".any.js".Length] + ".any.worker-module.html",
                WptTestKind.AnyJsSharedWorker => RelativePath[..^".any.js".Length] + ".any.sharedworker.html",
                WptTestKind.AnyJsSharedWorkerModule => RelativePath[..^".any.js".Length] + ".any.sharedworker-module.html",
                WptTestKind.AnyJsServiceWorker => RelativePath[..^".any.js".Length] + ".any.serviceworker.html",
                WptTestKind.AnyJsServiceWorkerModule => RelativePath[..^".any.js".Length] + ".any.serviceworker-module.html",
                WptTestKind.WorkerJs => RelativePath[..^".worker.js".Length] + ".worker.html",
                _ => RelativePath,
            };

            // The tests whose name or variant has the "https" flag, and the tests run in a service worker, are served over HTTPS (https://web-platform-tests.org/writing-tests/file-names.html),
            // and the tests with the "h2" flag of a variant (wpt_flags=h2) by the HTTP/2 server, as in tools/manifest/item.py
            var flags = Path.GetFileName(RelativePath).Split('.').Concat(GetVariantFlags()).ToList();
            var origin = flags.Contains("h2", StringComparer.Ordinal) ? "https://web-platform.test:9000/"
                : flags.Contains("https", StringComparer.Ordinal) || Kind is WptTestKind.AnyJsServiceWorker or WptTestKind.AnyJsServiceWorkerModule ? "https://web-platform.test:8443/"
                : "http://web-platform.test:8000/";
            return origin + path + Variant;
        }
    }

    /// <summary>The values of the <c>wpt_flags</c> parameters of the query of the variant (e.g. <c>?wpt_flags=h2</c>).</summary>
    private IEnumerable<string> GetVariantFlags() => Variant.TrimStart('?').Split('&')
        .Where(parameter => parameter.StartsWith("wpt_flags=", StringComparison.Ordinal))
        .Select(parameter => Uri.UnescapeDataString(parameter["wpt_flags=".Length..]));

    public override string ToString() => Id;
}

internal static partial class WptTestDiscovery
{
    private static readonly string[] ExcludedDirectories = ["resources", "support", "crashtests", "tools", "reference", "references"];

    /// <summary>The patterns of the tests listed in wpt-skip.json (a trailing * matches any suffix). The reasons why they are skipped are only used by the WPT coverage report.</summary>
    private static readonly Lazy<string[]> Skipped = new(() =>
    {
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(WptPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Wpt", "wpt-skip.json")));
        return [.. document.RootElement.EnumerateObject().Select(p => p.Name)];
    });

    internal static bool IsSkipped(string relativePath)
    {
        foreach (var pattern in Skipped.Value)
        {
            if (pattern.EndsWith('*', StringComparison.Ordinal) ? relativePath.StartsWith(pattern[..^1], StringComparison.Ordinal) : relativePath == pattern)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Discovers the tests of a directory. A '*' segment matches any directory (e.g. css/*/parsing); the directories
    /// matched by a pattern that are tested by another test group are listed in <paramref name="excludedDirectories"/>.
    /// </summary>
    public static IEnumerable<WptTest> Discover(string directory, params string[] excludedDirectories)
    {
        var files = ExpandDirectories(directory)
            .Where(d => !excludedDirectories.Contains(Path.GetRelativePath(WptPaths.Wpt, d).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal))
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(WptPaths.Wpt, file).Replace(Path.DirectorySeparatorChar, '/');

            // The subdirectories excluded from a directory (e.g. html/syntax/speculative-parsing in html/syntax)
            if (excludedDirectories.Any(d => relativePath.StartsWith(d + "/", StringComparison.Ordinal)))
                continue;

            var segments = relativePath.Split('/');
            if (segments.Any(s => ExcludedDirectories.Contains(s, StringComparer.Ordinal)))
                continue;

            if (IsSkipped(relativePath))
                continue;

            var fileName = segments[^1];
            // Reftest references ('-ref' / '-notref') are excluded by the testharness.js check below: a file that includes
            // testharness.js is a testharness test whatever its name (e.g. custom-state-set-strong-ref.html)
            if (fileName.Contains("-manual.", StringComparison.Ordinal) || fileName.Contains("-crash.", StringComparison.Ordinal))
                continue;

            var isHtml = fileName.EndsWith(".html", StringComparison.Ordinal) || fileName.EndsWith(".htm", StringComparison.Ordinal);
            var isXml = fileName.EndsWith(".xhtml", StringComparison.Ordinal) || fileName.EndsWith(".xht", StringComparison.Ordinal) || fileName.EndsWith(".svg", StringComparison.Ordinal) || fileName.EndsWith(".xml", StringComparison.Ordinal);
            if (isHtml || isXml)
            {
                var content = File.ReadAllText(file);
                if (!content.Contains("/resources/testharness.js", StringComparison.Ordinal))
                    continue;

                foreach (var variant in GetVariants(HtmlVariantRegex().Matches(content).Select(m => m.Groups["variant"].Value)))
                {
                    yield return new WptTest(relativePath, isHtml ? WptTestKind.Html : WptTestKind.Xml, variant);
                }
            }
            else if (fileName.EndsWith(".any.js", StringComparison.Ordinal))
            {
                var meta = ParseMeta(File.ReadAllText(file));
                foreach (var kind in GetAnyJsKinds(meta, includeServiceWorkers: relativePath.StartsWith("service-workers/", StringComparison.Ordinal)))
                {
                    foreach (var variant in GetVariants(meta.Where(m => m.Key == "variant").Select(m => m.Value)))
                    {
                        yield return new WptTest(relativePath, kind, variant);
                    }
                }
            }
            else if (fileName.EndsWith(".window.js", StringComparison.Ordinal) || fileName.EndsWith(".worker.js", StringComparison.Ordinal))
            {
                var meta = ParseMeta(File.ReadAllText(file));
                var kind = fileName.EndsWith(".window.js", StringComparison.Ordinal) ? WptTestKind.WindowJs : WptTestKind.WorkerJs;
                foreach (var variant in GetVariants(meta.Where(m => m.Key == "variant").Select(m => m.Value)))
                {
                    yield return new WptTest(relativePath, kind, variant);
                }
            }
        }
    }

    private static IEnumerable<string> ExpandDirectories(string directory)
    {
        IEnumerable<string> directories = [WptPaths.Wpt];
        foreach (var segment in directory.Split('/'))
        {
            directories = segment == "*"
                ? directories.SelectMany(d => Directory.EnumerateDirectories(d))
                : directories.Select(d => Path.Combine(d, segment)).Where(Directory.Exists);
        }

        return directories;
    }

    private static IEnumerable<string> GetVariants(IEnumerable<string> variants)
    {
        var list = variants.ToList();
        return list.Count == 0 ? [""] : list;
    }

    /// <summary>
    /// The globals in which a <c>.any.js</c> test runs (https://web-platform-tests.org/writing-tests/testharness.html#specifying-a-test-scope):
    /// the window and the dedicated worker by default. The service worker globals are only run for the tests of the service
    /// workers (<paramref name="includeServiceWorkers"/>); the ShadowRealms are not supported.
    /// </summary>
    private static IEnumerable<WptTestKind> GetAnyJsKinds(List<KeyValuePair<string, string>> meta, bool includeServiceWorkers)
    {
        var globals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var global in meta.Where(m => m.Key == "global").SelectMany(m => m.Value.Split(',')).Select(g => g.Trim()))
        {
            switch (global)
            {
                case "default":
                    globals.Add("window");
                    globals.Add("dedicatedworker");
                    break;
                case "worker":
                    globals.Add("dedicatedworker");
                    globals.Add("sharedworker");
                    globals.Add("serviceworker");
                    break;
                case "worker-module":
                    globals.Add("dedicatedworker-module");
                    globals.Add("sharedworker-module");
                    globals.Add("serviceworker-module");
                    break;
                default:
                    globals.Add(global);
                    break;
            }
        }

        if (globals.Count == 0)
        {
            globals.Add("window");
            globals.Add("dedicatedworker");
        }

        if (globals.Contains("window"))
            yield return WptTestKind.AnyJs;

        if (globals.Contains("dedicatedworker"))
            yield return WptTestKind.AnyJsDedicatedWorker;

        if (globals.Contains("dedicatedworker-module"))
            yield return WptTestKind.AnyJsDedicatedWorkerModule;

        if (globals.Contains("sharedworker"))
            yield return WptTestKind.AnyJsSharedWorker;

        if (globals.Contains("sharedworker-module"))
            yield return WptTestKind.AnyJsSharedWorkerModule;

        if (includeServiceWorkers && globals.Contains("serviceworker"))
            yield return WptTestKind.AnyJsServiceWorker;

        if (includeServiceWorkers && globals.Contains("serviceworker-module"))
            yield return WptTestKind.AnyJsServiceWorkerModule;
    }

    /// <summary>Parses the <c>// META: key=value</c> comments of a JavaScript test.</summary>
    public static List<KeyValuePair<string, string>> ParseMeta(string content)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var line in content.Split('\n'))
        {
            var match = MetaRegex().Match(line);
            if (!match.Success)
            {
                if (line.Trim().Length > 0 && !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    break;

                continue;
            }

            result.Add(new(match.Groups["key"].Value, match.Groups["value"].Value.Trim()));
        }

        return result;
    }

    [GeneratedRegex("""<meta\s+name=["']?variant["']?(?:\s+[^\s"'>=]+)*?\s+content=(?:"(?<variant>[^"]*)"|'(?<variant>[^']*)'|(?<variant>[^\s"'>]+))""", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlVariantRegex();

    [GeneratedRegex(@"^//\s*META:\s*(?<key>\w+)=(?<value>.*)$")]
    private static partial Regex MetaRegex();
}
