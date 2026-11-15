using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.Dng;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// DNG files read by the Adobe DNG SDK (<c>dng_validate</c>, the reference implementation of the format) and LibRaw
/// (<c>unprocessed_raw</c>, which reads neither Deflate nor packed samples), and files the DNG SDK writes read by the library:
/// <list type="bullet">
/// <item><description>the stages before the demosaicing are exact: the stored samples the library gives (<see cref="DngRawImage.CopyStoredRow"/>) are those of the DNG SDK (stage 1) and of LibRaw, and its linear values (<see cref="DngRawImage.CopyLinearRow"/>) are those of the DNG SDK (stage 2) to within its 16-bit rounding;</description></item>
/// <item><description>the rendering agrees with the one of the DNG SDK (made colorimetric by the identity tone curve of the scenes) within a tolerance: the demosaicing algorithms differ;</description></item>
/// <item><description>the files the DNG SDK rewrites (lossless JPEG tiles, JPEG XL tiles of interleaved fields, lossy JPEG XL proxies) render like their source, or like the DNG SDK renders them.</description></item>
/// </list>
/// </summary>
public sealed class DngInteropTests
{
    // The scenes whose stage 2 differs from the DNG SDK, as the corpus records: it repairs bad pixels with its own
    // interpolation, rounds the deltas of the black levels, and leaves DeltaPerRow and DeltaPerColumn unapplied
    private static readonly HashSet<string> Stage2Differs = new(StringComparer.Ordinal) { "opcodes-list1", "opcodes-list2", "black-level-pattern" };

    // The scenes the rendering of the DNG SDK is not compared with: an orientation it applies, a monochrome image it renders
    // in gray gamma 2.2, highlights its exposure rolls off, and the larger windows of the 6x6 pattern, which it interpolates
    // with another algorithm
    private static readonly HashSet<string> RenderingNotCompared = new(StringComparer.Ordinal) { "thumbnail-exif-xmp", "monochrome", "baseline-exposure", "cfa-6x6", "cfa-cmyg", "opcodes-list1", "opcodes-list2", "opcodes-list3" };

    public static TheoryData<string> Scenes => [.. DngScenes.All.Select(scene => scene.Name)];

    [Theory]
    [MemberData(nameof(Scenes))]
    public async Task TheStagesBeforeTheDemosaicingAreThoseOfTheDngSdk(string name)
    {
        var tool = await RequiredTools.DngSdkAsync();
        var builder = DngScenes.Get(name);
        var path = await WriteAsync(name, builder.Build());
        using var raw = DngRawImage.Load(await File.ReadAllBytesAsync(path, XunitCancellationToken));

        var (width1, height1, samples1, stage1) = await tool.GetStageAsync(path, 1, XunitCancellationToken);
        Assert.Equal((raw.Size.Width, raw.Size.Height, raw.SamplesPerPixel), (width1, height1, samples1));
        if (builder.OpcodeList1 is null)
        {
            var row = new float[width1 * samples1];
            for (var y = 0; y < height1; y++)
            {
                raw.CopyStoredRow(y, row);
                for (var i = 0; i < row.Length; i++)
                {
                    Assert.Equal(stage1[(y * row.Length) + i], (double)row[i]);
                }
            }
        }

        var (width2, height2, samples2, stage2) = await tool.GetStageAsync(path, 2, XunitCancellationToken);
        Assert.Equal((raw.ActiveArea.Width, raw.ActiveArea.Height, raw.SamplesPerPixel), (width2, height2, samples2));
        var linear = new float[width2 * samples2];
        var max = 0.0;
        for (var y = 0; y < height2; y++)
        {
            raw.CopyLinearRow(y, linear);
            for (var i = 0; i < linear.Length; i++)
            {
                var sdk = stage2[(y * linear.Length) + i];
                max = Math.Max(max, raw.IsFloatingPoint ? Math.Abs(linear[i] - sdk) * 65535 : Math.Abs((linear[i] * 65535.0) - sdk));
            }
        }

        if (Stage2Differs.Contains(name))
        {
            Assert.True(max > 0.501, $"{name}: the stage 2 of the DNG SDK now agrees (max {max.ToString(CultureInfo.InvariantCulture)}): remove the scene from Stage2Differs and from the notes of the corpus.");
        }
        else
        {
            // The rounding of the 16-bit samples of the DNG SDK, and the single precision of the library
            Assert.True(max <= 0.501, $"{name}: the linear values differ from the stage 2 of the DNG SDK by {max.ToString(CultureInfo.InvariantCulture)} units of 1/65535.");
        }
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public async Task TheRenderingAgreesWithTheDngSdk(string name)
    {
        if (RenderingNotCompared.Contains(name))
            return;

        var tool = await RequiredTools.DngSdkAsync();
        var data = DngScenes.Get(name).Build();
        var path = await WriteAsync(name, data);
        var (width, height, samples, rendering) = await tool.RenderAsync(path, XunitCancellationToken);
        using var image = Image.Load<RgbaFloat>(data);
        Assert.Equal((image.Width, image.Height, 3), (width, height, samples));
        var pixels = new RgbaFloat[width * height];
        image.Frames[0].CopyPixelDataTo(pixels);
        double max = 0, sum = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            foreach (var (value, channel) in new[] { (pixels[i].R, 0), (pixels[i].G, 1), (pixels[i].B, 2) })
            {
                var difference = Math.Abs(EncodeSrgb(value) - rendering[(i * 3) + channel]) * 255;
                max = Math.Max(max, difference);
                sum += difference;
            }
        }

        // Measured on the scenes (2026-11-15): max 0.4 to 10.1, mean 0.008 to 0.22 (8-bit units, sRGB-encoded); the
        // differences are at the edges and in the gradients, where the demosaicing algorithms differ
        var mean = sum / (pixels.Length * 3);
        Assert.True(max <= 12 && mean <= 0.3, $"{name}: the rendering differs from the one of the DNG SDK by max {max.ToString(CultureInfo.InvariantCulture)}, mean {mean.ToString(CultureInfo.InvariantCulture)} (8-bit units).");
    }

    [Theory]
    [InlineData("bayer-rggb")]
    [InlineData("bayer-gbrg")]
    [InlineData("tiles")]
    [InlineData("active-area-crop")]
    [InlineData("cfa-6x6")]
    public async Task LibRawReadsTheStoredSamples(string name)
    {
        var tool = await RequiredTools.DngSdkAsync();
        var data = DngScenes.Get(name).Build();
        var path = await WriteAsync(name, data);
        var (width, height, samples) = await tool.GetLibRawSamplesAsync(path, XunitCancellationToken);
        using var raw = DngRawImage.Load(data);
        Assert.Equal((raw.Size.Width, raw.Size.Height), (width, height));
        var row = new float[width];
        for (var y = 0; y < height; y++)
        {
            raw.CopyStoredRow(y, row);
            Assert.Equal(samples.AsSpan(y * width, width).ToArray().Select(value => (float)value), row);
        }
    }

    [Theory]
    [InlineData("bayer-rggb", "")]
    [InlineData("bayer-rggb", "-losslessJXL")]
    [InlineData("linear-raw-rgb", "")]
    [InlineData("linear-raw-rgb", "-losslessJXL")]
    [InlineData("deflate-tiles", "")]
    [InlineData("8-bit", "-losslessJXL")]
    public async Task FilesTheDngSdkRewritesRenderLikeTheirSource(string name, string option)
    {
        var tool = await RequiredTools.DngSdkAsync();
        var data = DngScenes.Get(name).Build();
        var path = await WriteAsync(name, data);
        var rewritten = await tool.RewriteAsync(path, name + "-rewritten" + option, option.Length == 0 ? [] : [option], XunitCancellationToken);
        using var expected = Image.Load<RgbaFloat>(data);
        using var actual = Image.Load<RgbaFloat>(rewritten);
        Assert.Equal(expected.Size, actual.Size);
        var a = new RgbaFloat[expected.Width * expected.Height];
        var b = new RgbaFloat[a.Length];
        expected.Frames[0].CopyPixelDataTo(a);
        actual.Frames[0].CopyPixelDataTo(b);

        // The DNG SDK writes the rationals of the camera profile with its own denominators
        for (var i = 0; i < a.Length; i++)
        {
            Assert.True(Math.Abs(a[i].R - b[i].R) < 1e-4 && Math.Abs(a[i].G - b[i].G) < 1e-4 && Math.Abs(a[i].B - b[i].B) < 1e-4, $"{name} {option}: pixel {i} differs.");
        }
    }

    [Theory]
    [InlineData("bayer-rggb")]
    [InlineData("linear-raw-rgb")]
    public async Task LossyProxiesOfTheDngSdkDecodeLikeItReadsThem(string name)
    {
        // A proxy is a linear raw image of a smaller size coded with lossy JPEG XL, and an OpcodeList2
        var tool = await RequiredTools.DngSdkAsync();
        var path = await WriteAsync(name, DngScenes.Get(name).Build());
        var proxy = await tool.RewriteAsync(path, name + "-proxy", ["-proxy", "32"], XunitCancellationToken);
        var (width, height, samples, stage2) = await tool.GetStageAsync(proxy, 2, XunitCancellationToken);
        using var raw = DngRawImage.Load(await File.ReadAllBytesAsync(proxy, XunitCancellationToken));
        Assert.False(raw.IsColorFilterArray);
        Assert.Equal((raw.ActiveArea.Width, raw.ActiveArea.Height, raw.SamplesPerPixel), (width, height, samples));
        var linear = new float[width * samples];
        var max = 0.0;
        for (var y = 0; y < height; y++)
        {
            raw.CopyLinearRow(y, linear);
            for (var i = 0; i < linear.Length; i++)
            {
                max = Math.Max(max, Math.Abs((linear[i] * 65535.0) - stage2[(y * linear.Length) + i]));
            }
        }

        // Lossy JPEG XL is decoded in floating point: the library and libjxl agree to a few units of 1/65535 (measured: 1.2)
        Assert.True(max <= 4, $"{name}: the proxy decodes to linear values that differ from the DNG SDK by {max.ToString(CultureInfo.InvariantCulture)} units of 1/65535.");
    }

    private static double EncodeSrgb(double value)
    {
        value = Math.Clamp(value, 0, 1);
        return value <= 0.0031308 ? 12.92 * value : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
    }

    private static async Task<FullPath> WriteAsync(string name, byte[] data)
    {
        var path = InteropSettings.GetArtifactsDirectory("DngInteropTests") / (name + ".dng");
        await File.WriteAllBytesAsync(path, data, XunitCancellationToken);
        return path;
    }
}
