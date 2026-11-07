using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// Histograms, tone operations, binarization and effects on 1920x1080 sources with transparency. Each in-place operation
/// clones the source first so it can be repeated; <see cref="Clone"/> measures that copy alone.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class ToneEffectBenchmarks
{
    private static readonly Rgba64 Background = new(0x1234, 0xABCD, 0x7F7F, ushort.MaxValue);

    private Image<Rgba32> _rgba32 = null!;
    private Image<Rgba64> _rgba64 = null!;
    private Image<Rgb24> _rgb24 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _rgba32 = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _rgba64 = Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        using var opaque = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32OpaqueSource, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _rgb24 = opaque.CloneAs<Rgb24>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _rgba32?.Dispose();
        _rgba64?.Dispose();
        _rgb24?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [Workload("1920x1080 Rgba32 clone only (no operation)")]
    public int Clone()
    {
        using var copy = _rgba32.Clone();
        return copy.Width;
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 luminance histogram")]
    public long HistogramLuminance32() => _rgba32.ComputeHistogram(HistogramChannel.Luminance).GetOtsuThreshold() > 0 ? 1 : 0;

    [Benchmark]
    [Workload("1920x1080 Rgba32 red histogram")]
    public long HistogramRed32() => _rgba32.ComputeHistogram(HistogramChannel.Red).TotalCount;

    [Benchmark]
    [Workload("1920x1080 Rgb24 luminance histogram")]
    public long HistogramLuminance24() => _rgb24.ComputeHistogram(HistogramChannel.Luminance).TotalCount;

    [Benchmark]
    [Workload("1920x1080 Rgba64 luminance histogram")]
    public long HistogramLuminance64() => _rgba64.ComputeHistogram(HistogramChannel.Luminance).TotalCount;

    [Benchmark]
    [Workload("1920x1080 Rgba32 global histogram equalization")]
    public int Equalize32() => Apply(_rgba32, static image => image.Equalize());

    [Benchmark]
    [Workload("1920x1080 Rgba32 adaptive histogram equalization (8x8 tiles)")]
    public int EqualizeAdaptive32() => Apply(_rgba32, static image => image.Equalize(new HistogramEqualizationOptions { Mode = HistogramEqualizationMode.Adaptive }));

    [Benchmark]
    [Workload("1920x1080 Rgba32 auto levels")]
    public int AutoLevels32() => Apply(_rgba32, static image => image.AutoLevels());

    [Benchmark]
    [Workload("1920x1080 Rgba64 auto levels")]
    public int AutoLevels64() => Apply(_rgba64, static image => image.AutoLevels());

    [Benchmark]
    [Workload("1920x1080 Rgba32 Otsu threshold")]
    public int ThresholdOtsu32() => Apply(_rgba32, static image => image.Threshold());

    [Benchmark]
    [Workload("1920x1080 Rgba32 fixed threshold")]
    public int ThresholdFixed32() => Apply(_rgba32, static image => image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, Level = 0.5 }));

    [Benchmark]
    [Workload("1920x1080 Rgb24 fixed threshold")]
    public int ThresholdFixed24() => Apply(_rgb24, static image => image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Fixed, Level = 0.5 }));

    [Benchmark]
    [Workload("1920x1080 Rgba32 mean adaptive threshold (window 15)")]
    public int ThresholdMean32() => Apply(_rgba32, static image => image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Mean, WindowSize = 15 }));

    [Benchmark]
    [Workload("1920x1080 Rgba32 Sauvola adaptive threshold (window 15)")]
    public int ThresholdSauvola32() => Apply(_rgba32, static image => image.Threshold(new ThresholdOptions { Method = ThresholdMethod.Sauvola, WindowSize = 15 }));

    [Benchmark]
    [Workload("1920x1080 Rgba32 grayscale")]
    public int Grayscale32() => Apply(_rgba32, static image => image.Grayscale());

    [Benchmark]
    [Workload("1920x1080 Rgba32 replace color (tolerance 0.1)")]
    public int ReplaceColor32() => Apply(_rgba32, static image => image.ReplaceColor(new Rgba64(0x8080, 0x8080, 0x8080, ushort.MaxValue), Background, 0.1));

    [Benchmark]
    [Workload("1920x1080 Rgba64 replace color (tolerance 0.1)")]
    public int ReplaceColor64() => Apply(_rgba64, static image => image.ReplaceColor(new Rgba64(0x8080, 0x8080, 0x8080, ushort.MaxValue), Background, 0.1));

    [Benchmark]
    [Workload("1920x1080 Rgba32 flatten")]
    public int Flatten32() => Apply(_rgba32, static image => image.Flatten(Background));

    [Benchmark]
    [Workload("1920x1080 Rgba64 flatten")]
    public int Flatten64() => Apply(_rgba64, static image => image.Flatten(Background));

    [Benchmark]
    [Workload("1920x1080 Rgba32 pixelate (8 px blocks)")]
    public int Pixelate32() => Apply(_rgba32, static image => image.Pixelate(8));

    [Benchmark]
    [Workload("1920x1080 Rgb24 pixelate (8 px blocks)")]
    public int Pixelate24() => Apply(_rgb24, static image => image.Pixelate(8));

    [Benchmark]
    [Workload("1920x1080 Rgba32 oil paint (radius 3, 20 levels)")]
    public int OilPaint32() => Apply(_rgba32, static image => image.OilPaint(20, 3));

    [Benchmark]
    [Workload("1920x1080 Rgba32 sepia color matrix")]
    public int Sepia32() => Apply(_rgba32, static image => image.ApplyColorMatrix(ColorMatrix.CreateSepia(1)));

    [Benchmark]
    [Workload("1920x1080 Rgba32 vignette")]
    public int Vignette32() => Apply(_rgba32, static image => image.Vignette(new Rgba64(0, 0, 0, ushort.MaxValue), 800, 450));

    [Benchmark]
    [Workload("1920x1080 Rgba32 glow")]
    public int Glow32() => Apply(_rgba32, static image => image.Glow(new Rgba64(ushort.MaxValue, ushort.MaxValue, 0, ushort.MaxValue), 800, 450));

    [Benchmark]
    [Workload("1920x1080 Rgba32 tone curve (levels then a curve through 4 points)")]
    public int ToneCurve32() => Apply(_rgba32, static image => image.ApplyToneCurve(LevelsAndCurve));

    [Benchmark]
    [Workload("1920x1080 Rgba64 tone curve (levels then a curve through 4 points)")]
    public int ToneCurve64() => Apply(_rgba64, static image => image.ApplyToneCurve(LevelsAndCurve));

    [Benchmark]
    [Workload("1920x1080 Rgba32 3D LUT of 33 points, tetrahedral")]
    public int Lut3DTetrahedral32() => Apply(_rgba32, static image => image.ApplyLut(Grade33));

    [Benchmark]
    [Workload("1920x1080 Rgba32 3D LUT of 33 points, trilinear")]
    public int Lut3DTrilinear32() => Apply(_rgba32, static image => image.ApplyLut(Grade33, LutInterpolation.Trilinear));

    [Benchmark]
    [Workload("1920x1080 Rgba64 3D LUT of 33 points, tetrahedral")]
    public int Lut3DTetrahedral64() => Apply(_rgba64, static image => image.ApplyLut(Grade33));

    private static readonly ToneCurve LevelsAndCurve = ToneCurve.CreateLevels(0.05, 0.95, 1.1).Then(ToneCurve.CreateFromPoints(new PointD(0, 0), new PointD(0.25, 0.2), new PointD(0.75, 0.8), new PointD(1, 1)));

    private static readonly CubeLut Grade33 = CreateGrade(33);

    /// <summary>A nonlinear grade of <paramref name="size"/>^3 points, as a .cube file holds it.</summary>
    private static CubeLut CreateGrade(int size)
    {
        var text = new System.Text.StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"LUT_3D_SIZE {size}\n");
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    double red = r / (size - 1.0), green = g / (size - 1.0), blue = b / (size - 1.0);
                    text.Append(CultureInfo.InvariantCulture, $"{0.5 + (0.45 * Math.Sin((3 * red) + green - blue)):F6} {((red * green) + (blue * blue)) / 2:F6} {0.9 * Math.Sqrt(blue) * (1 - (0.3 * red)):F6}\n");
                }
            }
        }

        return CubeLut.Parse(text.ToString());
    }

    private static int Apply<TPixel>(Image<TPixel> source, Action<Image<TPixel>> operation)
        where TPixel : unmanaged
    {
        using var copy = source.Clone();
        operation(copy);
        return copy.Width;
    }
}
