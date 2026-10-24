using System.Diagnostics;
using System.Text.Json;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.PdfRendering;
using Microsoft.Playwright;

namespace ChromiumDiff;

internal sealed class RenderOptions
{
    public string? Filter { get; init; }
    public IReadOnlySet<string>? Scenarios { get; init; }
    public List<string> CorpusDirectories { get; } = [];
    public bool Pages { get; init; }
    public bool Record { get; init; }
    public required string OutputDirectory { get; init; }
    public string? Rasterizer { get; init; }
    public string? SummaryPath { get; init; }
    public bool NoFail { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>An accepted difference of the rendering comparison (render-known-differences.json).</summary>
/// <param name="Fixture">The file name of the fixture, or <c>*</c>.</param>
/// <param name="Scenario">The name of the scenario, a prefix followed by <c>*</c>, or <c>*</c>.</param>
/// <param name="Reason">Why the rendering differs.</param>
/// <param name="Output">The output (<c>screenshot</c>, <c>pdf</c>, <c>pages</c>), or null for all of them.</param>
internal sealed record RenderKnownDifference(string Fixture, string Scenario, string Reason, string? Output = null)
{
    public bool Matches(string fixture, string scenario, string output)
        => (Fixture == "*" || Fixture == fixture)
            && (Scenario == "*" || Scenario == scenario || (Scenario.EndsWith('*', StringComparison.Ordinal) && scenario.StartsWith(Scenario[..^1], StringComparison.Ordinal)))
            && (Output is null || Output == output);
}

internal enum RenderStatus
{
    Pass,
    Known,
    Unsupported,
    Unexpected,
    Error,
}

/// <summary>The result of the comparison of an output of a fixture in a scenario.</summary>
/// <param name="Output">
/// <c>screenshot</c>; <c>pdf</c> (the PDF of the library rasterized as the PDF of Chromium); <c>pages</c> (the pages rendered
/// by <c>RenderPages</c> compared with the PDF of Chromium rasterized).
/// </param>
internal sealed record RenderResult(string Scenario, string Fixture, string Output, RenderStatus Status, ImageMetrics? Metrics, IReadOnlyList<string> Unsupported, string? Message, IReadOnlyList<string> Images);

/// <summary>
/// Compares the exports of pages by Playwright (Chromium) and by the library: the screenshots (<c>page.screenshot()</c> vs
/// <c>TakeScreenshot</c>) and the PDF (<c>page.pdf()</c> vs <c>PrintToPdf</c> and <c>RenderPages</c>), in the scenarios of
/// <see cref="RenderScenarios"/> (devices, screenshot options, print options). Each page is loaded with the same resources,
/// without scripts, with the injected font and without animations (as the layout comparison). The images are compared by
/// <see cref="ImageComparison"/>, written with a diff image in the output directory, and summarized in a report.
/// </summary>
internal static class RenderComparison
{
    private const string BaseUrl = "https://chromium-diff.test/";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };

    public static async Task<int> RunAsync(string root, RenderOptions options)
    {
        var toolDirectory = Path.Combine(root, "tools", "Meziantou.Framework.WebPlatform.ChromiumDiff");
        var knownDifferences = JsonSerializer.Deserialize<List<RenderKnownDifference>>(File.ReadAllText(Path.Combine(toolDirectory, "render-known-differences.json")), JsonOptions) ?? [];
        var styleSheet = ComparisonCommon.CreateStyleSheet(root);
        var rasterizer = PdfRasterizer.Create(options.Rasterizer);

        // The fixtures of the corpus (with their resources), the files of the --corpus directories, the pages of pages.txt
        var inputs = new List<RenderInput>();
        var corpus = Path.Combine(toolDirectory, "render-corpus");
        foreach (var directory in (IEnumerable<string>)[corpus, .. options.CorpusDirectories])
        {
            var cache = ResourceCache.FromDirectory(directory, BaseUrl);
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly).Where(IsDocument).Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                inputs.Add(new RenderInput(name, BaseUrl + name, File.ReadAllBytes(file), ResourceCache.GetContentType(file), cache, Page: null));
            }
        }

        if (options.Pages)
        {
            foreach (var page in Corpus.GetPages(root))
                inputs.Add(new RenderInput(page.Name + ".html", page.Url, File.ReadAllBytes(page.Path), "text/html; charset=utf-8", Cache: null, page));
        }

        inputs = [.. inputs.Where(input => options.Filter is null || input.Name.Contains(options.Filter, StringComparison.Ordinal))];
        var scenarios = RenderScenarios.All.Where(s => options.Scenarios is null || options.Scenarios.Contains(s.Name)).ToList();

        if (Directory.Exists(options.OutputDirectory))
            Directory.Delete(options.OutputDirectory, recursive: true);

        Directory.CreateDirectory(options.OutputDirectory);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await ComparisonCommon.LaunchChromiumAsync(playwright, classicScrollbars: false);
        Console.WriteLine($"Chromium {browser.Version}, {inputs.Count} page(s), {scenarios.Count} scenario(s), PDF rasterizer: {rasterizer.Name}");

        var results = new List<RenderResult>();
        var stopwatch = Stopwatch.StartNew();
        foreach (var scenario in scenarios)
        {
            var device = ResolvedDevice.Resolve(playwright, scenario);
            foreach (var input in inputs)
            {
                // The pages of pages.txt are rendered in the screenshot scenarios of the desktop only
                if (input.Page is null ? !scenario.Fixtures(input.Name) : scenario.Name is not ("desktop" or "omit-background"))
                    continue;

                var mapping = RenderScenarios.Map(scenario, device);
                var directory = Path.Combine(options.OutputDirectory, scenario.Name, Path.GetFileNameWithoutExtension(input.Name));
                Directory.CreateDirectory(directory);
                List<RenderResult> inputResults;
                try
                {
                    var cache = input.Cache ?? await GetPageCacheAsync(root, browser, device, input, styleSheet, options.Record);
                    inputResults = scenario.Kind is RenderKind.Pdf
                        ? await ComparePdfAsync(browser, scenario, device, mapping, input, cache, styleSheet, rasterizer, directory, options)
                        : [await CompareScreenshotAsync(browser, scenario, device, mapping, input, cache, styleSheet, directory, options)];
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    inputResults = [new RenderResult(scenario.Name, input.Name, scenario.Kind is RenderKind.Pdf ? "pdf" : "screenshot", RenderStatus.Error, Metrics: null, mapping.Unsupported, ex.GetType().Name + ": " + ex.Message.Split('\n')[0], [])];
                }

                foreach (var result in inputResults)
                {
                    var classified = Classify(result, GetThresholds(scenario, result.Output));
                    results.Add(classified);
                    Console.WriteLine($"{classified.Status.ToString().ToUpperInvariant(),-11} {scenario.Name} {input.Name} [{classified.Output}] {classified.Metrics?.Describe()}{(classified.Message is null ? "" : " " + classified.Message)}{(classified.Unsupported.Count > 0 ? " (unsupported: " + string.Join(", ", classified.Unsupported) + ")" : "")}");
                }
            }
        }

        // A known difference that matches no result must be removed from the list (only when everything is compared)
        var stale = options.Filter is null && options.Scenarios is null
            ? knownDifferences.Where(k => !results.Any(r => r.Status is RenderStatus.Known && r.Message == k.Reason)).ToList()
            : [];

        RenderReport.Write(options.OutputDirectory, browser.Version, rasterizer.Name, scenarios, results, stale);
        if (options.SummaryPath is not null)
            RenderReport.WriteSummary(options.SummaryPath, browser.Version, rasterizer.Name, scenarios, results);

        var counts = results.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"Compared {results.Count} output(s) in {stopwatch.Elapsed.TotalSeconds:F1} s: "
            + string.Join(", ", Enum.GetValues<RenderStatus>().Select(s => $"{counts.GetValueOrDefault(s)} {s.ToString().ToLowerInvariant()}"))
            + $"; {stale.Count} stale known difference(s). Report: {Path.Combine(options.OutputDirectory, "report.html")}");
        foreach (var known in stale)
        {
            Console.WriteLine($"STALE KNOWN DIFFERENCE {known.Fixture} {known.Scenario}: {known.Reason} (no result matches it: remove it from render-known-differences.json)");
        }

        if (options.NoFail)
            return 0;

        return counts.GetValueOrDefault(RenderStatus.Unexpected) == 0 && counts.GetValueOrDefault(RenderStatus.Error) == 0 && stale.Count == 0 ? 0 : 1;

        RenderResult Classify(RenderResult result, ComparisonThresholds thresholds)
        {
            if (result.Status is RenderStatus.Error)
                return result;

            var passes = result.Status is not RenderStatus.Unexpected && (result.Metrics?.Passes(thresholds) ?? true);
            if (result.Unsupported.Count > 0)
                return result with { Status = RenderStatus.Unsupported };

            if (passes)
                return result with { Status = RenderStatus.Pass };

            var known = knownDifferences.FirstOrDefault(k => k.Matches(result.Fixture, result.Scenario, result.Output));
            return known is null ? result with { Status = RenderStatus.Unexpected } : result with { Status = RenderStatus.Known, Message = known.Reason };
        }
    }

    /// <summary>
    /// The thresholds of an output: the pages rendered by the library are compared with the PDF of Chromium rasterized by
    /// another rasterizer (whose images are not smoothed and whose glyphs are positioned differently), so only the layout
    /// and the pagination are compared.
    /// </summary>
    private static ComparisonThresholds GetThresholds(RenderScenario scenario, string output) => output == "pages" ? ComparisonThresholds.Lossy : scenario.Thresholds;

    private static bool IsDocument(string path) => Path.GetExtension(path) is ".html" or ".htm" or ".svg" or ".xhtml";

    private sealed record RenderInput(string Name, string Url, byte[] Content, string ContentType, ResourceCache? Cache, Page? Page);

    private static async Task<ResourceCache> GetPageCacheAsync(string root, IBrowser browser, ResolvedDevice device, RenderInput input, string styleSheet, bool record)
    {
        var directory = Path.Combine(root, "artifacts", "chromium-diff", "resources", input.Page!.Name);
        if (!record && ResourceCache.Load(directory) is { } cache)
            return cache;

        await using var context = await browser.NewContextAsync(device.CreateContextOptions());
        return await ResourceCache.RecordAsync(context, input.Url, input.Content, directory, p => ComparisonCommon.PrepareChromiumAsync(p, styleSheet));
    }

    private static async Task<IPage> LoadChromiumAsync(IBrowserContext context, RenderScenario scenario, RenderInput input, ResourceCache cache, string styleSheet)
    {
        var page = await context.NewPageAsync();
        await cache.RouteAsync(page, input.Url, input.Content, input.ContentType);
        await page.GotoAsync(input.Url, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 120_000 });
        await ComparisonCommon.PrepareChromiumAsync(page, styleSheet);
        if (scenario.PrintMedia)
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { Media = Media.Print });

        return page;
    }

    private static async Task<BrowsingContext> LoadLibraryAsync(LibraryMapping mapping, RenderInput input, ResourceCache cache, string styleSheet, RenderOptions options)
    {
        var context = new BrowsingContext(new BrowsingContextOptions
        {
            DocumentLoader = cache.CreateLoader(),
            Viewport = mapping.Viewport,
            RenderingBackend = new ImagingRenderingBackend(),
            DeclarativeRefreshEnabled = false,
            SpeculativeLoading = new SpeculativeLoadingOptions { PrefetchEnabled = false, PrerenderEnabled = false },
        });

        try
        {
            var stopwatch = Stopwatch.StartNew();
            context.Navigate(input.Url, _ => new DocumentResource(input.Content) { Headers = [new("Content-Type", input.ContentType)] });
            await ComparisonCommon.RunUntilIdleAsync(context.EventLoop, stopwatch, options.Timeout);
            await ComparisonCommon.PrepareLibraryAsync(context, styleSheet, stopwatch, options.Timeout);
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    private static async Task<RenderResult> CompareScreenshotAsync(IBrowser browser, RenderScenario scenario, ResolvedDevice device, LibraryMapping mapping, RenderInput input, ResourceCache cache, string styleSheet, string directory, RenderOptions options)
    {
        byte[] chromiumBytes;
        await using (var chromiumContext = await browser.NewContextAsync(device.CreateContextOptions()))
        {
            var page = await LoadChromiumAsync(chromiumContext, scenario, input, cache, styleSheet);
            var screenshotOptions = RenderScenarios.CreateScreenshotOptions(scenario);
            chromiumBytes = scenario.Kind is RenderKind.ElementScreenshot
                ? await page.Locator(scenario.ElementSelector).ScreenshotAsync(new LocatorScreenshotOptions
                {
                    OmitBackground = screenshotOptions.OmitBackground,
                    Type = screenshotOptions.Type,
                    Quality = screenshotOptions.Quality,
                    Animations = screenshotOptions.Animations,
                    Caret = screenshotOptions.Caret,
                    Scale = screenshotOptions.Scale,
                })
                : await page.ScreenshotAsync(screenshotOptions);
        }

        using var chromium = Image.Load<Rgba32>(chromiumBytes);
        using var context = await LoadLibraryAsync(mapping, input, cache, styleSheet, options);
        using var library = TakeLibraryScreenshot(context, scenario, mapping);

        var (metrics, diff) = ImageComparison.Compare(chromium, library, scenario.Thresholds);
        using (diff)
        {
            chromium.Save(Path.Combine(directory, "chromium.png"));
            library.Save(Path.Combine(directory, "library.png"));
            diff.Save(Path.Combine(directory, "diff.png"));
        }

        return new RenderResult(scenario.Name, input.Name, "screenshot", RenderStatus.Pass, metrics, mapping.Unsupported, Message: null, [Path.Combine(scenario.Name, Path.GetFileNameWithoutExtension(input.Name))]);
    }

    private static Image<Rgba32> TakeLibraryScreenshot(BrowsingContext context, RenderScenario scenario, LibraryMapping mapping)
    {
        if (scenario.Kind is RenderKind.ElementScreenshot)
        {
            var element = context.Document.QuerySelector(scenario.ElementSelector) ?? throw new InvalidOperationException($"No element matches '{scenario.ElementSelector}'");
            return scenario.Jpeg ? Image.Load<Rgba32>(element.TakeScreenshotAsJpeg(mapping.Screenshot)) : element.TakeScreenshot(mapping.Screenshot);
        }

        return scenario.Jpeg ? Image.Load<Rgba32>(context.TakeScreenshotAsJpeg(mapping.Screenshot)) : context.TakeScreenshot(mapping.Screenshot);
    }

    private static async Task<List<RenderResult>> ComparePdfAsync(IBrowser browser, RenderScenario scenario, ResolvedDevice device, LibraryMapping mapping, RenderInput input, ResourceCache cache, string styleSheet, PdfRasterizer rasterizer, string directory, RenderOptions options)
    {
        byte[] chromiumPdf;
        await using (var chromiumContext = await browser.NewContextAsync(device.CreateContextOptions()))
        {
            var page = await LoadChromiumAsync(chromiumContext, scenario, input, cache, styleSheet);
            chromiumPdf = await page.PdfAsync(RenderScenarios.CreatePdfOptions(scenario));
        }

        byte[] libraryPdf;
        IReadOnlyList<Image<Rgba32>> libraryPages;
        using (var context = await LoadLibraryAsync(mapping, input, cache, styleSheet, options))
        {
            using var stream = new MemoryStream();
            context.PrintToPdf(stream, mapping.Print, mapping.Pdf);
            libraryPdf = stream.ToArray();
            libraryPages = context.RenderPages(mapping.Print, new ScreenshotOptions { DevicePixelRatio = 1 });
        }

        await File.WriteAllBytesAsync(Path.Combine(directory, "chromium.pdf"), chromiumPdf);
        await File.WriteAllBytesAsync(Path.Combine(directory, "library.pdf"), libraryPdf);

        // The structure: the number of pages and their sizes (in points)
        var chromiumSizes = GetPageSizes(chromiumPdf);
        var librarySizes = GetPageSizes(libraryPdf);
        var structure = new List<string>();
        if (chromiumSizes.Count != librarySizes.Count)
            structure.Add($"{chromiumSizes.Count} page(s) vs {librarySizes.Count}");

        for (var i = 0; i < Math.Min(chromiumSizes.Count, librarySizes.Count); i++)
        {
            if (Math.Abs(chromiumSizes[i].Width - librarySizes[i].Width) > 0.5 || Math.Abs(chromiumSizes[i].Height - librarySizes[i].Height) > 0.5)
            {
                structure.Add(System.FormattableString.Invariant($"page {i + 1} is {chromiumSizes[i].Width:0.#}x{chromiumSizes[i].Height:0.#}pt vs {librarySizes[i].Width:0.#}x{librarySizes[i].Height:0.#}pt"));
                break;
            }
        }

        var chromiumImages = await rasterizer.RasterizeAsync(chromiumPdf, directory, "chromium");
        var libraryImages = await rasterizer.RasterizeAsync(libraryPdf, directory, "library");
        try
        {
            var relative = Path.Combine(scenario.Name, Path.GetFileNameWithoutExtension(input.Name));
            return
            [
                ComparePages("pdf", libraryImages),
                ComparePages("pages", libraryPages),
            ];

            RenderResult ComparePages(string output, IReadOnlyList<Image<Rgba32>> pages)
            {
                ImageMetrics? worst = null;
                var failing = new List<int>();
                var images = new List<string>();
                for (var i = 0; i < Math.Min(chromiumImages.Count, pages.Count); i++)
                {
                    var thresholds = GetThresholds(scenario, output);
                    var (metrics, diff) = ImageComparison.Compare(chromiumImages[i], pages[i], thresholds);
                    using (diff)
                    {
                        var prefix = $"{output}-{i + 1}";
                        chromiumImages[i].Save(Path.Combine(directory, prefix + "-chromium.png"));
                        pages[i].Save(Path.Combine(directory, prefix + "-library.png"));
                        diff.Save(Path.Combine(directory, prefix + "-diff.png"));
                        images.Add(Path.Combine(relative, prefix));
                    }

                    if (!metrics.Passes(thresholds))
                        failing.Add(i + 1);

                    if (worst is null || !metrics.SameSize || (worst.SameSize && metrics.Ssim < worst.Ssim))
                        worst = metrics;
                }

                var message = string.Join("; ", structure.Concat(failing.Count > 0 ? [$"page(s) {string.Join(", ", failing)} differ"] : []));
                var status = structure.Count > 0 ? RenderStatus.Unexpected : RenderStatus.Pass;
                return new RenderResult(scenario.Name, input.Name, output, status, worst, mapping.Unsupported, message.Length == 0 ? null : message, images);
            }
        }
        finally
        {
            foreach (var image in chromiumImages.Concat(libraryImages).Concat(libraryPages))
                image.Dispose();
        }
    }

    private static List<(double Width, double Height)> GetPageSizes(byte[] pdf)
    {
        var document = PdfDocument.Open(pdf);
        var sizes = new List<(double Width, double Height)>(document.Pages.Count);
        for (var i = 0; i < document.Pages.Count; i++)
        {
            var box = document.Pages[i].MediaBox;
            sizes.Add((box.Width, box.Height));
        }

        return sizes;
    }
}

/// <summary>Rasterizes the pages of a PDF at 96 dpi (one pixel per CSS pixel), with Poppler (pdftoppm) or Meziantou.Framework.Pdf.</summary>
internal abstract class PdfRasterizer
{
    public abstract string Name { get; }

    public abstract Task<IReadOnlyList<Image<Rgba32>>> RasterizeAsync(byte[] pdf, string directory, string name);

    public static PdfRasterizer Create(string? name)
    {
        name ??= FindExecutable("pdftoppm") is not null ? "pdftoppm" : "meziantou";
        return name switch
        {
            "pdftoppm" => new PopplerRasterizer(FindExecutable("pdftoppm") ?? throw new InvalidOperationException("pdftoppm (Poppler) is not on the PATH")),
            "meziantou" => new MeziantouRasterizer(),
            _ => throw new ArgumentException($"Unknown rasterizer '{name}' (pdftoppm or meziantou)", nameof(name)),
        };
    }

    private static string? FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var candidate in (IEnumerable<string>)[name, name + ".exe"])
            {
                var path = Path.Combine(directory, candidate);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    private sealed class PopplerRasterizer(string executable) : PdfRasterizer
    {
        public override string Name => "pdftoppm";

        public override async Task<IReadOnlyList<Image<Rgba32>>> RasterizeAsync(byte[] pdf, string directory, string name)
        {
            var temporary = Path.Combine(directory, name + "-raster");
            Directory.CreateDirectory(temporary);
            var input = Path.Combine(temporary, "input.pdf");
            await File.WriteAllBytesAsync(input, pdf);
            var startInfo = new ProcessStartInfo(executable) { RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in (IEnumerable<string>)["-r", "96", "-png", input, Path.Combine(temporary, "page")])
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Cannot start pdftoppm");
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"pdftoppm failed ({process.ExitCode}): {error}");

            // The files are page-1.png, or page-01.png when there are 10 pages or more
            var files = Directory.EnumerateFiles(temporary, "page-*.png")
                .OrderBy(file => int.Parse(Path.GetFileNameWithoutExtension(file)["page-".Length..], System.Globalization.CultureInfo.InvariantCulture))
                .ToList();
            var images = files.Select(file => Image.Load<Rgba32>(file)).ToList();
            Directory.Delete(temporary, recursive: true);
            return images;
        }
    }

    private sealed class MeziantouRasterizer : PdfRasterizer
    {
        public override string Name => "Meziantou.Framework.Pdf";

        public override Task<IReadOnlyList<Image<Rgba32>>> RasterizeAsync(byte[] pdf, string directory, string name)
        {
            var document = PdfDocument.Open(pdf);
            var options = new PdfRenderOptions { Dpi = 96, Background = new Rgba32(255, 255, 255) };
            var images = new List<Image<Rgba32>>(document.Pages.Count);
            for (var i = 0; i < document.Pages.Count; i++)
                images.Add(PdfRenderer.RenderPage(document.Pages[i], options));

            return Task.FromResult<IReadOnlyList<Image<Rgba32>>>(images);
        }
    }
}
