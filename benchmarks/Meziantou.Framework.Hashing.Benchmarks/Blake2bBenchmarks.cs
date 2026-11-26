using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Benchmarks;

[MemoryDiagnoser]
public class Blake2bBenchmarks
{
    private byte[] _data = [];
    private readonly byte[] _digest = new byte[64];

    [Params(64, 1024, 1024 * 1024)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup() => _data = CreateData(Length);

    [Benchmark]
    public void HashData() => Blake2b.HashData(_data, _digest);

    internal static byte[] CreateData(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 31) ^ (i >> 7));
        }

        return data;
    }
}
