using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

[MemoryDiagnoser]
public class QuerySelectorBenchmarks
{
    private Document _document = null!;
    private global::AngleSharp.Dom.IDocument _angleSharpDocument = null!;

    [Params("a", "div.mw-parser-output p a[href^='/wiki/']", "#content", "li:nth-child(2n+1) > a", "div:has(> table)")]
    public string Selector { get; set; } = "a";

    [GlobalSetup]
    public void Setup()
    {
        var html = BenchmarkData.Get("wikipedia");
        _document = HtmlDocumentParser.ParseDocument(html);
        _angleSharpDocument = new global::AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);
    }

    [Benchmark(Baseline = true)]
    public int Meziantou() => _document.QuerySelectorAll(Selector).Count;

    [Benchmark]
    public int AngleSharp() => _angleSharpDocument.QuerySelectorAll(Selector).Length;
}
