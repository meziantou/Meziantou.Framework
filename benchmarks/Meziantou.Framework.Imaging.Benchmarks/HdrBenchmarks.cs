using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// High dynamic range and wide-gamut color: Ultra HDR JPEG files (a 2048x1536 base image with a 512x384 gain map), the
/// tone mapping of 1920x1080 PQ and HLG pixels to sRGB, and the conversion of Display P3 photos to sRGB. Each in-place
/// conversion clones the source first so it can be repeated.
/// </summary>
[Config(typeof(WorkloadConfig))]
public class HdrBenchmarks : IDisposable
{
    private static readonly IccColorTransformOptions Bt2390 = new() { ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Bt2390 } };
    private static readonly IccColorTransformOptions Hable = new() { ToneMapping = new HdrToneMappingOptions { Operator = HdrToneMappingOperator.Hable } };
    private static readonly IccColorTransformOptions ReduceChroma = new() { GamutMapping = GamutMapping.ReduceChroma };

    private readonly MemoryStream _output = new();
    private Image<Rgb24> _base = null!;
    private Image<Gray8> _gainMap = null!;
    private Image<Rgba64> _pq = null!;
    private Image<Rgba64> _hlg = null!;
    private Image<Rgba32> _displayP3 = null!;
    private JpegEncoder _ultraHdrEncoder = null!;

    [GlobalSetup]
    public void Setup()
    {
        _base = Image.ImportPixelData<Rgb24>(BenchmarkInputs.LargeRgb24Source, BenchmarkInputs.LargeWidth, BenchmarkInputs.LargeHeight);
        _gainMap = BenchmarkInputs.CreateGainMap();
        _ultraHdrEncoder = new JpegEncoder { Quality = BenchmarkInputs.JpegQuality, ChromaSubsampling = JpegChromaSubsampling.Ratio420, GainMap = new GainMap(_gainMap, BenchmarkInputs.UltraHdrGainMapMetadata) };

        // BT.2020 primaries, full-range RGB samples, with the PQ (16) and HLG (18) transfer characteristics
        _pq = Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64OpaqueSource, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _pq.Metadata.Cicp = new CicpColorDescription(colorPrimaries: 9, transferCharacteristics: 16, matrixCoefficients: 0, fullRange: true);
        _pq.Metadata.ContentLightLevel = new ContentLightLevel(maxContentLightLevel: 1000, maxPictureAverageLightLevel: 400);
        _hlg = Image.ImportPixelData<Rgba64>(BenchmarkInputs.ResizeRgba64OpaqueSource, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _hlg.Metadata.Cicp = new CicpColorDescription(colorPrimaries: 9, transferCharacteristics: 18, matrixCoefficients: 0, fullRange: true);

        _displayP3 = Image.ImportPixelData<Rgba32>(BenchmarkInputs.ResizeRgba32Source, BenchmarkInputs.ResizeWidth, BenchmarkInputs.ResizeHeight);
        _displayP3.Metadata.IccProfile = IccProfile.DisplayP3;

        // Untimed correctness gate: the file must carry the gain map that is rendered
        if (AuxiliaryImageCollection.Load(BenchmarkInputs.UltraHdrJpeg).GainMapImage?.GainMapMetadata is null)
            throw new InvalidOperationException("The Ultra HDR input has no gain map with known parameters.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _base?.Dispose();
        _gainMap?.Dispose();
        _pq?.Dispose();
        _hlg?.Dispose();
        _displayP3?.Dispose();
    }

    [Benchmark]
    [Workload("Ultra HDR JPEG 2048x1536: list the auxiliary images (gain map and its parameters)")]
    public int ListGainMap() => AuxiliaryImageCollection.Load(BenchmarkInputs.UltraHdrJpeg).Count;

    [Benchmark]
    [Workload("Ultra HDR JPEG 2048x1536 -> RgbaFloat linear light for a display of 2 stops of headroom")]
    public int DecodeUltraHdr()
    {
        using var image = AuxiliaryImageCollection.Load(BenchmarkInputs.UltraHdrJpeg).DecodeHdr(hdrHeadroom: 2);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 base + 512x384 Gray8 gain map -> RgbaFloat for 2 stops of headroom (decoded images)")]
    public int ApplyGainMap()
    {
        using var image = BenchmarkInputs.UltraHdrGainMapMetadata.Apply(_base, _gainMap, hdrHeadroom: 2);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 + 512x384 Gray8 gain map -> Ultra HDR JPEG q85 4:2:0", ReturnsOutputBytes = true)]
    public long EncodeUltraHdr() => ImageWorkloads.Encode(_base, _ultraHdrEncoder, _output);

    [Benchmark]
    [Workload("1920x1080 Rgba64 BT.2020 PQ (1000 cd/m2 peak) -> sRGB, BT.2390 tone mapping")]
    public int ToneMapPq() => Apply(_pq, IccProfile.Srgb, Bt2390);

    [Benchmark]
    [Workload("1920x1080 Rgba64 BT.2020 PQ (1000 cd/m2 peak) -> sRGB, Hable tone mapping")]
    public int ToneMapPqHable() => Apply(_pq, IccProfile.Srgb, Hable);

    [Benchmark]
    [Workload("1920x1080 Rgba64 BT.2020 HLG -> sRGB, BT.2390 tone mapping")]
    public int ToneMapHlg() => Apply(_hlg, IccProfile.Srgb, Bt2390);

    [Benchmark]
    [Workload("1920x1080 Rgba32 Display P3 -> sRGB, gamut clipped")]
    public int DisplayP3ToSrgb() => Apply(_displayP3, IccProfile.Srgb, options: null);

    [Benchmark]
    [Workload("1920x1080 Rgba32 Display P3 -> sRGB, chroma reduced into the gamut")]
    public int DisplayP3ToSrgbReduceChroma() => Apply(_displayP3, IccProfile.Srgb, ReduceChroma);

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }

    private static int Apply(Image source, IccProfile profile, IccColorTransformOptions? options)
    {
        using var result = ImageWorkloads.FilterCopy(source, image => image.ConvertColorProfile(profile, options));
        return result.Width;
    }
}
