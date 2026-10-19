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

    [Benchmark]
    public object Meziantou() => HtmlDocumentParser.ParseDocument(_html);
}
