using BenchmarkDotNet.Attributes;
using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingBenchmarks;

/// <summary>Measures PNG decoding, which dominates image snapshot comparisons that are not byte-identical.</summary>
[MemoryDiagnoser]
public class PngImageLoaderBenchmark
{
    private byte[] _png = null!;

    [Params(64, 512)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() => _png = PngImageEncoder.Encode(ImageBenchmarkData.CreateImage(Size, Size, seed: 0));

    [Benchmark]
    public int Load() => Image.Load(_png).Width;
}
