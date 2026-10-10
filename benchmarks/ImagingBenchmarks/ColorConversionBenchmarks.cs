using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// ICC color conversion of 1920x1080 pixels between the profiles provided by the library: matrix-based RGB and monochrome
/// profiles with the sRGB parametric curve or no curve (linear light), as sample buffers and as an image operation.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class ColorConversionBenchmarks
{
    private byte[] _rgb8 = null!;
    private ushort[] _rgb16 = null!;
    private byte[] _converted8 = null!;
    private ushort[] _converted16 = null!;
    private byte[] _gray8 = null!;
    private Image<Rgba32> _rgba32 = null!;
    private IccColorTransform _srgbToLinear = null!;
    private IccColorTransform _linearToSrgb = null!;
    private IccColorTransform _srgbToGray = null!;

    [GlobalSetup]
    public void Setup()
    {
        var pixels = BenchmarkInputs.ResizeWidth * BenchmarkInputs.ResizeHeight;
        _rgb8 = new byte[pixels * 3];
        _rgb16 = new ushort[pixels * 3];
        var state = 12345u;
        for (var i = 0; i < _rgb8.Length; i++)
        {
            state = (state * 1664525) + 1013904223;
            _rgb8[i] = (byte)(state >> 24);
            _rgb16[i] = (ushort)(state >> 16);
        }

        _converted8 = new byte[_rgb8.Length];
        _converted16 = new ushort[_rgb16.Length];
        _gray8 = new byte[pixels];
        _rgba32 = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _srgbToLinear = IccColorTransform.Create(IccProfile.Srgb, BuiltInIccProfiles.LinearSrgb);
        _linearToSrgb = IccColorTransform.Create(BuiltInIccProfiles.LinearSrgb, IccProfile.Srgb);
        _srgbToGray = IccColorTransform.Create(IccProfile.Srgb, IccProfile.SrgbGray);
    }

    [GlobalCleanup]
    public void Cleanup() => _rgba32?.Dispose();

    [Benchmark(Baseline = true)]
    [Workload("1920x1080 RGB 8-bit, sRGB to linear sRGB (decoding curve from tables)")]
    public int SrgbToLinear8()
    {
        _srgbToLinear.Convert(_rgb8, _converted8);
        return _converted8[0];
    }

    [Benchmark]
    [Workload("1920x1080 RGB 8-bit, linear sRGB to sRGB (encoding curve evaluated)")]
    public int LinearToSrgb8()
    {
        _linearToSrgb.Convert(_rgb8, _converted8);
        return _converted8[0];
    }

    [Benchmark]
    [Workload("1920x1080 RGB 16-bit, sRGB to linear sRGB (decoding curve evaluated)")]
    public int SrgbToLinear16()
    {
        _srgbToLinear.Convert(_rgb16, _converted16);
        return _converted16[0];
    }

    [Benchmark]
    [Workload("1920x1080 RGB 8-bit to gray 8-bit, sRGB to sGray")]
    public int SrgbToGray8()
    {
        _srgbToGray.Convert(_rgb8, _gray8);
        return _gray8[0];
    }

    [Benchmark]
    [Workload("1920x1080 Rgba32 image, linear-light pixels to sRGB (transactional, alpha copied)")]
    public int ImageLinearToSrgb()
    {
        using var copy = _rgba32.Clone();
        copy.Metadata.TransferFunction = ColorTransferFunction.Linear;
        copy.ConvertColorProfile(IccProfile.Srgb);
        return copy.Width;
    }
}
