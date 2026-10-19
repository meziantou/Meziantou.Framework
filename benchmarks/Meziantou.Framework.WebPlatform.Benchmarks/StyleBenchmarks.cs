using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>
/// Computes the style of all the elements of a document: the Wikipedia article with its style sheets (7,864 elements, 230 KB
/// of CSS), and the small page with Bootstrap (280 KB of CSS). Each iteration loads a new document (not measured), so no
/// computed style is cached.
/// </summary>
[MemoryDiagnoser]
public class StyleBenchmarks
{
    private static readonly string[] Properties = ["display", "color", "font-size", "margin-top"];

    private string _html = "";
    private BrowsingContext? _context;
    private Window _window = null!;
    private Element[] _elements = [];

    [Params("wikipedia-styled", "small-bootstrap")]
    public string Document { get; set; } = "wikipedia-styled";

    [GlobalSetup]
    public void Setup() => _html = BenchmarkData.Get(Document);

    [IterationSetup]
    public void SetupIteration()
    {
        _context?.Dispose();
        _context = new BrowsingContext();
        var document = _context.Navigate("https://example.com/", _ => _html);
        _window = document.DefaultView!;
        _elements = [.. document.QuerySelectorAll("*").Cast<Element>()];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
    }

    [Benchmark]
    public int Meziantou()
    {
        var length = 0;
        foreach (var element in _elements)
        {
            var style = _window.GetComputedStyle(element);
            foreach (var property in Properties)
            {
                length += style.GetPropertyValue(property).Length;
            }
        }

        return length;
    }
}
