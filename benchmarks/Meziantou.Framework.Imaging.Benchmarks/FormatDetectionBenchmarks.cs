using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>Baseline benchmark of the content-based format detection.</summary>
[MemoryDiagnoser]
public class FormatDetectionBenchmarks
{
    private readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly byte[] _jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];
    private readonly byte[] _unknown = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];

    [Benchmark(Baseline = true)]
    public ImageFormat Png() => Image.DetectFormat(_png);

    [Benchmark]
    public ImageFormat Jpeg() => Image.DetectFormat(_jpeg);

    [Benchmark]
    public ImageFormat Unknown() => Image.DetectFormat(_unknown);
}
