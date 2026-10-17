using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>The result of a reftest: <c>PASS</c>, <c>FAIL</c>, <c>ERROR</c> (a page cannot be rendered) or <c>TIMEOUT</c> (a page is never ready for the screenshot).</summary>
internal sealed record WptReftestResult(string Status, string? Message, IReadOnlyList<string> Log);

/// <summary>
/// Runs a reftest as wptrunner does (https://web-platform-tests.org/writing-tests/reftests.html): the test and its references
/// are rendered at 800x600 (the default viewport of the reftests) by the Imaging backend, and their pixels are compared with
/// the fuzziness of the test. The test passes when it matches one of its match references (if any) and differs from all its
/// mismatch references, a reference that has its own references (a chain) passing only when they pass too.
/// </summary>
internal static class WptReftestRunner
{
    /// <summary>The maximum depth of the chains of references (the comparisons already made are skipped, so the cycles end).</summary>
    private const int MaxDepth = 8;

    /// <summary>The number of reftests that must use a reference for its rendering to be shared by the tests (e.g. the green squares of css/reference).</summary>
    private const int SharedReferenceUseCount = 16;

    /// <summary>
    /// The backend that renders the pages of all the reftests: it is thread-safe, and its caches (the outlines of the glyphs of the font
    /// files) live as long as the font files of the documents.
    /// </summary>
    internal static readonly ImagingRenderingBackend Backend = new();

    /// <summary>The references used by many reftests (<see cref="SharedReferenceUseCount"/>), whose rendering is computed once and shared by the tests.</summary>
    private static readonly Lazy<HashSet<string>> SharedReferences = new(() =>
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in WptReftestManifest.All.Values)
        {
            foreach (var reference in node.References)
            {
                counts[reference.Url] = counts.GetValueOrDefault(reference.Url) + 1;
            }
        }

        return [.. counts.Where(p => p.Value >= SharedReferenceUseCount).Select(p => p.Key)];
    });

    /// <summary>The renderings of the shared references. The bitmaps are never disposed (~140 references of 1.9 MB).</summary>
    private static readonly ConcurrentDictionary<string, Lazy<WptPageRendering>> SharedRenderings = new(StringComparer.Ordinal);

    /// <summary>
    /// The directory where the images of the failing comparisons are saved (<c>&lt;test&gt;.png</c>, <c>&lt;reference&gt;.png</c>
    /// and a <c>diff.png</c> where the different pixels are red): <c>WPT_REFTEST_IMAGES</c>, or else <c>TestResults/reftests</c>
    /// for the tests expected to pass (the regressions).
    /// </summary>
    private static string? ImagesDirectory => Environment.GetEnvironmentVariable("WPT_REFTEST_IMAGES") is { Length: > 0 } directory ? directory : null;

    public static WptReftestResult Run(WptReftest test, bool expectedToPass)
    {
        var imagesDirectory = ImagesDirectory ?? (expectedToPass ? Path.Combine(WptPaths.Root, "TestResults", "reftests") : null);
        using var context = new RunContext(test, imagesDirectory);
        var root = WptReftestManifest.GetNode(test.Url);
        context.Root = root;
        var passes = context.Evaluate(root, depth: 0);

        // A page that cannot be rendered gives its status to the reftest (ERROR or TIMEOUT)
        if (context.PageError is { } error)
            return new WptReftestResult(error.Status, error.Message, context.Log);

        return new WptReftestResult(passes ? "PASS" : "FAIL", context.Message, context.Log);
    }

    /// <summary>The renderings of the pages of a reftest, rendered once each and disposed at the end of the test.</summary>
    private sealed class RunContext(WptReftest test, string? imagesDirectory) : IDisposable
    {
        private readonly Dictionary<string, WptPageRendering> _renderings = new(StringComparer.Ordinal);
        private readonly StringBuilder _message = new();
        private readonly HashSet<(string Relation, (string, string) Urls)> _comparisons = [];
        private bool _imagesSaved;

        public List<string> Log { get; } = [];

        /// <summary>The node of the reftest: its page ranges apply to the pages of the tree (print reftests).</summary>
        public WptReftestNode? Root { get; set; }

        /// <summary>Whether the pages are printed (a print reftest): their pages are compared.</summary>
        private bool IsPrint => test.IsPrint;

        public string? Message => _message.Length == 0 ? null : _message.ToString().TrimEnd();

        /// <summary>Gets the status and the message of the first page that cannot be rendered, or null.</summary>
        public (string Status, string Message)? PageError { get; private set; }

        /// <summary>Returns whether a page passes: it matches one of its match references and differs from all its mismatch references.</summary>
        public bool Evaluate(WptReftestNode node, int depth)
        {
            if (PageError is not null)
                return true;

            // A comparison already made in the tree is not made again (as wptrunner, which stops at the cycles of references, e.g.
            // A == B and B == A): a page whose comparisons were all made is a leaf
            var references = node.References.Where(reference => _comparisons.Add((reference.Relation, string.CompareOrdinal(node.Url, reference.Url) < 0 ? (node.Url, reference.Url) : (reference.Url, node.Url)))).ToList();
            if (references.Count == 0)
                return true;

            if (depth >= MaxDepth)
            {
                _message.AppendLine("The chain of references is too deep: " + node.Url);
                return false;
            }

            var hasMatch = false;
            var matched = false;
            foreach (var reference in references)
            {
                if (reference.IsMatch)
                {
                    hasMatch = true;
                    if (matched)
                        continue;

                    matched = Compare(node, reference) && Evaluate(WptReftestManifest.GetNode(reference.Url), depth + 1);
                }
                else if (!Compare(node, reference) || !Evaluate(WptReftestManifest.GetNode(reference.Url), depth + 1))
                {
                    return false;
                }
            }

            return !hasMatch || matched;
        }

        /// <summary>Compares a page with a reference (as <c>check_pass</c> of wptrunner). Returns whether the relation holds.</summary>
        private bool Compare(WptReftestNode node, WptReference reference)
        {
            if (GetRendering(node.Url, node.LongTimeout) is not { } actual || GetRendering(reference.Url, node.LongTimeout) is not { } expected)
                return false;

            var fuzzy = node.GetFuzzy(reference);
            var actualPages = GetComparedPages(node.Url, actual);
            var expectedPages = GetComparedPages(reference.Url, expected);

            // https://web-platform-tests.org/writing-tests/print-reftests.html: the pages are compared one by one (as check_pass of
            // wptrunner, a different number of pages is a difference); the fuzziness applies to each page
            if (actualPages.Count != expectedPages.Count)
            {
                if (!reference.IsMatch)
                    return true;

                _message.AppendLine(CultureInfo.InvariantCulture, $"/{node.Url} {reference.Relation} /{reference.Url}: {actualPages.Count} pages, expected {expectedPages.Count}");
                return false;
            }

            for (var page = 0; page < actualPages.Count; page++)
            {
                var (maxDifference, pixels) = GetDifferences(actualPages[page], expectedPages[page]);
                var equal = fuzzy.IsExact
                    ? pixels == 0
                    : (pixels == 0 && fuzzy.MinPixels == 0)
                        || (maxDifference == 0 && fuzzy.MinDifference == 0)
                        || (fuzzy.MinDifference <= maxDifference && maxDifference <= fuzzy.MaxDifference && fuzzy.MinPixels <= pixels && pixels <= fuzzy.MaxPixels);
                if (equal)
                    continue;

                if (!reference.IsMatch)
                    return true;

                _message.Append(CultureInfo.InvariantCulture, $"/{node.Url} {reference.Relation} /{reference.Url}: ");
                _message.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{(IsPrint ? "page " + (page + 1) + ": " : "")}{pixels} different pixels, maximum difference {maxDifference}{(fuzzy.IsExact ? "" : " (allowed: " + fuzzy + ")")}{(IsSubpixelDifference(actualPages[page], expectedPages[page]) ? ", only on the edges (subpixel offsets, antialiasing or offsets of 1 pixel)" : "")}"));
                if (imagesDirectory is not null && !_imagesSaved)
                {
                    _imagesSaved = true;
                    SaveImages(node.Url, reference.Url, actualPages[page], expectedPages[page]);
                }

                return false;
            }

            if (reference.IsMatch)
                return true;

            _message.Append(CultureInfo.InvariantCulture, $"/{node.Url} {reference.Relation} /{reference.Url}: ");
            _message.AppendLine("the renderings are identical");
            return false;
        }

        /// <summary>Returns the images compared for a page: its screenshot, or its printed pages that the reftest compares (<c>reftest-pages</c>).</summary>
        private List<WptScreenshot> GetComparedPages(string url, WptPageRendering rendering)
        {
            if (rendering.Pages is not { } pages)
                return [rendering.Screenshot!];

            var indexes = Root?.GetComparedPages(url, pages.Count) ?? [.. Enumerable.Range(0, pages.Count)];
            return [.. indexes.Where(index => index < pages.Count).Select(index => pages[index])];
        }

        private WptPageRendering? GetRendering(string url, bool longTimeout)
        {
            if (!_renderings.TryGetValue(url, out var rendering))
            {
                var print = IsPrint;
                rendering = SharedReferences.Value.Contains(url)
                    ? SharedRenderings.GetOrAdd((print ? "print:" : "") + url, _ => new Lazy<WptPageRendering>(() => RenderPage(url, longTimeout, print))).Value
                    : RenderPage(url, longTimeout, print);
                _renderings.Add(url, rendering);
                foreach (var line in rendering.Log)
                {
                    Log.Add("/" + url + ": " + line);
                }
            }

            if (rendering.Screenshot is null && rendering.Pages is null)
            {
                PageError ??= (rendering.Status is "TIMEOUT" ? "TIMEOUT" : "ERROR", $"/{url}: {rendering.Status} {rendering.Message}");
                return null;
            }

            return rendering;
        }

        private static WptPageRendering RenderPage(string url, bool longTimeout, bool print)
        {
            // A few tests are compared with about:blank: the white canvas of an empty document (a blank page when it is printed)
#pragma warning disable CA2000 // The screenshot is owned by the rendering, disposed at the end of the test
            if (url == "about:blank")
                return print ? new WptPageRendering(null, "OK", null, []) { Pages = [CreateBlankScreenshot(WptTestRunner.PrintPageWidth, WptTestRunner.PrintPageHeight)] } : new WptPageRendering(CreateBlankScreenshot(800, 600), "OK", null, []);
#pragma warning restore CA2000

            var queryIndex = url.IndexOfAny(['?', '#']);
            var path = queryIndex < 0 ? url : url[..queryIndex];
            var extension = Path.GetExtension(path);
            var kind = extension is ".xht" or ".xhtml" or ".svg" or ".xml" ? WptTestKind.Xml : WptTestKind.Html;
            if (!File.Exists(Path.Combine(WptPaths.Wpt, path.Replace('/', Path.DirectorySeparatorChar))))
                return new WptPageRendering(null, "ERROR", "The file is not fetched (tests/Meziantou.Framework.WebPlatform.SpecData/sources.json)", []);

            try
            {
                return WptTestRunner.Render(new WptTest(path, kind, queryIndex < 0 ? "" : url[queryIndex..]), Backend, longTimeout, print);
            }
            catch (Exception ex)
            {
                return new WptPageRendering(null, "ERROR", ex.GetType().Name + ": " + ex.Message, []);
            }
        }

        private static WptScreenshot CreateBlankScreenshot(int width, int height)
        {
            var blank = new WptScreenshot(width, height);
            blank.Pixels.Fill(0xFF);
            return blank;
        }

        private void SaveImages(string testUrl, string referenceUrl, WptScreenshot actual, WptScreenshot expected)
        {
            var directory = Path.Combine(imagesDirectory!, test.Id.Replace('/', '_').Replace('?', '_').Replace('&', '_').Replace(':', '_'));
            Directory.CreateDirectory(directory);
            Save(actual, Path.Combine(directory, "test-" + Path.GetFileName(testUrl.Split('?')[0]) + ".png"));
            Save(expected, Path.Combine(directory, "reference-" + Path.GetFileName(referenceUrl.Split('?')[0]) + ".png"));
            using var diff = CreateDiffImage(actual, expected);
            Save(diff, Path.Combine(directory, "diff.png"));
            Log.Add("Images of the comparison: " + directory);

            static void Save(WptScreenshot screenshot, string path) => File.WriteAllBytes(path, PngEncoder.Encode(screenshot.Width, screenshot.Height, screenshot.Pixels));
        }

        public void Dispose()
        {
            foreach (var (url, rendering) in _renderings)
            {
                if (!SharedReferences.Value.Contains(url))
                {
                    rendering.Screenshot?.Dispose();
                    foreach (var page in rendering.Pages ?? [])
                    {
                        page.Dispose();
                    }
                }
            }
        }
    }

    /// <summary>
    /// Returns the maximum difference of a color channel (red, green or blue) and the number of different pixels of two
    /// screenshots (as <c>get_differences</c> of wptrunner, which compares the RGB images). The screenshots are opaque.
    /// </summary>
    internal static (int MaxDifference, int Pixels) GetDifferences(WptScreenshot actual, WptScreenshot expected)
    {
        if (actual.Width != expected.Width || actual.Height != expected.Height)
            return (255, Math.Max(actual.Width * actual.Height, expected.Width * expected.Height));

        var actualBytes = actual.Pixels;
        var expectedBytes = expected.Pixels;
        if (actualBytes.SequenceEqual(expectedBytes))
            return (0, 0);

        // RGBA: the alpha is the most significant byte of a little-endian pixel
        var actualPixels = unsafe(MemoryMarshal.Cast<byte, uint>(actualBytes));
        var expectedPixels = unsafe(MemoryMarshal.Cast<byte, uint>(expectedBytes));
        var maxDifference = 0;
        var pixels = 0;
        for (var i = 0; i < actualPixels.Length; i++)
        {
            var a = actualPixels[i];
            var e = expectedPixels[i];
            if (((a ^ e) & 0x00FFFFFF) == 0)
                continue;

            pixels++;
            var difference = Math.Max(Math.Abs((int)(a & 0xFF) - (int)(e & 0xFF)), Math.Max(Math.Abs((int)((a >> 8) & 0xFF) - (int)((e >> 8) & 0xFF)), Math.Abs((int)((a >> 16) & 0xFF) - (int)((e >> 16) & 0xFF))));
            maxDifference = Math.Max(maxDifference, difference);
        }

        return (maxDifference, pixels);
    }

    /// <summary>
    /// Returns whether the differences of two screenshots are only on the edges of what they paint, as when the same boxes and
    /// glyphs are painted at subpixel offsets (antialiased edges, seams between adjacent boxes) or 1 pixel apart: each different pixel is
    /// next to an edge in one of the images (a pixel of its 3x3 neighborhood has another color), and its color in each image is
    /// close to the range of the colors of its 3x3 neighborhood in the other image (the antialiasing of two adjacent edges blends
    /// their colors with the background). A missing or extra box or glyph, or another color, is a difference inside a painted area of one of the images.
    /// </summary>
    internal static bool IsSubpixelDifference(WptScreenshot actual, WptScreenshot expected)
    {
        if (actual.Width != expected.Width || actual.Height != expected.Height)
            return false;

        var width = actual.Width;
        var height = actual.Height;
        var actualPixels = unsafe(MemoryMarshal.Cast<byte, uint>(actual.Pixels));
        var expectedPixels = unsafe(MemoryMarshal.Cast<byte, uint>(expected.Pixels));
        for (var i = 0; i < actualPixels.Length; i++)
        {
            if (((actualPixels[i] ^ expectedPixels[i]) & 0x00FFFFFF) == 0)
                continue;

            int x = i % width, y = i / width;
            if ((!IsNearEdge(actualPixels, x, y, width, height) && !IsNearEdge(expectedPixels, x, y, width, height))
                || !IsInNeighborhoodRange(actualPixels[i], expectedPixels, x, y, width, height) || !IsInNeighborhoodRange(expectedPixels[i], actualPixels, x, y, width, height))
            {
                return false;
            }
        }

        return true;

        static bool IsNearEdge(ReadOnlySpan<uint> pixels, int x, int y, int width, int height)
        {
            var color = pixels[(y * width) + x] & 0x00FFFFFF;
            for (var dy = Math.Max(0, y - 1); dy <= Math.Min(height - 1, y + 1); dy++)
            {
                for (var dx = Math.Max(0, x - 1); dx <= Math.Min(width - 1, x + 1); dx++)
                {
                    if ((pixels[(dy * width) + dx] & 0x00FFFFFF) != color)
                        return true;
                }
            }

            return false;
        }

        static bool IsInNeighborhoodRange(uint pixel, ReadOnlySpan<uint> other, int x, int y, int width, int height)
        {
            const int Tolerance = 64;
            for (var shift = 0; shift < 24; shift += 8)
            {
                var value = (int)((pixel >> shift) & 0xFF);
                int min = 255, max = 0;
                for (var dy = Math.Max(0, y - 1); dy <= Math.Min(height - 1, y + 1); dy++)
                {
                    for (var dx = Math.Max(0, x - 1); dx <= Math.Min(width - 1, x + 1); dx++)
                    {
                        var component = (int)((other[(dy * width) + dx] >> shift) & 0xFF);
                        min = Math.Min(min, component);
                        max = Math.Max(max, component);
                    }
                }

                if (value < min - Tolerance || value > max + Tolerance)
                    return false;
            }

            return true;
        }
    }

    /// <summary>An image of the differences: the expected image, faded, with the different pixels in red.</summary>
    private static WptScreenshot CreateDiffImage(WptScreenshot actual, WptScreenshot expected)
    {
        var diff = new WptScreenshot(expected.Width, expected.Height);
        var actualPixels = unsafe(MemoryMarshal.Cast<byte, uint>(actual.Pixels));
        var expectedPixels = unsafe(MemoryMarshal.Cast<byte, uint>(expected.Pixels));
        var diffPixels = unsafe(MemoryMarshal.Cast<byte, uint>(diff.Pixels));
        var sameSize = actual.Width == expected.Width && actual.Height == expected.Height;
        for (var i = 0; i < diffPixels.Length; i++)
        {
            var e = expectedPixels[i];
            if (sameSize && ((actualPixels[i] ^ e) & 0x00FFFFFF) == 0)
            {
                // 3/4 of white + 1/4 of the color, per channel
                diffPixels[i] = 0xFF000000 | (((e & 0x00FCFCFC) >> 2) + 0x00BFBFBF);
            }
            else
            {
                // Red (RGBA, little-endian)
                diffPixels[i] = 0xFF0000FF;
            }
        }

        return diff;
    }
}
