using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// A reftest of the web-platform-tests (https://web-platform-tests.org/writing-tests/reftests.html): a page whose rendering is
/// compared with the rendering of its references. A test with several variants (<c>?query</c>) has a test per variant.
/// </summary>
/// <param name="RelativePath">The path of the file of the test, relative to the root of the test suite.</param>
/// <param name="Url">The URL of the test, relative to the root of the test suite (the path of the file and the query of the variant).</param>
/// <param name="IsPrint">Whether the test is a print reftest (https://web-platform-tests.org/writing-tests/print-reftests.html): its pages are compared.</param>
internal sealed record WptReftest(string RelativePath, string Url, bool IsPrint = false)
{
    /// <summary>The identifier of the test in the expectations: the path of the file and the query of the variant.</summary>
    public string Id => Url;

    public override string ToString() => Id;
}

/// <summary>A reference of a reftest or of a reference: <paramref name="Url"/> must match (<c>==</c>) or mismatch (<c>!=</c>) the page that links it.</summary>
/// <param name="Url">The URL of the reference, relative to the root of the test suite (without the leading '/').</param>
/// <param name="IsMatch">Whether the reference must match the page (<c>==</c>) or mismatch it (<c>!=</c>).</param>
internal sealed record WptReference(string Url, bool IsMatch)
{
    public string Relation => IsMatch ? "==" : "!=";
}

/// <summary>
/// The fuzziness allowed for a comparison (https://web-platform-tests.org/writing-tests/reftests.html#fuzzy-matching): inclusive
/// ranges of the maximum difference of a color channel and of the number of different pixels.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WptFuzzy(int MinDifference, int MaxDifference, int MinPixels, int MaxPixels)
{
    public bool IsExact => MaxDifference == 0 && MaxPixels == 0;

    public override string ToString() => $"maxDifference={MinDifference}-{MaxDifference};totalPixels={MinPixels}-{MaxPixels}";
}

/// <summary>The key of a fuzziness: the comparison of a page with a reference (any page when <see cref="TestUrl"/> is null, any relation when <see cref="Relation"/> is null).</summary>
internal sealed record WptFuzzyKey(string? TestUrl, string ReferenceUrl, string? Relation);

/// <summary>
/// A page of the reference tree of a reftest: the test, or a reference that has its own references (a reference can itself be
/// a reftest). <see cref="Fuzzy"/> is the fuzziness of the comparisons of the page with its references, from the
/// <c>&lt;meta name=fuzzy&gt;</c> elements of the page (read by the manifest).
/// </summary>
internal sealed record WptReftestNode(string Url, IReadOnlyList<WptReference> References, IReadOnlyList<(WptFuzzyKey? Key, WptFuzzy Value)> Fuzzy, bool LongTimeout)
{
    /// <summary>Whether the page is a print reftest (a <c>print-reftest</c> item of the manifest): the test and its references are printed and their pages compared.</summary>
    public bool IsPrint { get; init; }

    /// <summary>
    /// The pages compared by a print reftest, by URL of the page (the test or a reference): the ranges of page numbers of the
    /// <c>&lt;meta name=reftest-pages&gt;</c> elements (1-based, inclusive; null for an open end), or null for all the pages.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<(int? Start, int? End)>>? PageRanges { get; init; }

    /// <summary>Returns the indexes (0-based) of the pages of a page compared by a print reftest among its <paramref name="pageCount"/> pages (as <c>get_pages</c> of wptrunner).</summary>
    public IReadOnlyList<int> GetComparedPages(string url, int pageCount)
    {
        if (PageRanges is null || !PageRanges.TryGetValue(url, out var ranges) || ranges.Count == 0)
            return [.. Enumerable.Range(0, pageCount)];

        var result = new SortedSet<int>();
        foreach (var (start, end) in ranges)
        {
            var first = start ?? 1;
            var last = end ?? pageCount;
            if (first > pageCount)
                continue;

            for (var page = first; page <= last; page++)
            {
                result.Add(page - 1);
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Returns the fuzziness of the comparison of the page with a reference: the value whose key is the page, the reference and
    /// the relation, then the value of the reference, then the value without key (as <c>get_fuzzy</c> of wptrunner).
    /// </summary>
    public WptFuzzy GetFuzzy(WptReference reference)
    {
        WptFuzzy? referenceOnly = null;
        WptFuzzy? any = null;
        foreach (var (key, value) in Fuzzy)
        {
            if (key is null)
            {
                any ??= value;
            }
            else if (key.ReferenceUrl == reference.Url && (key.TestUrl is null || key.TestUrl == Url) && (key.Relation is null || key.Relation == reference.Relation))
            {
                if (key.TestUrl is not null)
                    return value;

                referenceOnly ??= value;
            }
        }

        return referenceOnly ?? any ?? default;
    }
}

/// <summary>
/// The reftests of the manifest of the pinned revision (<c>tests/Meziantou.Framework.WebPlatform.External/wpt/MANIFEST.json.gz</c>, items of type <c>reftest</c>
/// and <c>print-reftest</c>): their references, their fuzziness, their timeout and the pages compared by the print reftests.
/// The visual tests (<c>visual</c>) are another type of the manifest, so they are not run.
/// </summary>
internal static class WptReftestManifest
{
    private static readonly Lazy<Dictionary<string, WptReftestNode>> Nodes = new(Read);

    /// <summary>Gets the reftests of the manifest by URL (relative to the root of the test suite).</summary>
    public static IReadOnlyDictionary<string, WptReftestNode> All => Nodes.Value;

    /// <summary>Returns the page of a URL: a reftest of the manifest, or a reference without references.</summary>
    public static WptReftestNode GetNode(string url) => Nodes.Value.TryGetValue(url, out var node) ? node : new WptReftestNode(url, [], [], LongTimeout: false);

    private static Dictionary<string, WptReftestNode> Read()
    {
        var result = new Dictionary<string, WptReftestNode>(StringComparer.Ordinal);
        var manifestPath = Path.Combine(WptPaths.Wpt, "MANIFEST.json.gz");
        if (!File.Exists(manifestPath))
            return result;

        using var stream = new GZipStream(File.OpenRead(manifestPath), CompressionMode.Decompress);
        using var document = JsonDocument.Parse(stream);
        var items = document.RootElement.GetProperty("items");
        if (items.TryGetProperty("reftest", out var reftests))
        {
            Walk(reftests, "", isPrint: false);
        }

        if (items.TryGetProperty("print-reftest", out var printReftests))
        {
            Walk(printReftests, "", isPrint: true);
        }

        return result;

        // The items are a tree of directories whose leaves are the test files: [hash, [url or null, references, extras], ...]
        void Walk(JsonElement directory, string prefix, bool isPrint)
        {
            foreach (var property in directory.EnumerateObject())
            {
                var path = prefix + property.Name;
                if (property.Value.ValueKind is JsonValueKind.Object)
                {
                    Walk(property.Value, path + "/", isPrint);
                    continue;
                }

                foreach (var variant in property.Value.EnumerateArray().Skip(1))
                {
                    var url = variant[0].ValueKind is JsonValueKind.String ? variant[0].GetString()!.TrimStart('/') : path;
                    var references = variant[1].EnumerateArray().Select(r => new WptReference(r[0].GetString()!.TrimStart('/'), r[1].GetString() == "==")).ToList();
                    var extras = variant[2];
                    var fuzzy = new List<(WptFuzzyKey?, WptFuzzy)>();
                    if (extras.TryGetProperty("fuzzy", out var fuzzyElement))
                    {
                        foreach (var item in fuzzyElement.EnumerateArray())
                        {
                            fuzzy.Add((ReadFuzzyKey(item[0]), ReadFuzzy(item[1])));
                        }
                    }

                    var longTimeout = extras.TryGetProperty("timeout", out var timeout) && timeout.GetString() == "long";
                    Dictionary<string, IReadOnlyList<(int?, int?)>>? pageRanges = null;
                    if (extras.TryGetProperty("page_ranges", out var pageRangesElement))
                    {
                        pageRanges = new(StringComparer.Ordinal);
                        foreach (var page in pageRangesElement.EnumerateObject())
                        {
                            // [[1], [2, 3], [null, 4], [5, null]]
                            pageRanges[page.Name.TrimStart('/')] = [.. page.Value.EnumerateArray().Select(range => (ReadPage(range[0]), range.GetArrayLength() > 1 ? ReadPage(range[1]) : ReadPage(range[0])))];
                        }
                    }

                    result[url] = new WptReftestNode(url, references, fuzzy, longTimeout) { IsPrint = isPrint, PageRanges = pageRanges };
                }
            }
        }

        // null (any comparison), a reference URL, or [test URL, reference URL, relation]
        static WptFuzzyKey? ReadFuzzyKey(JsonElement key) => key.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => new WptFuzzyKey(null, key.GetString()!.TrimStart('/'), null),
            _ => new WptFuzzyKey(key[0].GetString()!.TrimStart('/'), key[1].GetString()!.TrimStart('/'), key.GetArrayLength() > 2 ? key[2].GetString() : null),
        };

        // A few tests allow any number of pixels with a large value (e.g. 4294967295)
        static WptFuzzy ReadFuzzy(JsonElement value) => new(ReadInt(value[0][0]), ReadInt(value[0][1]), ReadInt(value[1][0]), ReadInt(value[1][1]));

        static int ReadInt(JsonElement value) => (int)Math.Min(value.GetInt64(), int.MaxValue);

        static int? ReadPage(JsonElement value) => value.ValueKind is JsonValueKind.Number ? value.GetInt32() : null;
    }
}

internal static class WptReftestDiscovery
{
    /// <summary>
    /// Gets whether the reftests are run: <c>WPT_REFTESTS=0</c> skips them (e.g. to run the testharness.js tests faster while
    /// working on something else than the rendering).
    /// </summary>
    public static bool IsEnabled => Environment.GetEnvironmentVariable("WPT_REFTESTS") is not ("0" or "false");

    /// <summary>
    /// Discovers the reftests of the fetched directories under a directory: the reftests of the manifest whose file exists
    /// (the directories of <c>tests/Meziantou.Framework.WebPlatform.SpecData/sources.json</c>), except the ones of <c>wpt-skip.json</c> and of the excluded
    /// directories.
    /// </summary>
    public static IEnumerable<WptReftest> Discover(string directory, params string[] excludedDirectories)
    {
        var prefix = directory.Length == 0 ? "" : directory + "/";
        foreach (var url in WptReftestManifest.All.Keys.Order(StringComparer.Ordinal))
        {
            var relativePath = url.Split('?', '#')[0];
            if (!relativePath.StartsWith(prefix, StringComparison.Ordinal) || excludedDirectories.Any(d => relativePath.StartsWith(d + "/", StringComparison.Ordinal)))
                continue;

            if (WptTestDiscovery.IsSkipped(relativePath) || !File.Exists(Path.Combine(WptPaths.Wpt, relativePath.Replace('/', Path.DirectorySeparatorChar))))
                continue;

            yield return new WptReftest(relativePath, url, WptReftestManifest.All[url].IsPrint);
        }
    }
}
