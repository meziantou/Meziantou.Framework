using System.Globalization;
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

/// <summary>Runs <see cref="StyleBenchmarks"/> in a loop, to profile it (e.g. with <c>dotnet-trace</c>).</summary>
internal static class StyleProfile
{
    public static int Run(string document, int count)
    {
        var benchmarks = new StyleBenchmarks { Document = document };
        benchmarks.Setup();
        var cpu = TimeSpan.Zero;
        var allocated = 0L;
        var minimum = TimeSpan.MaxValue;
        var length = 0;
        for (var i = 0; i < count; i++)
        {
            benchmarks.SetupIteration();
            var start = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime;
            var allocatedStart = GC.GetAllocatedBytesForCurrentThread();
            length = benchmarks.Meziantou();
            var elapsed = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime - start;
            allocated += GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
            cpu += elapsed;
            minimum = elapsed < minimum ? elapsed : minimum;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{document}: {cpu.TotalMilliseconds / count:F3} ms CPU per invocation (minimum {minimum.TotalMilliseconds:F3} ms), {allocated / count / 1024 / 1024.0:F1} MB allocated, result {length}"));
        benchmarks.Cleanup();
        return 0;
    }
}
