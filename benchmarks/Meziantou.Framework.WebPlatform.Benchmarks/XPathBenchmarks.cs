using BenchmarkDotNet.Attributes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Benchmarks;

[MemoryDiagnoser]
public class XPathBenchmarks
{
    private Document _document = null!;
    private XPathExpression _expression = null!;

    [Params(1000, 8000, 16000)]
    public int NodeCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var html = "<!DOCTYPE html><ul>" + string.Concat(Enumerable.Repeat("<li>x</li>", NodeCount)) + "</ul>";
        _document = HtmlDocumentParser.ParseDocument(html);
        _expression = _document.CreateExpression("//li");
        _ = _expression.Evaluate(_document, XPathResult.FirstOrderedNodeType).SingleNodeValue;
        _ = _expression.Evaluate(_document, XPathResult.OrderedNodeSnapshotType).SnapshotLength;
    }

    [Benchmark]
    public Node? FirstOrderedNode() => _expression.Evaluate(_document, XPathResult.FirstOrderedNodeType).SingleNodeValue;

    [Benchmark]
    public uint OrderedSnapshot() => _expression.Evaluate(_document, XPathResult.OrderedNodeSnapshotType).SnapshotLength;
}
