using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Playwright;

namespace ChromiumDiff;

internal sealed class LayoutOptions
{
    public string? Filter { get; init; }
    public string? ReportPath { get; init; }
    public string? JsonReportPath { get; init; }
    public List<string> CorpusDirectories { get; } = [];
    public bool Pages { get; init; }
    public bool Record { get; init; }
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 800;
    public double Tolerance { get; init; } = 1;
    public int MaxDifferences { get; init; } = 10;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
    public string? Inspect { get; init; }
    public IReadOnlyList<string> InspectedProperties { get; init; } = DefaultInspectedProperties;
    public static IReadOnlyList<string> DefaultInspectedProperties { get; } = ["display", "position", "float", "box-sizing", "width", "height", "min-width", "max-width", "margin", "padding", "border-width", "font-size", "line-height", "overflow", "flex", "scrollbar-width", "scrollbar-gutter"];
}

/// <summary>An accepted difference of the layout comparison (layout-known-differences.json).</summary>
internal sealed record LayoutKnownDifference(string Selector, string Reason, string? Page = null);

/// <summary>The border box of an element in document coordinates.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct Box(double X, double Y, double Width, double Height)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"x={X:0.##} y={Y:0.##} w={Width:0.##} h={Height:0.##}");
}

/// <summary>
/// Compares the layout of pages in Chromium and in the library: each page is loaded with the same subresources (recorded
/// once from the network, see <see cref="ResourceCache"/>), the same viewport, without scripts, with the same font for all
/// the text (a style sheet injected in both forces the bundled Arimo of the library, loaded as a web font), and without
/// animations and transitions. The border boxes of the elements (getBoundingClientRect) are compared with a tolerance, and
/// the differences that are not explained by another difference are reported (see <see cref="FindDifferences"/>).
/// </summary>
internal static partial class LayoutComparison
{
    public static async Task<int> RunAsync(string root, LayoutOptions options)
    {
        var knownDifferencesPath = Path.Combine(root, "tools", "Meziantou.Framework.WebPlatform.ChromiumDiff", "layout-known-differences.json");
        var knownDifferences = JsonSerializer.Deserialize<List<LayoutKnownDifference>>(File.ReadAllText(knownDifferencesPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }) ?? [];
        var styleSheet = ComparisonCommon.CreateStyleSheet(root);

        // The pages of pages.txt with their recorded subresources, and the files of the --corpus directories without subresources
        var inputs = new List<(string Id, string Url, byte[] Html, Page? Page)>();
        if (options.Pages)
        {
            foreach (var page in Corpus.GetPages(root))
                inputs.Add((page.Url, page.Url, File.ReadAllBytes(page.Path), page));
        }

        foreach (var directory in options.CorpusDirectories)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.htm*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
                inputs.Add((relative, "https://chromium-diff.test/" + relative, File.ReadAllBytes(file), null));
            }
        }

        inputs = [.. inputs.Where(input => options.Filter is null || input.Id.Contains(options.Filter, StringComparison.Ordinal))];
        Console.WriteLine($"{inputs.Count} page(s)");

        using var playwright = await Playwright.CreateAsync();
        // The classic scrollbars of 15px are the ones of the library
        await using var browser = await ComparisonCommon.LaunchChromiumAsync(playwright, classicScrollbars: true);
        Console.WriteLine($"Chromium {browser.Version}");
        var measureScript = ComparisonCommon.ReadResource("layout.js");
        var stylesScript = ComparisonCommon.ReadResource("styles.js");
        var inspectScript = ComparisonCommon.ReadResource("inspect.js");

        // The reports are written as the pages are compared
        using var reportWriter = options.ReportPath is null ? null : new StreamWriter(options.ReportPath) { AutoFlush = true };
        using var jsonWriter = options.JsonReportPath is null ? null : new StreamWriter(options.JsonReportPath) { AutoFlush = true };
        var usedKnownDifferences = new HashSet<LayoutKnownDifference>();
        var unexpectedCount = 0;
        var knownCount = 0;
        var failedPages = 0;
        var stopwatch = Stopwatch.StartNew();
        foreach (var (id, url, html, page) in inputs)
        {
            var pageStopwatch = Stopwatch.StartNew();
            await using var context = await browser.NewContextAsync(CreateContextOptions(options));
            ResourceCache cache;
            if (page is null)
            {
                cache = ResourceCache.Empty;
            }
            else
            {
                var directory = Path.Combine(root, "artifacts", "chromium-diff", "resources", page.Name);
                cache = (options.Record ? null : ResourceCache.Load(directory))
                    ?? await ResourceCache.RecordAsync(context, url, html, directory, p => ComparisonCommon.PrepareChromiumAsync(p, styleSheet));
            }

            Dictionary<string, Box> chromium;
            IPage chromiumPage;
            try
            {
                chromiumPage = await context.NewPageAsync();
                await cache.RouteAsync(chromiumPage, url, html);
                await chromiumPage.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 120_000 });
                await ComparisonCommon.PrepareChromiumAsync(chromiumPage, styleSheet);
                chromium = await MeasureChromiumAsync(chromiumPage, measureScript);
            }
            catch (PlaywrightException ex)
            {
                Console.Error.WriteLine($"error: cannot load {id} in Chromium: {ex.Message.Split('\n')[0]}");
                failedPages++;
                continue;
            }

            Dictionary<string, Box> library;
            Dictionary<string, Element> elements;
            BrowsingContext? browsingContext = null;
            try
            {
                (browsingContext, library, elements) = await MeasureLibraryAsync(url, html, cache, styleSheet, options);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Report("ERROR " + id + ": the library threw " + ex.GetType().Name + ": " + ex.Message + "\n");
                Console.Error.WriteLine($"error: the library cannot lay out {id}: {ex}");
                browsingContext?.Dispose();
                failedPages++;
                continue;
            }

            using (browsingContext)
            {
                var differences = FindDifferences(chromium, library, options.Tolerance, key => elements[key].LocalName);
                var missing = chromium.Keys.Count(key => !library.ContainsKey(key)) + library.Keys.Count(key => !chromium.ContainsKey(key));
                var different = chromium.Keys.Count(key => library.TryGetValue(key, out var box) && !AreClose(chromium[key], box, options.Tolerance));

                // The computed styles of the reported elements in both
                var chromiumStyles = await chromiumPage.EvaluateAsync<string[][]>(stylesScript, differences.Select(d => d.Key).ToArray());
                var pageUnexpected = 0;
                var pageReport = new StringBuilder();
                for (var i = 0; i < differences.Count; i++)
                {
                    var difference = differences[i];
                    var element = elements[difference.Key];
                    // A difference that propagates from the contents is explained by the difference of its first different descendant
                    var known = GetKnownDifference(element)
                        ?? (difference.FirstDifferentDescendant is { } differentDescendant ? GetKnownDifference(elements[differentDescendant]) : null);
                    var libraryStyle = GetLibraryStyle(element);
                    var path = GetCssPath(element);
                    if (known is not null)
                    {
                        if (!AutomaticKnownDifferences.Contains(known))
                            usedKnownDifferences.Add(known);

                        knownCount++;
                    }
                    else
                    {
                        unexpectedCount++;
                        pageUnexpected++;
                        if (pageUnexpected <= options.MaxDifferences)
                        {
                            pageReport.Append("  DIFFERENCE ").Append(path).Append(" [").Append(string.Join(", ", difference.Kinds)).Append("]\n");
                            pageReport.Append("    chromium: ").Append(chromium[difference.Key]).Append("  ").Append(FormatStyle(chromiumStyles[i])).Append('\n');
                            pageReport.Append("    library:  ").Append(library[difference.Key]).Append("  ").Append(FormatStyle(libraryStyle)).Append('\n');
                            if (difference.FirstDifferentDescendant is { } descendant)
                            {
                                pageReport.Append("    first different descendant: ").Append(GetCssPath(elements[descendant])).Append('\n');
                                pageReport.Append("      chromium: ").Append(chromium[descendant]).Append('\n');
                                pageReport.Append("      library:  ").Append(library[descendant]).Append('\n');
                            }
                        }
                    }

                    jsonWriter?.WriteLine(JsonSerializer.Serialize(new
                    {
                        page = id,
                        path,
                        tag = element.LocalName,
                        kinds = difference.Kinds,
                        chromium = chromium[difference.Key],
                        library = library[difference.Key],
                        chromiumStyle = chromiumStyles[i],
                        libraryStyle,
                        descendant = difference.FirstDifferentDescendant is { } d ? GetCssPath(elements[d]) : null,
                        known = known?.Reason,
                    }));
                }

                if (options.Inspect is not null)
                    await InspectAsync(chromiumPage, inspectScript, elements, options);

                var summary = $"{chromium.Count} elements, {different} different, {differences.Count} difference(s) not explained by another one ({differences.Count - pageUnexpected} known), {missing} not in both trees, {pageStopwatch.Elapsed.TotalSeconds:F1} s";
                if (pageUnexpected == 0)
                {
                    Console.WriteLine($"{id}: {summary}");
                }
                else
                {
                    if (pageUnexpected > options.MaxDifferences)
                        pageReport.Append("  ... ").Append(pageUnexpected - options.MaxDifferences).Append(" more\n");

                    Report("UNEXPECTED DIFFERENCES " + id + ": " + summary + "\n" + pageReport);
                }
            }
        }

        // A known difference that matches no difference must be removed from the list (only when all the pages are compared)
        var stale = options.Filter is null && options.Pages
            ? knownDifferences.Where(k => !usedKnownDifferences.Contains(k) && (k.Page is null || inputs.Any(input => input.Id == k.Page))).ToList()
            : [];
        foreach (var known in stale)
        {
            Report("STALE KNOWN DIFFERENCE " + (known.Page ?? "*") + " " + known.Selector + " (no difference matches it: remove it from layout-known-differences.json)\n");
        }

        Console.WriteLine($"Compared {inputs.Count - failedPages} page(s) in {stopwatch.Elapsed.TotalSeconds:F1} s: {unexpectedCount + knownCount} difference(s), {knownCount} known, {unexpectedCount} unexpected; {stale.Count} stale known difference(s); {failedPages} page(s) not compared");
        return unexpectedCount == 0 && stale.Count == 0 && failedPages == 0 ? 0 : 1;

        LayoutKnownDifference? GetKnownDifference(Element element)
            => knownDifferences.FirstOrDefault(k => (k.Page is null || k.Page == element.OwnerDocument!.Url) && element.Closest(k.Selector) is not null)
                ?? (HasCharactersWithoutGlyph(element) ? FontFallback : null)
                ?? (HasSolidusBetweenCharacters(element) ? SolidusBreak : null)
                ?? (HasLetterSpacing(element) ? LetterSpacingAtLineEnd : null)
                ?? (IsInlineBoxWithBlockLevelChild(element) ? BlockInInline : null);

        void Report(string text)
        {
            Console.Write(text);
            reportWriter?.Write(text);
        }
    }

    /// <summary>
    /// The difference of an element whose text has characters that the injected font does not have: the fonts of the
    /// fallback are not the same in Chromium (the fonts of the system) and in the library (its bundled fonts).
    /// </summary>
    private static readonly LayoutKnownDifference FontFallback = new("*", "font fallback: the text has characters that the injected font does not have, rendered with other fonts in Chromium (the fonts of the system) and in the library (its bundled fonts)");

    /// <summary>The characters of the subset of Arimo bundled with the library (eng/webplatform/update-fonts.sh).</summary>
    private static readonly (int First, int Last)[] InjectedFontCharacters =
    [
        (0x0000, 0x017F), (0x0192, 0x0192), (0x0218, 0x021B), (0x02C6, 0x02C7), (0x02C9, 0x02C9), (0x02D8, 0x02DD), (0x0370, 0x03FF),
        (0x0400, 0x04FF), (0x2000, 0x206F), (0x20AC, 0x20AC), (0x2122, 0x2122), (0x2190, 0x2195), (0x2212, 0x2212), (0x2215, 0x2215),
        (0xFEFF, 0xFEFF), (0xFFFD, 0xFFFD),
    ];

    /// <summary>
    /// The difference of an element whose text has a solidus between two characters (e.g. a URL): Chromium does not break a
    /// line after a solidus followed by a letter or a digit (its line breaking table of the ASCII characters, as WebKit),
    /// UAX #14 and Firefox do, as the library.
    /// </summary>
    private static readonly LayoutKnownDifference SolidusBreak = new("*", "line breaking after a solidus: the text has a solidus between two characters (e.g. a URL), after which Chromium and WebKit do not break a line before a letter or a digit, where UAX #14, Firefox and the library do");

    /// <summary>
    /// Whether the text of the element or the generated content of its pseudo-elements and of the ones of its descendants (when
    /// they are not too long to be the cause of a difference) have characters that the injected font does not have.
    /// </summary>
    private static bool HasCharactersWithoutGlyph(Element element)
    {
        var text = element.TextContent ?? "";
        if (text.Length > 2000)
            return false;

        if (HasCharacterWithoutGlyph(text))
            return true;

        // The generated content (e.g. the icons of an icon font)
        var window = element.OwnerDocument!.DefaultView!;
        var count = 0;
        foreach (var current in element.GetElementsByTagName("*").Prepend(element))
        {
            if (++count > 100)
                break;

            foreach (var pseudoElement in (string[])["::before", "::after", "::marker"])
            {
                var content = window.GetComputedStyle(current, pseudoElement).GetPropertyValue("content");
                if (content.Length > 0 && content is not ("none" or "normal") && HasCharacterWithoutGlyph(content))
                    return true;
            }
        }

        return false;

        static bool HasCharacterWithoutGlyph(string text)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (!InjectedFontCharacters.Any(range => rune.Value >= range.First && rune.Value <= range.Last))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The difference of an element with a letter-spacing (or with a descendant that has one): the browsers add the spacing
    /// after the last character of a line, which CSS Text 4 and the library do not.
    /// </summary>
    private static readonly LayoutKnownDifference LetterSpacingAtLineEnd = new("*", "letter-spacing at the end of the lines: the element or a descendant has a letter-spacing, which the browsers add after the last character of a line, unlike CSS Text 4 (https://drafts.csswg.org/css-text-4/#letter-spacing-property) and the library");

    /// <summary>
    /// The difference of an inline box with a block-level child: its rectangles around the child are the anonymous block box in
    /// Chromium and WebKit (as wide as the containing block), the border box of the child in Firefox and in the library.
    /// </summary>
    private static readonly LayoutKnownDifference BlockInInline = new("*", "block-in-inline: the rectangle of an inline box around a block-level child is the anonymous block (as wide as the containing block) in Chromium and WebKit, the border box of the child in Firefox and in the library");

    /// <summary>The known differences that are recognized by the element rather than listed in layout-known-differences.json.</summary>
    private static readonly HashSet<LayoutKnownDifference> AutomaticKnownDifferences = [FontFallback, SolidusBreak, LetterSpacingAtLineEnd, BlockInInline];

    /// <summary>Whether the element is an inline box with a block-level child in flow.</summary>
    private static bool IsInlineBoxWithBlockLevelChild(Element element)
    {
        var window = element.OwnerDocument!.DefaultView!;
        if (window.GetComputedStyle(element).GetPropertyValue("display") != "inline")
            return false;

        for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            var style = window.GetComputedStyle(child);
            if (style.GetPropertyValue("display") is "block" or "flex" or "grid" or "table" or "list-item" or "flow-root"
                && style.GetPropertyValue("float") == "none" && style.GetPropertyValue("position") is "static" or "relative" or "sticky")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the element or one of its descendants (when they are not too many to be the cause of a difference) has a letter-spacing.</summary>
    private static bool HasLetterSpacing(Element element)
    {
        var window = element.OwnerDocument!.DefaultView!;
        var count = 0;
        foreach (var current in element.GetElementsByTagName("*").Prepend(element))
        {
            if (++count > 100)
                return false;

            if (window.GetComputedStyle(current).GetPropertyValue("letter-spacing") is not ("normal" or "0px" or ""))
                return true;
        }

        return false;
    }

    /// <summary>Whether the text of the element (when it is not too long to be the cause of a difference) has a solidus between two letters or digits.</summary>
    private static bool HasSolidusBetweenCharacters(Element element)
    {
        var text = element.TextContent ?? "";
        if (text.Length > 2000)
            return false;

        for (var i = text.IndexOf('/', StringComparison.Ordinal); i >= 0; i = text.IndexOf('/', i + 1, StringComparison.Ordinal))
        {
            if (i > 0 && i + 1 < text.Length && !char.IsWhiteSpace(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]))
                return true;
        }

        return false;
    }

    /// <summary>Prints the computed values of the elements that match the selector of --inspect in both, and their client and scroll sizes.</summary>
    private static async Task InspectAsync(IPage page, string script, Dictionary<string, Element> elements, LayoutOptions options)
    {
        var inspected = elements.Where(pair => pair.Value.Matches(options.Inspect!)).Take(20).ToList();
        var chromium = await page.EvaluateAsync<string[][]>(script, new object[] { inspected.Select(pair => pair.Key).ToArray(), options.InspectedProperties.ToArray() });
        for (var i = 0; i < inspected.Count; i++)
        {
            var element = inspected[i].Value;
            var rect = element.GetBoundingClientRect();
            Console.WriteLine($"INSPECT {GetCssPath(element)}");
            var markup = element.OuterHtml;
            Console.WriteLine($"  markup: {(markup.Length > 600 ? markup[..600] + "..." : markup).ReplaceLineEndings(" ")}");
            Console.WriteLine($"  chromium: {chromium[i][options.InspectedProperties.Count]}");
            Console.WriteLine($"  library:  {new Box(rect.X, rect.Y, rect.Width, rect.Height)} client={element.ClientWidth}x{element.ClientHeight} scroll={element.ScrollWidth}x{element.ScrollHeight}");
            var style = element.OwnerDocument!.DefaultView!.GetComputedStyle(element);
            for (var j = 0; j < options.InspectedProperties.Count; j++)
            {
                var property = options.InspectedProperties[j];
                var value = style.GetPropertyValue(property);
                Console.WriteLine($"  {(value == chromium[i][j] ? " " : "*")} {property}: {chromium[i][j]} / {value}");
            }
        }
    }

    private static BrowserNewContextOptions CreateContextOptions(LayoutOptions options) => new()
    {
        // The scripts do not run in both (the library runs them only with a script engine): the layout is the one of the
        // parsed document and its style sheets, and the noscript elements are rendered
        JavaScriptEnabled = false,
        ViewportSize = new ViewportSize { Width = options.Width, Height = options.Height },
        DeviceScaleFactor = 1,
        ColorScheme = ColorScheme.Light,
        ReducedMotion = ReducedMotion.NoPreference,
        Locale = "en-US",
        ServiceWorkers = ServiceWorkerPolicy.Block,
    };

    private static async Task<Dictionary<string, Box>> MeasureChromiumAsync(IPage page, string script)
    {
        var result = await page.EvaluateAsync<JsonElement>(script);
        var keys = result.GetProperty("keys").EnumerateArray().Select(key => key.GetString()!).ToList();
        var rects = result.GetProperty("rects").EnumerateArray().Select(value => value.GetDouble()).ToList();
        var boxes = new Dictionary<string, Box>(keys.Count, StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            boxes[keys[i]] = new Box(rects[i * 4], rects[(i * 4) + 1], rects[(i * 4) + 2], rects[(i * 4) + 3]);
        }

        return boxes;
    }

    private static async Task<(BrowsingContext Context, Dictionary<string, Box> Boxes, Dictionary<string, Element> Elements)> MeasureLibraryAsync(string url, byte[] html, ResourceCache cache, string styleSheet, LayoutOptions options)
    {
        var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = cache.CreateLoader(),

            // The classic scrollbars of 15px of the library are the ones of headless Chromium
            Viewport = new ViewportOptions { Width = options.Width, Height = options.Height, DevicePixelRatio = 1, CaretVisible = false },
            DeclarativeRefreshEnabled = false,
            SpeculativeLoading = new SpeculativeLoadingOptions { PrefetchEnabled = false, PrerenderEnabled = false },
        });

        try
        {
            var stopwatch = Stopwatch.StartNew();
            context.Navigate(url, _ => new DocumentResource(html) { Headers = [new("Content-Type", "text/html; charset=utf-8")] });
            await ComparisonCommon.RunUntilIdleAsync(context.EventLoop, stopwatch, options.Timeout);
            await ComparisonCommon.PrepareLibraryAsync(context, styleSheet, stopwatch, options.Timeout);

            var document = context.Document;

            var boxes = new Dictionary<string, Box>(StringComparer.Ordinal);
            var elements = new Dictionary<string, Element>(StringComparer.Ordinal);
            if (document.DocumentElement is { } rootElement)
            {
                Visit(rootElement, "0");
            }

            return (context, boxes, elements);

            void Visit(Element element, string key)
            {
                var rect = element.GetBoundingClientRect();
                boxes[key] = new Box(rect.X, rect.Y, rect.Width, rect.Height);
                elements[key] = element;
                if (element.LocalName is "head" or "select" or "svg" or "math")
                    return;

                var index = 0;
                for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
                {
                    Visit(child, key + "/" + index.ToString(CultureInfo.InvariantCulture));
                    index++;
                }
            }
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static bool AreClose(Box a, Box b, double tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Width - b.Width) <= tolerance && Math.Abs(a.Height - b.Height) <= tolerance;

    internal sealed record Difference(string Key, IReadOnlyList<string> Kinds, string? FirstDifferentDescendant);

    /// <summary>
    /// Returns the differences that are not explained by another difference, in document order:
    /// <list type="bullet">
    /// <item>the elements of a parent whose x or width is different are not reported (the width of a parent changes the
    /// layout of its contents);</item>
    /// <item>a different x or width is reported when no previous sibling is different;</item>
    /// <item>a different y is reported when it is not the difference of the y of the previous sibling (else of the parent: the
    /// small differences within the tolerance add up in a long document), and no previous sibling is different (a different
    /// height of a previous sibling moves the next ones);</item>
    /// <item>a different height is reported when no child is different (the height of a parent depends on its contents), unless
    /// the bottom of its last child moved by the same amount;</item>
    /// <item>a different x or width of a table cell is reported for the first cell of its table only (the columns).</item>
    /// </list>
    /// The parent of an element is its nearest ancestor with a box (not display: contents).
    /// The first different descendant of a reported element (the first different child, recursively) helps to find the
    /// origin of a difference that propagates from the contents (e.g. the width of a float that depends on its contents).
    /// </summary>
    internal static List<Difference> FindDifferences(Dictionary<string, Box> chromium, Dictionary<string, Box> library, double tolerance, Func<string, string> getTag)
    {
        var keys = chromium.Keys.Where(library.ContainsKey).ToList();
        var different = keys.Where(key => !AreClose(chromium[key], library[key], tolerance)).ToHashSet(StringComparer.Ordinal);

        // The parent of an element is its nearest ancestor that has a box in both (the children of a display: contents
        // element are siblings of its siblings)
        var parents = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            var parent = GetParentKey(key);
            while (parent is not null && (!chromium.TryGetValue(parent, out var c) || IsEmpty(c)) && (!library.TryGetValue(parent, out var l) || IsEmpty(l)))
            {
                parent = GetParentKey(parent);
            }

            parents[key] = parent;
        }

        var withDifferentChild = new HashSet<string>(StringComparer.Ordinal);
        var firstDifferentChild = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (different.Contains(key) && parents[key] is { } parent)
            {
                withDifferentChild.Add(parent);
                firstDifferentChild.TryAdd(parent, key);
            }
        }

        // The last child of each element, and the previous sibling of each element (the elements with a box)
        var lastChildren = new Dictionary<string, string>(StringComparer.Ordinal);
        var previousSiblings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (parents[key] is { } parent && !(IsEmpty(chromium[key]) && IsEmpty(library[key])))
            {
                if (lastChildren.TryGetValue(parent, out var previous))
                    previousSiblings[key] = previous;

                lastChildren[parent] = key;
            }
        }

        var parentsWithDifferentChild = new HashSet<string>(StringComparer.Ordinal);
        var tablesWithDifferentColumns = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<Difference>();
        foreach (var key in keys)
        {
            if (!different.Contains(key))
                continue;

            var parent = parents[key];
            var previousSiblingIsDifferent = parent is not null && !parentsWithDifferentChild.Add(parent);
            var (c, l) = (chromium[key], library[key]);
            if (parent is not null && different.Contains(parent) && IsHorizontallyDifferent(chromium[parent], library[parent], tolerance))
                continue;

            // The columns of a table: the x and the width of a cell are the ones of the cells above it
            var horizontallyExplained = previousSiblingIsDifferent;
            if (getTag(key) is "td" or "th" && IsHorizontallyDifferent(c, l, tolerance) && GetTable(key) is { } table && !tablesWithDifferentColumns.Add(table))
                horizontallyExplained = true;

            var kinds = new List<string>();
            if (!horizontallyExplained)
            {
                if (Math.Abs(l.X - c.X) > tolerance)
                    kinds.Add("x");

                if (Math.Abs(l.Width - c.Width) > tolerance)
                    kinds.Add("width");
            }

            // The y relative to the previous sibling, else to the parent: the small differences of the elements before it, each
            // one within the tolerance (e.g. the rounding of the line heights), add up in a long document
            if (!previousSiblingIsDifferent)
            {
                var reference = previousSiblings.TryGetValue(key, out var previousSibling) ? previousSibling : parent;
                var referenceDeltaY = reference is not null ? library[reference].Y - chromium[reference].Y : 0;
                if (Math.Abs(l.Y - c.Y - referenceDeltaY) > tolerance)
                    kinds.Add("y");
            }

            // The height when no child is different, and when the bottom of the last child is not moved by the same amount
            // (the small differences of the children add up)
            if (Math.Abs(l.Height - c.Height) > tolerance && !withDifferentChild.Contains(key)
                && !(lastChildren.TryGetValue(key, out var lastChild) && Math.Abs(l.Height - c.Height - (library[lastChild].Y + library[lastChild].Height - l.Y - (chromium[lastChild].Y + chromium[lastChild].Height - c.Y))) <= tolerance))
            {
                kinds.Add("height");
            }

            if (kinds.Count == 0)
                continue;

            string? descendant = null;
            for (var current = key; firstDifferentChild.TryGetValue(current, out var child); current = child)
            {
                descendant = child;
            }

            result.Add(new Difference(key, kinds, descendant));
        }

        return result;

        string? GetTable(string key)
        {
            for (var current = GetParentKey(key); current is not null; current = GetParentKey(current))
            {
                if (getTag(current) == "table")
                    return current;
            }

            return null;
        }

        static bool IsEmpty(Box box) => box.Width == 0 && box.Height == 0;

        static string? GetParentKey(string key)
        {
            var index = key.LastIndexOf('/', StringComparison.Ordinal);
            return index < 0 ? null : key[..index];
        }
    }

    private static bool IsHorizontallyDifferent(Box a, Box b, double tolerance) => Math.Abs(a.X - b.X) > tolerance || Math.Abs(a.Width - b.Width) > tolerance;

    private static string[] GetLibraryStyle(Element element)
    {
        var window = element.OwnerDocument!.DefaultView!;
        var style = window.GetComputedStyle(element);
        var parent = element.ParentElement is { } parentElement ? window.GetComputedStyle(parentElement).GetPropertyValue("display") : "";
        return [style.GetPropertyValue("display"), style.GetPropertyValue("position"), style.GetPropertyValue("float"), parent];
    }

    private static string FormatStyle(string[] style) => $"display={style[0]} position={style[1]} float={style[2]} parent-display={style[3]}";

    /// <summary>Returns a selector of the element: its tag, id, first class and position from the nearest ancestor with a unique id.</summary>
    internal static string GetCssPath(Element element)
    {
        var parts = new List<string>();
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.Id.Length > 0 && IdentifierRegex().IsMatch(current.Id) && current.OwnerDocument!.GetElementById(current.Id) == current)
            {
                parts.Add("#" + current.Id);
                break;
            }

            var part = new StringBuilder(current.LocalName);
            var firstClass = current.ClassName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstClass is not null && IdentifierRegex().IsMatch(firstClass))
                part.Append('.').Append(firstClass);

            if (current.ParentElement is { } parent)
            {
                var index = 0;
                var count = 0;
                for (var sibling = parent.FirstElementChild; sibling is not null; sibling = sibling.NextElementSibling)
                {
                    if (sibling.LocalName == current.LocalName)
                    {
                        count++;
                        if (sibling == current)
                            index = count;
                    }
                }

                if (count > 1)
                    part.Append(":nth-of-type(").Append(index).Append(')');
            }

            parts.Add(part.ToString());
        }

        parts.Reverse();
        return string.Join(" > ", parts);
    }

    [GeneratedRegex("^-?[A-Za-z_][A-Za-z0-9_-]*$")]
    private static partial Regex IdentifierRegex();
}
