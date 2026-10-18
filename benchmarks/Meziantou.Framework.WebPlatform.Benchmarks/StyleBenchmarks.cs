using AngleSharp;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;
using BrowsingContext = Meziantou.Framework.WebPlatform.Dom.BrowsingContext;

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
    private global::AngleSharp.IBrowsingContext? _angleSharpContext;
    private global::AngleSharp.Dom.IWindow _angleSharpWindow = null!;
    private global::AngleSharp.Dom.IElement[] _angleSharpElements = [];

    [Params("wikipedia-styled", "small-bootstrap")]
    public string Document { get; set; } = "wikipedia-styled";

    [GlobalSetup]
    public void Setup() => _html = BenchmarkData.Get(Document);

    [IterationSetup(Target = nameof(Meziantou))]
    public void SetupMeziantou()
    {
        _context?.Dispose();
        _context = new BrowsingContext();
        var document = _context.Navigate("https://example.com/", _ => _html);
        _window = document.DefaultView!;
        _elements = [.. document.QuerySelectorAll("*").Cast<Element>()];
    }

    [IterationSetup(Target = nameof(AngleSharp))]
    public void SetupAngleSharp()
    {
        _angleSharpContext?.Dispose();
        _angleSharpContext = global::AngleSharp.BrowsingContext.New(global::AngleSharp.Configuration.Default.WithCss().WithRenderDevice(new global::AngleSharp.Css.DefaultRenderDevice { DeviceWidth = 800, DeviceHeight = 600, ViewPortWidth = 800, ViewPortHeight = 600, FontSize = 16 }));
        var document = _angleSharpContext.OpenAsync(response => response.Content(_html).Address("https://example.com/")).GetAwaiter().GetResult();
        _angleSharpWindow = document.DefaultView!;
        _angleSharpElements = [.. document.QuerySelectorAll("*")];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context?.Dispose();
        _angleSharpContext?.Dispose();
    }

    [Benchmark(Baseline = true)]
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

    [Benchmark]
    public int AngleSharp()
    {
        var length = 0;
        foreach (var element in _angleSharpElements)
        {
            try
            {
                var style = global::AngleSharp.Dom.WindowExtensions.GetComputedStyle(_angleSharpWindow, element);
                foreach (var property in Properties)
                {
                    length += style.GetPropertyValue(property).Length;
                }
            }
            catch (NullReferenceException)
            {
                // AngleSharp.Css 1.1.2 fails to compute some box-shadow values (123 elements of the Wikipedia article)
            }
        }

        return length;
    }
}
