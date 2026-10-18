using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

[MemoryDiagnoser]
public class ParseBenchmarks
{
    private string _html = "";

    [Params("small", "wikipedia", "html-spec")]
    public string Document { get; set; } = "small";

    [GlobalSetup]
    public void Setup() => _html = BenchmarkData.Get(Document);

    [Benchmark(Baseline = true)]
    public object Meziantou() => HtmlDocumentParser.ParseDocument(_html);

    [Benchmark]
    public object AngleSharp() => new global::AngleSharp.Html.Parser.HtmlParser().ParseDocument(_html);

    [Benchmark]
    public object HtmlAgilityPack()
    {
        var document = new global::HtmlAgilityPack.HtmlDocument();
        document.LoadHtml(_html);
        return document;
    }
}
