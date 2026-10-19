using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Lays out the Wikipedia article with its style sheets (7,864 elements). Each iteration loads a new document and computes
/// its styles (not measured), so the layout starts with cold caches: the box tree, the text shaping of every text run (with
/// the GSUB and GPOS lookups of the fonts) and the line breaking are measured.
/// </summary>
[MemoryDiagnoser]
public class LayoutBenchmarks
{
    private string _html = "";
    private BrowsingContext? _context;
    private Document _document = null!;

    [GlobalSetup]
    public void Setup() => _html = BenchmarkData.Get("wikipedia-styled");

    [IterationSetup]
    public void SetupDocument()
    {
        _context?.Dispose();
        _context = new BrowsingContext();
        _document = _context.Navigate("https://example.com/", _ => _html);
        var window = _document.DefaultView!;
        foreach (var element in _document.QuerySelectorAll("*"))
        {
            _ = window.GetComputedStyle((Element)element).GetPropertyValue("display");
        }
    }

    [GlobalCleanup]
    public void Cleanup() => _context?.Dispose();

    [Benchmark]
    public object? LayoutArticle() => _document.GetLayoutEngine().EnsureLayout();
}
