using BenchmarkDotNet.Attributes;
using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingBenchmarks;

/// <summary>
/// Measures <see cref="ImageComparer"/> on byte-identical images (every passing image snapshot test)
/// and on images that actually differ.
/// </summary>
[MemoryDiagnoser]
public class ImageComparerBenchmark
{
    private readonly ImageComparer _comparer = new();
    private SnapshotData _expected = null!;
    private SnapshotData _identical = null!;
    private SnapshotData _different = null!;

    [Params(64, 512)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var expected = PngImageEncoder.Encode(ImageBenchmarkData.CreateImage(Size, Size, seed: 0));
        _expected = new SnapshotData("png", expected);
        _identical = new SnapshotData("png", [.. expected]);
        _different = new SnapshotData("png", PngImageEncoder.Encode(ImageBenchmarkData.CreateImage(Size, Size, seed: 1)));
    }

    [Benchmark]
    public bool IdenticalBytes() => _comparer.Equals(_expected, _identical);

    [Benchmark]
    public bool DifferentBytes() => _comparer.Equals(_expected, _different);
}
