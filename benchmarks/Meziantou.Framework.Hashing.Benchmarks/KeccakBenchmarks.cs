using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Benchmarks;

[MemoryDiagnoser]
public class KeccakBenchmarks
{
    private byte[] _data = [];
    private readonly byte[] _digest = new byte[32];

    [Params(64, 1024, 1024 * 1024)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup() => _data = Blake2bBenchmarks.CreateData(Length);

    [Benchmark]
    public void Sha3_256() => Sha3.HashData(256, _data, _digest);

    [Benchmark]
    public void Shake128() => Shake.HashData(128, _data, _digest);

    [Benchmark]
    public void KangarooTwelve128() => KangarooTwelve.HashData(128, _data, _digest);
}
