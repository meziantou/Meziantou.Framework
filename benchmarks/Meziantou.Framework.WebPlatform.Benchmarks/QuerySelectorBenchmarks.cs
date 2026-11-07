using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

[MemoryDiagnoser]
public class QuerySelectorBenchmarks
{
    private Document _document = null!;

    [Params("a", "div.mw-parser-output p a[href^='/wiki/']", "#content", "li:nth-child(2n+1) > a", "div:has(> table)")]
    public string Selector { get; set; } = "a";

    [GlobalSetup]
    public void Setup() => _document = HtmlDocumentParser.ParseDocument(BenchmarkData.Get("wikipedia"));

    [Benchmark]
    public int Meziantou() => _document.QuerySelectorAll(Selector).Count;
}
