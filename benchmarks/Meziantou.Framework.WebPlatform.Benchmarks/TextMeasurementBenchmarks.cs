using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Shapes and measures the text of the Wikipedia article (the non-blank text nodes of the body, and their words) with the default font (Tinos, 16px, kerning): the shaping of each run without cache, the measurement of each word
/// with a cold cache (the first layout of a document) and with a warm cache (a relayout).
/// </summary>
[MemoryDiagnoser]
public class TextMeasurementBenchmarks
{
    private BrowsingContext? _context;
    private FontFallbackList _fonts = null!;
    private TextShapingOptions _options;
    private string[] _runs = [];
    private string[] _words = [];

    [GlobalSetup]
    public void Setup()
    {
        using var articleContext = new BrowsingContext();
        var article = articleContext.Navigate("https://example.com/", _ => BenchmarkData.Get("wikipedia"));
        var runs = new List<string>();
        foreach (var node in article.Body!.Descendants())
        {
            if (node is Text text && !string.IsNullOrWhiteSpace(text.Data))
            {
                runs.Add(text.Data);
            }
        }

        _runs = [.. runs];
        _words = [.. runs.SelectMany(run => run.Split(' ', StringSplitOptions.RemoveEmptyEntries))];

        _context = new BrowsingContext();
        var document = _context.Navigate("https://example.com/", _ => "<div style='font: 16px serif'></div>");
        var style = document.GetStyleEngine().GetStyle(document.QuerySelector("div")!);
        _fonts = style.Fonts;
        _options = style.ShapingOptions;
    }

    [GlobalCleanup]
    public void Cleanup() => _context?.Dispose();

    [Benchmark]
    public double ShapeRuns()
    {
        var width = 0d;
        foreach (var run in _runs)
        {
            width += TextShaper.ShapeUncached(run, _fonts, _options).Width;
        }

        return width;
    }

    [Benchmark]
    public double MeasureWordsColdCache()
    {
        _fonts.ShapeCache.Clear();
        var width = 0d;
        foreach (var word in _words)
        {
            width += TextShaper.MeasureWidth(word, _fonts, _options);
        }

        return width;
    }

    [Benchmark]
    public double MeasureWordsWarmCache()
    {
        var width = 0d;
        foreach (var word in _words)
        {
            width += TextShaper.MeasureWidth(word, _fonts, _options);
        }

        return width;
    }
}
