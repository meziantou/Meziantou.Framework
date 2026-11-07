using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Parses a style sheet into its object model (rules, selectors and declarations).</summary>
[MemoryDiagnoser]
public class CssParseBenchmarks
{
    private string _css = "";

    [Params("wikipedia-css", "bootstrap-css")]
    public string StyleSheet { get; set; } = "wikipedia-css";

    [GlobalSetup]
    public void Setup() => _css = BenchmarkData.Get(StyleSheet);

    [Benchmark]
    public object Meziantou()
    {
        var sheet = new CssStyleSheet();
        sheet.ReplaceSync(_css);
        return sheet;
    }
}
