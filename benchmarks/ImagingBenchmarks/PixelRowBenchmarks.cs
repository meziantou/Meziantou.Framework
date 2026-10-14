using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Typed row loops with static callbacks on a 1920x1080 Rgba32 frame: steady state must not allocate per row. The
/// <c>Convert*</c> workloads convert 1920x1080 frames between pixel formats (<see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>).
/// </summary>
[Config(typeof(WorkloadConfig))]
public class PixelRowBenchmarks
{
    private static readonly PixelConversionOptions Flatten = new() { BackgroundColor = new Rgba64(0x1234, 0xABCD, 0x7F7F, ushort.MaxValue) };

    private readonly ImageWorkloads.SumState _state = new();
    private Image<Rgba32> _image = null!;
    private Image<Rgba64> _rgba64 = null!;
    private Image<Rgb24> _rgb24 = null!;
    private Image<Rgba32> _framed = null!;
    private Image<Rgba64> _framed64 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _image = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _rgba64 = Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _rgb24 = _image.CloneAs<Rgb24>(Flatten);

        // The content inside a uniform white frame of 100 pixels: the automatic crop finds a border and weighs the content
        var framed = BenchmarkInputs.ResizeRgba32Source;
        for (var y = 0; y < BenchmarkInputs.ResizeHeight; y++)
        {
            for (var x = 0; x < BenchmarkInputs.ResizeWidth; x++)
            {
                if (x < 100 || y < 100 || x >= BenchmarkInputs.ResizeWidth - 100 || y >= BenchmarkInputs.ResizeHeight - 100)
                {
                    framed[(y * BenchmarkInputs.ResizeWidth) + x] = new Rgba32(255, 255, 255);
                }
            }
        }

        _framed = Image.ImportPixelData<Rgba32>(framed, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _framed64 = _framed.CloneAs<Rgba64>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _image?.Dispose();
        _rgba64?.Dispose();
        _rgb24?.Dispose();
        _framed?.Dispose();
        _framed64?.Dispose();
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32: invert colors, static ProcessPixelRows callback")]
    public void Invert() => ImageWorkloads.InvertRows(_image.Frames[0]);

    [Benchmark]
    [Workload("1920x1080 Rgba32: sum green, static callback with state")]
    public long SumGreen() => ImageWorkloads.SumGreen(_image.Frames[0], _state);

    [Benchmark]
    [Workload("1920x1080 Rgba64 -> Rgba32 (16 to 8 bits)")]
    public int ConvertRgba64ToRgba32() => Convert<Rgba64, Rgba32>(_rgba64, options: null);

    [Benchmark]
    [Workload("1920x1080 Rgb24 -> Rgba64 (8 to 16 bits)")]
    public int ConvertRgb24ToRgba64() => Convert<Rgb24, Rgba64>(_rgb24, options: null);

    [Benchmark]
    [Workload("1920x1080 Rgb24 -> Gray8 (luma)")]
    public int ConvertRgb24ToGray8() => Convert<Rgb24, Gray8>(_rgb24, options: null);

    [Benchmark]
    [Workload("1920x1080 Rgba32 with transparency -> Rgb24 (flattened onto a background)")]
    public int ConvertRgba32ToRgb24() => Convert<Rgba32, Rgb24>(_image, Flatten);

    [Benchmark]
    [Workload("1920x1080 Rgba32 with transparency -> Gray8 (flattened onto a background, luma)")]
    public int ConvertRgba32ToGray8() => Convert<Rgba32, Gray8>(_image, Flatten);

    [Benchmark]
    [Workload("1920x1080 Rgba64 with transparency -> Rgb24 (flattened onto a background, 16 to 8 bits)")]
    public int ConvertRgba64ToRgb24() => Convert<Rgba64, Rgb24>(_rgba64, Flatten);

    [Benchmark]
    [Workload("1920x1080 Rgba64 with transparency -> Gray16 (flattened onto a background, luma)")]
    public int ConvertRgba64ToGray16() => Convert<Rgba64, Gray16>(_rgba64, Flatten);

    [Benchmark]
    [Workload("1920x1080 Rgba32: clone, then grayscale in place")]
    public int GrayscaleRgba32()
    {
        using var copy = _image.Clone();
        copy.Grayscale();
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgb24: clone, then grayscale in place")]
    public int GrayscaleRgb24()
    {
        using var copy = _rgb24.Clone();
        copy.Grayscale();
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32: clone, then rotate by 90 degrees")]
    public int Rotate90Rgba32()
    {
        using var copy = _image.Clone();
        copy.Rotate(RotateMode.Rotate90);
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba64: clone, then rotate by 270 degrees")]
    public int Rotate270Rgba64()
    {
        using var copy = _rgba64.Clone();
        copy.Rotate(RotateMode.Rotate270);
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 in a white frame: automatic crop analysis with content weights")]
    public double AnalyzeAutoCropWeights() => _framed.AnalyzeAutoCrop(new AutoCropOptions { AnalyzeWeights = true }).WeightX;

    [Benchmark]
    [Workload("1920x1080 Rgba64 in a white frame: automatic crop analysis with content weights")]
    public double AnalyzeAutoCropWeights16() => _framed64.AnalyzeAutoCrop(new AutoCropOptions { AnalyzeWeights = true }).WeightX;

    private static int Convert<TSource, TDestination>(Image<TSource> image, PixelConversionOptions? options)
        where TSource : unmanaged
        where TDestination : unmanaged
    {
        using var result = image.CloneAs<TDestination>(options);
        return result.Width;
    }
}
