using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>Aliased scanline scaling with path complexity, canvas width and forward or reverse crossing order.</summary>
[Config(typeof(WorkloadConfig))]
public class AliasedRasterizationBenchmarks : IDisposable
{
    private Image<Gray8> _image = null!;
    private VectorPath _path = null!;

    [Params(32, 256, 2048)]
    public int Contours { get; set; }

    [Params(256, 1920)]
    public int Width { get; set; }

    [Params(false, true)]
    public bool Reverse { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _image = new Image<Gray8>(Width, 128);
        var builder = new VectorPathBuilder();
        for (var i = 0; i < Contours; i++)
        {
            var index = Reverse ? Contours - 1 - i : i;
            var left = (double)index * Width / Contours;
            builder.AddRectangle(new RectangleD(left, 0, (double)Width / (2 * Contours), 128));
        }

        _path = builder.Build();
    }

    [GlobalCleanup]
    public void Cleanup() => Dispose();

    public void Dispose()
    {
        _image?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark]
    [Workload("Aliased fill of disjoint full-height rectangles on a 128-row Gray8 canvas")]
    public int Fill()
    {
        _image.Draw(_path, (canvas, path) => canvas.Antialiasing(enabled: false, path, (hard, shape) => hard.Fill(new Rgba32(255, 255, 255), shape)));
        return _image.Width;
    }
}
