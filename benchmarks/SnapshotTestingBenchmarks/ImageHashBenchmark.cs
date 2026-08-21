using BenchmarkDotNet.Attributes;
using Meziantou.Framework.SnapshotTesting;

namespace SnapshotTestingBenchmarks;

[MemoryDiagnoser]
public class ImageHashBenchmark
{
    private Image _image = null!;

    [Params(32, 1920)]
    public int Width { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var height = Width == 32 ? 32 : 1080;
        _image = ImageBenchmarkData.CreateImage(Width, height, seed: 0);
    }

    [Benchmark]
    public ulong DHash()
    {
        return ImageHash.ComputeDHash(_image);
    }

    [Benchmark]
    public ulong PHash()
    {
        return ImageHash.ComputePHash(_image);
    }
}
