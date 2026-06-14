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
        var pixels = new Argb[checked(Width * height)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var red = (byte)((x * 17 + y * 31) % 256);
                var green = (byte)((x * 7 + y * 13) % 256);
                var blue = (byte)((x * 3 + y * 5) % 256);
                pixels[y * Width + x] = new Argb(255, red, green, blue);
            }
        }

        _image = Image.Create(Width, height, pixels);
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
