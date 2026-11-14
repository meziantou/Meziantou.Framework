using System.IO.Compression;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// A crash test of the web-platform-tests (https://web-platform-tests.org/writing-tests/crashtest.html): a page that passes when
/// it loads and is rendered without crashing or hanging the browser. A test with several variants (<c>?query</c>) has a test
/// per variant.
/// </summary>
/// <param name="RelativePath">The path of the file of the test, relative to the root of the test suite.</param>
/// <param name="Url">The URL of the test, relative to the root of the test suite (the path of the file and the query of the variant).</param>
/// <param name="LongTimeout">Whether the test has the long timeout (<c>&lt;meta name=timeout content=long&gt;</c>).</param>
internal sealed record WptCrashtest(string RelativePath, string Url, bool LongTimeout)
{
    /// <summary>The identifier of the test in the expectations: the path of the file and the query of the variant.</summary>
    public string Id => Url;

    public override string ToString() => Id;
}

/// <summary>The result of a crash test: <c>PASS</c>, <c>ERROR</c> (the page cannot be loaded or rendered) or <c>TIMEOUT</c> (the page never completes).</summary>
internal sealed record WptCrashtestResult(string Status, string? Message, IReadOnlyList<string> Log);

/// <summary>Discovers the crash tests: the <c>crashtest</c> items of the manifest whose file exists.</summary>
internal static class WptCrashtestDiscovery
{
    private static readonly Lazy<Dictionary<string, WptCrashtest>> Items = new(Read);

    /// <summary>Gets whether the crash tests are run: <c>WPT_CRASHTESTS=0</c> skips them.</summary>
    public static bool IsEnabled => Environment.GetEnvironmentVariable("WPT_CRASHTESTS") is not ("0" or "false");

    /// <summary>
    /// Whether a markup file is a crash test, as for the manifest (<c>tools/manifest/sourcefile.py</c>): a file of a
    /// <c>crashtests</c> directory, or a file whose type flag (the part of the name after its last <c>-</c>) is <c>crash</c>
    /// (e.g. <c>foo-crash.html</c>, <c>foo-crash.tentative.html</c>).
    /// </summary>
    public static bool IsCrashtestPath(string relativePath)
    {
        var segments = relativePath.Split('/');
        if (segments.AsSpan(0, segments.Length - 1).Contains("crashtests"))
            return true;

        var name = Path.GetFileNameWithoutExtension(segments[^1]);
        var dash = name.LastIndexOf('-', StringComparison.Ordinal);
        return dash >= 0 && name[(dash + 1)..].Split('.')[0] == "crash";
    }

    /// <summary>
    /// Discovers the crash tests of the fetched directories under a directory, except the ones of <c>wpt-skip.json</c> and of the
    /// excluded directories.
    /// </summary>
    public static IEnumerable<WptCrashtest> Discover(string directory, params string[] excludedDirectories)
    {
        var prefix = directory.Length == 0 ? "" : directory + "/";
        foreach (var (url, test) in Items.Value.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var relativePath = test.RelativePath;
            if (!relativePath.StartsWith(prefix, StringComparison.Ordinal) || excludedDirectories.Any(d => relativePath.StartsWith(d + "/", StringComparison.Ordinal)))
                continue;

            if (WptTestDiscovery.IsSkipped(relativePath) || !File.Exists(Path.Combine(WptPaths.Wpt, relativePath.Replace('/', Path.DirectorySeparatorChar))))
                continue;

            yield return test;
        }
    }

    private static Dictionary<string, WptCrashtest> Read()
    {
        var result = new Dictionary<string, WptCrashtest>(StringComparer.Ordinal);
        var manifestPath = Path.Combine(WptPaths.Wpt, "MANIFEST.json.gz");
        if (!File.Exists(manifestPath))
            return result;

        using var stream = new GZipStream(File.OpenRead(manifestPath), CompressionMode.Decompress);
        using var document = JsonDocument.Parse(stream);
        if (document.RootElement.GetProperty("items").TryGetProperty("crashtest", out var crashtests))
        {
            Walk(crashtests, "");
        }

        return result;

        // The items are a tree of directories whose leaves are the test files: [hash, [url or null, extras], ...]
        void Walk(JsonElement directory, string prefix)
        {
            foreach (var property in directory.EnumerateObject())
            {
                var path = prefix + property.Name;
                if (property.Value.ValueKind is JsonValueKind.Object)
                {
                    Walk(property.Value, path + "/");
                    continue;
                }

                foreach (var variant in property.Value.EnumerateArray().Skip(1))
                {
                    var url = variant[0].ValueKind is JsonValueKind.String ? variant[0].GetString()!.TrimStart('/') : path;
                    var longTimeout = variant[1].TryGetProperty("timeout", out var timeout) && timeout.GetString() == "long";
                    result[url] = new WptCrashtest(path, url, longTimeout);
                }
            }
        }
    }
}

/// <summary>
/// Runs a crash test as wptrunner does (https://web-platform-tests.org/writing-tests/crashtest.html): the page is loaded, the
/// runner waits for the <c>load</c> event and for the removal of the <c>test-wait</c> class of the root element (as for a
/// reftest), and the page is rendered. The test passes when this completes; an exception of the library is a crash
/// (<c>ERROR</c>), and a page that never completes is a <c>TIMEOUT</c>. The errors of the scripts of the page do not matter.
/// </summary>
internal static class WptCrashtestRunner
{
    public static WptCrashtestResult Run(WptCrashtest test)
    {
        var queryIndex = test.Url.IndexOfAny(['?', '#']);
        var extension = Path.GetExtension(test.RelativePath);
        var kind = extension is ".xht" or ".xhtml" or ".svg" or ".xml" ? WptTestKind.Xml : WptTestKind.Html;
        WptPageRendering rendering;
        try
        {
            rendering = WptTestRunner.Render(new WptTest(test.RelativePath, kind, queryIndex < 0 ? "" : test.Url[queryIndex..]), WptReftestRunner.Backend, test.LongTimeout);
        }
        catch (Exception ex)
        {
            return new WptCrashtestResult("ERROR", ex.GetType().Name + ": " + ex.Message, []);
        }

        rendering.Screenshot?.Dispose();
        return rendering.Screenshot is not null
            ? new WptCrashtestResult("PASS", null, rendering.Log)
            : new WptCrashtestResult(rendering.Status is "TIMEOUT" ? "TIMEOUT" : "ERROR", rendering.Status + " " + rendering.Message, rendering.Log);
    }
}
