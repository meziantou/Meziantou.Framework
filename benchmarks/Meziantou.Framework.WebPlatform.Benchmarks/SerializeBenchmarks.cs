using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Serializes a parsed document (outerHTML) and reads its text (textContent).</summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[CategoriesColumn]
public class SerializeBenchmarks
{
    private Document _document = null!;
    private global::AngleSharp.Dom.IDocument _angleSharpDocument = null!;

    [Params("wikipedia", "html-spec")]
    public string Document { get; set; } = "wikipedia";

    [GlobalSetup]
    public void Setup()
    {
        var html = BenchmarkData.Get(Document);
        _document = HtmlDocumentParser.ParseDocument(html);
        _angleSharpDocument = new global::AngleSharp.Html.Parser.HtmlParser().ParseDocument(html);
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("OuterHtml")]
    public string Meziantou_OuterHtml() => _document.DocumentElement!.OuterHtml;

    [Benchmark]
    [BenchmarkCategory("OuterHtml")]
    public string AngleSharp_OuterHtml() => _angleSharpDocument.DocumentElement.OuterHtml;

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("TextContent")]
    public string? Meziantou_TextContent() => _document.DocumentElement!.TextContent;

    [Benchmark]
    [BenchmarkCategory("TextContent")]
    public string? AngleSharp_TextContent() => _angleSharpDocument.DocumentElement.TextContent;
}
