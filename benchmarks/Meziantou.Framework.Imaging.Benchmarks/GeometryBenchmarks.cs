using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Geometric operations on a 1920x1080 source with transparency: the exact ones (flip, crop, padding, EXIF orientation)
/// and the resampled ones (rotation by any angle, skew, perspective). Each in-place operation clones the source first so it
/// can be repeated; <see cref="Clone"/> measures that copy alone. Quarter turns are in <see cref="PixelRowBenchmarks"/>.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class GeometryBenchmarks
{
    private static readonly Rgba64 White = new(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);

    private Image<Rgba32> _rgba32 = null!;
    private Image<Rgba32> _rotatedPhoto = null!;
    private Image<Rgba32> _transposedPhoto = null!;

    /// <summary>Gets or sets <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> (1 is the default configuration).</summary>
    [Params(1, 4)]
    public int Workers { get; set; } = 1;

    [GlobalSetup]
    public void Setup()
    {
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = Workers };
        _rgba32 = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight, configuration: configuration);

        // A phone photo held in portrait: its pixels are stored sideways, with the orientation that turns them upright
        _rotatedPhoto = _rgba32.Clone();
        _rotatedPhoto.Metadata.Orientation = ExifOrientation.RightTop;
        _transposedPhoto = _rgba32.Clone();
        _transposedPhoto.Metadata.Orientation = ExifOrientation.LeftTop;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rgba32?.Dispose();
        _rotatedPhoto?.Dispose();
        _transposedPhoto?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [Workload("1920x1080 Rgba32 clone only (no operation)")]
    public int Clone()
    {
        using var copy = _rgba32.Clone();
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 horizontal flip")]
    public int FlipHorizontal() => Apply(_rgba32, image => image.Flip(FlipMode.Horizontal));

    [Benchmark]
    [Workload("1920x1080 Rgba32 vertical flip")]
    public int FlipVertical() => Apply(_rgba32, image => image.Flip(FlipMode.Vertical));

    [Benchmark]
    [Workload("1920x1080 Rgba32 crop to the centered 1280x720 area")]
    public int Crop() => Apply(_rgba32, image => image.Crop(new Rectangle(320, 180, 1280, 720)));

    [Benchmark]
    [Workload("1920x1080 Rgba32 padding of 64 white pixels on each side")]
    public int Pad() => Apply(_rgba32, image => image.Pad(64, 64, 64, 64, White));

    [Benchmark]
    [Workload("1920x1080 Rgba32 canvas extended to 1920x1920 (letterbox), centered")]
    public int ExtendCanvas() => Apply(_rgba32, image => image.ExtendCanvas(new Size(1920, 1920)));

    [Benchmark]
    [Workload("1920x1080 Rgba32 EXIF orientation 6 (quarter turn) applied")]
    public int AutoOrientRotate() => Apply(_rotatedPhoto, image => image.AutoOrient());

    [Benchmark]
    [Workload("1920x1080 Rgba32 EXIF orientation 5 (transpose) applied")]
    public int AutoOrientTranspose() => Apply(_transposedPhoto, image => image.AutoOrient());

    [Benchmark]
    [Workload("1920x1080 Rgba32 rotation by 3 degrees, bilinear, expanded canvas")]
    public int RotateBilinear() => Apply(_rgba32, image => image.Rotate(3, new TransformOptions { Filter = ResamplingFilter.Bilinear }));

    [Benchmark]
    [Workload("1920x1080 Rgba32 rotation by 3 degrees, bicubic, expanded canvas")]
    public int RotateBicubic() => Apply(_rgba32, image => image.Rotate(3, new TransformOptions { Filter = ResamplingFilter.Bicubic }));

    [Benchmark]
    [Workload("1920x1080 Rgba32 rotation by 30 degrees, bicubic, canvas kept")]
    public int RotateKeepCanvas() => Apply(_rgba32, image => image.Rotate(30, new TransformOptions { Filter = ResamplingFilter.Bicubic, CanvasMode = TransformCanvasMode.Keep }));

    [Benchmark]
    [Workload("1920x1080 Rgba32 skew by 10 degrees horizontally, bicubic")]
    public int Skew() => Apply(_rgba32, image => image.Skew(10, 0));

    [Benchmark]
    [Workload("1920x1080 Rgba32 perspective correction of a quadrilateral to 1600x900, bicubic")]
    public int CorrectPerspective() => Apply(_rgba32, image => image.CorrectPerspective(new PointD(180, 120), new PointD(1760, 60), new PointD(1840, 1020), new PointD(90, 960), new Size(1600, 900)));

    private static int Apply(Image source, Action<Image> operation)
    {
        using var result = ImageWorkloads.FilterCopy(source, operation);
        return result.Width;
    }
}
