using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Http.Hsts.Benchmarks;

// The lookup HstsClientHandler makes for every request, with the host names as Uri.IdnHost reports them
[MemoryDiagnoser]
public class MustUpgradeRequestBenchmark
{
    private HstsDomainPolicyCollection _preloadOnly = null!;
    private HstsDomainPolicyCollection _preloadAndLearned = null!;

    [Params(
        "github.com", // preloaded, includeSubdomains
        "api.github.com", // covered by its preloaded parent
        "bbc.co.uk", // preloaded, without includeSubdomains
        "www.example.com", // not preloaded, and a preloaded name starts with com.example: every suffix is searched
        "api.contoso.com", // not preloaded, and no preloaded name starts with com.contoso
        "bucket.s3.amazonaws.com", // not preloaded, four labels
        "cdn.assets.static.example.co.uk", // not preloaded, more labels than any preloaded name
        "localhost")]
    public string Host { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        _preloadOnly = new HstsDomainPolicyCollection(includePreloadDomains: true);
        _preloadOnly.MustUpgradeRequest("warmup.example.com");

        _preloadAndLearned = new HstsDomainPolicyCollection(includePreloadDomains: true);
        for (var i = 0; i < 1000; i++)
        {
            _preloadAndLearned.Add($"host{i}.learned{i % 50}.test", TimeSpan.FromDays(365), includeSubdomains: i % 2 == 0);
        }
    }

    [Benchmark(Baseline = true)]
    public bool Preload() => _preloadOnly.MustUpgradeRequest(Host);

    [Benchmark]
    public bool PreloadAndLearned() => _preloadAndLearned.MustUpgradeRequest(Host);
}
