using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Benchmarks;

public class ChecksumBenchmarks
{
    private byte[] _data = [];

    [Params(64, 1024, 1024 * 1024)]
    public int Length { get; set; }

    [GlobalSetup]
    public void Setup() => _data = Blake2bBenchmarks.CreateData(Length);

    [Benchmark]
    public uint Crc32() => Hashing.Crc32.Compute(_data);

    [Benchmark]
    public uint Adler32() => Hashing.Adler32.Compute(_data);
}
