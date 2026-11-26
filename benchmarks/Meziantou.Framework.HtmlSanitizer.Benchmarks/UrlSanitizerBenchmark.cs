using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Sanitizers.Benchmarks;

/// <summary>Validates URLs and srcset values.</summary>
[MemoryDiagnoser]
public class UrlSanitizerBenchmark
{
    [Params("https://example.com/path/to/page?query=1#hash", "/relative/path.png", "javascript:alert(1)", "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")]
    public string Value { get; set; } = "";

    [Benchmark]
    public bool IsSafeUrl() => UrlSanitizer.IsSafeUrl(Value);

    [Benchmark]
    public bool IsSafeSrcset() => UrlSanitizer.IsSafeSrcset("https://example.com/a.png 1x, https://example.com/b.png 2x, /c.png 640w");
}
