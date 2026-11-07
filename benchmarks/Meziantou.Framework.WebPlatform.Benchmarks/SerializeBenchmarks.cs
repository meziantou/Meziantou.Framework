using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

/// <summary>Serializes a parsed document (outerHTML) and reads its text (textContent).</summary>
[MemoryDiagnoser]
public class SerializeBenchmarks
{
    private Document _document = null!;

    [Params("wikipedia", "html-spec")]
    public string Document { get; set; } = "wikipedia";

    [GlobalSetup]
    public void Setup() => _document = HtmlDocumentParser.ParseDocument(BenchmarkData.Get(Document));

    [Benchmark]
    public string Meziantou_OuterHtml() => _document.DocumentElement!.OuterHtml;

    [Benchmark]
    public string? Meziantou_TextContent() => _document.DocumentElement!.TextContent;
}
