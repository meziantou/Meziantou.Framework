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

/// <summary>
/// Resizes the outer grid of nested grids in a subgrid, with a size container inside (WPT
/// css/css-grid/subgrid/nested-grid-subgrid-resize.html): each operation lays out the document twice (200px, then 100px).
/// </summary>
[MemoryDiagnoser]
public class NestedGridBenchmarks
{
    private BrowsingContext? _context;
    private HtmlElement _grid = null!;
    private HtmlElement _container = null!;

    [Params(4, 8, 11)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var html = "<!DOCTYPE html><style>.grid { display: grid } #subgrid { display: grid; grid-template-columns: subgrid } #box { width: 100px; height: 20px } #container { container-type: inline-size; width: 100% } #panel { height: 10px } @container (width < 150px) { #panel { display: none } }</style>"
            + "<div id=grid class=grid style='width: 100px'><div id=subgrid>" + string.Concat(Enumerable.Repeat("<div class=grid>", Depth))
            + "<div id=box></div><div id=container><div id=panel></div></div>" + string.Concat(Enumerable.Repeat("</div>", Depth)) + "</div></div>";
        _context = new BrowsingContext();
        var document = _context.Navigate("https://example.com/", _ => html);
        _grid = (HtmlElement)document.GetElementById("grid")!;
        _container = (HtmlElement)document.GetElementById("container")!;
        _ = _container.OffsetWidth;
    }

    [GlobalCleanup]
    public void Cleanup() => _context?.Dispose();

    [Benchmark]
    public int Resize()
    {
        _grid.Style.SetProperty("width", "200px");
        var wide = _container.OffsetWidth;
        _grid.Style.SetProperty("width", "100px");
        return wide + _container.OffsetWidth;
    }
}
