using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Http.Hsts.Benchmarks;

// What HstsClientHandler does for every HTTPS response that carries a Strict-Transport-Security header
[MemoryDiagnoser]
public class AddPolicyBenchmark
{
    private HstsDomainPolicyCollection _policies = null!;

    [GlobalSetup]
    public void Setup()
    {
        _policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
        _policies.Add("www.example.com", TimeSpan.FromDays(365), includeSubdomains: true);
    }

    [Benchmark]
    public void UpdateExisting() => _policies.Add("www.example.com", TimeSpan.FromDays(365), includeSubdomains: true);
}
