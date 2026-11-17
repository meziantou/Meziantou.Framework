using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Radiance;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Radiance HDR files exchanged with FFmpeg's independent Radiance codec (libavcodec <c>hdr</c>) and read by Apple ImageIO:
/// <list type="bullet">
/// <item><description>the output of the encoder, flat and run-length encoded, of every width around the limits of the new encoding, holds the RGBE pixels "Real Pixels" gives (<see cref="ReferenceRadiance"/>), and FFmpeg and ImageIO read them;</description></item>
/// <item><description>the files FFmpeg writes decode in the library to the RGBE pixels they store;</description></item>
/// <item><description>the standard-orientation files of the corpus decode in ImageIO to the pixels of the library.</description></item>
/// <item><description>the tools of the Radiance distribution (<see cref="RadianceTool"/>) read the output of the encoder as the library does, bit for bit; the files <c>pvalue</c> writes in the eight orientations, flat and run-length encoded, decode in the library to the colors it was given; the files <c>pflip</c> and <c>protate</c> rewrite in other orientations decode to the same picture; and the XYZE files of <c>ra_xyze</c> are identified and rejected.</description></item>
/// </list>
/// Both tools read a mantissa <c>m</c> as the bottom of its interval, <c>m × 2^(e - 136)</c>, where Radiance (and the
/// library) reads its middle, <c>(m + 0.5) × 2^(e - 136)</c>: the tests require exactly that difference, and ImageIO rounds
/// the result to a half. Neither tool reads the other orientations of the resolution string (FFmpeg gives the scanlines in
/// file order and misreads columns, ImageIO rejects the file), which the corpus records; ImageIO rejects the "old"
/// run-length encoding and a header without FORMAT too.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class RadianceInteropTests
{
    private static readonly (int Width, int Height)[] Sizes = [(1, 1), (7, 3), (8, 5), (37, 45), (300, 2)];

    public static TheoryData<RadianceCompression> Compressions() => [.. Enum.GetValues<RadianceCompression>()];

    [Theory]
    [MemberData(nameof(Compressions))]
    public async Task EncoderOutputDecodesInFFmpeg(RadianceCompression compression)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        foreach (var (width, height) in Sizes)
        {
            using var image = CreateImage(width, height, seed: (width * 100) + (int)compression);
            var path = InteropSettings.GetArtifactsDirectory("RadianceInteropTests") / string.Create(CultureInfo.InvariantCulture, $"output-{compression}-{width}x{height}.hdr");
            await image.SaveAsync(path, new RadianceEncoder { Compression = compression }, XunitCancellationToken);
            var picture = AssertRealPixels(image, await File.ReadAllBytesAsync(path, XunitCancellationToken));
            var encoded = compression == RadianceCompression.RunLength && width >= 8;
            Assert.All(picture.Encodings, encoding => Assert.Equal(encoded ? RadianceScanlineEncoding.NewRunLength : RadianceScanlineEncoding.Flat, encoding));

            // The green, blue and red planes
            var frame = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, "gbrpf32le", width, height, XunitCancellationToken));
            var plane = width * height;
            var rgbe = picture.Rgbe.ToArray();
            for (var i = 0; i < plane; i++)
            {
                var expected = (Bottom(rgbe, i, 0), Bottom(rgbe, i, 1), Bottom(rgbe, i, 2));
                Assert.Equal(expected, (ReadFloat(frame, (2 * plane) + i), ReadFloat(frame, i), ReadFloat(frame, plane + i)));
            }
        }
    }

    [Theory]
    [MemberData(nameof(Compressions))]
    public async Task EncoderOutputDecodesInAppleImageIO(RadianceCompression compression)
    {
        RequiredTools.AppleImageIO();
        foreach (var (width, height) in Sizes)
        {
            using var image = CreateImage(width, height, seed: (width * 10) + (int)compression);
            var path = InteropSettings.GetArtifactsDirectory("RadianceInteropTests") / string.Create(CultureInfo.InvariantCulture, $"imageio-{compression}-{width}x{height}.hdr");
            await image.SaveAsync(path, new RadianceEncoder { Compression = compression }, XunitCancellationToken);
            var picture = AssertRealPixels(image, await File.ReadAllBytesAsync(path, XunitCancellationToken));
            AssertImageIOReadsBottoms(picture.Rgbe.ToArray(), await RequiredTools.DecodeHalvesWithAppleImageIOAsync(path, width, height), path.Value);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 4)]
    [InlineData(8, 3)]
    [InlineData(37, 45)]
    [InlineData(600, 2)]
    public async Task FilesWrittenByFFmpegDecodeToTheirPixels(int width, int height)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();

        // Planes of colors of high dynamic range, black and equal pixels included
        var random = new Random(width + height);
        var plane = width * height;
        var input = new byte[plane * 3 * 4];
        for (var i = 0; i < plane; i++)
        {
            var scale = Math.Pow(10, random.Next(-4, 5));
            for (var c = 0; c < 3; c++)
            {
                var value = i % 7 == 0 ? 0 : i % 5 == 0 ? 1.5 : random.NextDouble() * scale;
                BinaryPrimitives.WriteSingleLittleEndian(input.AsSpan(((c * plane) + i) * 4), (float)value);
            }
        }

        var directory = InteropSettings.GetArtifactsDirectory("RadianceInteropTests");
        var raw = directory / string.Create(CultureInfo.InvariantCulture, $"ffmpeg-{width}x{height}.raw");
        var hdr = directory / string.Create(CultureInfo.InvariantCulture, $"ffmpeg-{width}x{height}.hdr");
        await File.WriteAllBytesAsync(raw, input, XunitCancellationToken);
        var result = await ffmpeg.RunAsync(["-hide_banner", "-y", "-f", "rawvideo", "-pix_fmt", "gbrpf32le", "-s", $"{width}x{height}", "-i", raw, "-c:v", "hdr", "-frames:v", "1", hdr], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode, result.StandardError);

        var data = await File.ReadAllBytesAsync(hdr, XunitCancellationToken);
        var picture = ReferenceRadiance.Read(data);
        using var image = Image.Load<RgbaFloat>(data);
        var bytes = new byte[plane * 16];
        image.Frames[0].CopyPixelBytesTo(bytes);
        Assert.Equal(picture.GetFloatPixels(), bytes);
    }

    /// <summary>
    /// The valid fixtures ImageIO reads: those in the standard orientation, with a FORMAT line, without "old" run-length
    /// encoded scanlines. ImageIO (macOS 27) rejects the other orientations ("missing image size specifier"), a header
    /// without FORMAT ("no FORMAT specifier found"), and the repetitions of the old encoding ("RGBE read error").
    /// </summary>
    public static TheoryData<string> ImageIOFixtures()
        => [.. GoldenCorpus.Default.GetIds(format: "hdr", kind: "valid").Where(id => GoldenCorpus.Default.Get(id).Entry is var entry && entry.HasFeature("hdr.scanOrder=-Y+X") && !entry.HasFeature("hdr.encoding=old-rle") && id != "hdr/signature-rgbe")];

    [Theory]
    [MemberData(nameof(ImageIOFixtures))]
    public async Task CorpusFilesDecodeInAppleImageIOToThePixelsOfTheLibrary(string id)
    {
        RequiredTools.AppleImageIO();
        var data = GoldenCorpus.Default.Get(id).ReadInput();
        var path = InteropSettings.GetArtifactsDirectory("RadianceInteropTests") / (id.Replace('/', '-') + ".hdr");
        await File.WriteAllBytesAsync(path, data, XunitCancellationToken);
        var picture = ReferenceRadiance.Read(data);
        AssertImageIOReadsBottoms(picture.Rgbe.ToArray(), await RequiredTools.DecodeHalvesWithAppleImageIOAsync(path, picture.Width, picture.Height), id);

        // The library reads the reference: the middle of each interval
        using var image = Image.Load<RgbaFloat>(data);
        var bytes = new byte[image.Width * image.Height * 16];
        image.Frames[0].CopyPixelBytesTo(bytes);
        Assert.Equal(picture.GetFloatPixels(), bytes);

    }

    [Theory]
    [MemberData(nameof(Compressions))]
    public async Task EncoderOutputDecodesInPvalueToThePixelsOfTheLibrary(RadianceCompression compression)
    {
        var radiance = await RequiredTools.RadianceAsync();
        foreach (var (width, height) in Sizes)
        {
            using var image = CreateImage(width, height, seed: (width * 1000) + (int)compression);
            var path = InteropSettings.GetArtifactsDirectory("RadianceInteropTests") / string.Create(CultureInfo.InvariantCulture, $"pvalue-{compression}-{width}x{height}.hdr");
            await image.SaveAsync(path, new RadianceEncoder { Compression = compression }, XunitCancellationToken);
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"-Y {height} +X {width}"), await radiance.GetResolutionAsync(path, XunitCancellationToken));

            // The standard orientation: the scanlines are the rows, from the top
            using var decoded = Image.Load<RgbaFloat>(path);
            Assert.Equal(GetColors(decoded), await radiance.DecodeAsync(path, XunitCancellationToken));
        }
    }

    public static TheoryData<string, bool> PvalueOrientations()
    {
        var data = new TheoryData<string, bool>();
        foreach (var order in RadianceFileBuilder.ScanOrders)
        {
            data.Add(order, false);
            data.Add(order, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PvalueOrientations))]
    public async Task FilesWrittenByPvalueDecodeToTheirColors(string scanOrder, bool flat)
    {
        var radiance = await RequiredTools.RadianceAsync();

        // 23x19: scanlines of 23 or 19 pixels, which pvalue run-length encodes (it writes scanlines shorter than 17 flat)
        const int Width = 23;
        const int Height = 19;
        var random = new Random(Width + scanOrder.GetHashCode(StringComparison.Ordinal));
        var colors = new float[Width * Height * 3];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = (i / 3) % 7 == 0 ? 0.5f : (float)(random.NextDouble() * Math.Pow(10, random.Next(-3, 4)));
        }

        var path = InteropSettings.GetArtifactsDirectory("RadianceInteropTests") / ("pvalue-written-" + scanOrder.Replace(' ', '_') + (flat ? "-flat" : "") + ".hdr");
        await radiance.EncodeAsync(colors, scanOrder, Width, Height, flat, xyz: false, path, XunitCancellationToken);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"{scanOrder[..2]} {(scanOrder[1] == 'X' ? Width : Height)} {scanOrder[3..]} {(scanOrder[1] == 'X' ? Height : Width)}"), await radiance.GetResolutionAsync(path, XunitCancellationToken));
        var picture = ReferenceRadiance.Read(await File.ReadAllBytesAsync(path, XunitCancellationToken));
        Assert.Equal(scanOrder, picture.ScanOrder);
        Assert.All(picture.Encodings, encoding => Assert.Equal(flat ? RadianceScanlineEncoding.Flat : RadianceScanlineEncoding.NewRunLength, encoding));

        // pvalue reads its own file as the library does, the scanlines in file order
        using var decoded = Image.Load<RgbaFloat>(path);
        Assert.Equal((Width, Height), (decoded.Width, decoded.Height));
        Assert.Equal(ToFileOrder(GetColors(decoded), Width, Height, scanOrder), await radiance.DecodeAsync(path, XunitCancellationToken));
    }

    public static TheoryData<string, bool, bool> Rewrites() => new()
    {
        { "pflip", true, false },
        { "pflip", false, true },
        { "pflip", true, true },
        { "protate", false, false },
        { "protate", true, false },
    };

    [Theory]
    [MemberData(nameof(Rewrites))]
    public async Task FilesRewrittenInAnotherOrientationDecodeToTheSamePicture(string tool, bool first, bool second)
    {
        var radiance = await RequiredTools.RadianceAsync();
        using var image = CreateImage(29, 17, seed: 7);
        var directory = InteropSettings.GetArtifactsDirectory("RadianceInteropTests");
        var source = directory / $"rewrite-source-{tool}-{first}-{second}.hdr";
        var output = directory / $"rewrite-{tool}-{first}-{second}.hdr";
        await image.SaveAsync(source, new RadianceEncoder(), XunitCancellationToken);

        // Without -c, the tools change the order of the scanlines and the resolution string, not the picture
        if (tool == "pflip")
        {
            await radiance.FlipAsync(source, output, horizontal: first, vertical: second, XunitCancellationToken);
        }
        else
        {
            await radiance.RotateAsync(source, output, counterClockwise: first, XunitCancellationToken);
        }

        Assert.NotEqual("-Y 17 +X 29", await radiance.GetResolutionAsync(output, XunitCancellationToken));
        using var original = Image.Load<RgbaFloat>(source);
        using var rewritten = Image.Load<RgbaFloat>(output);
        Assert.Equal(GetColors(original), GetColors(rewritten));
    }

    [Fact]
    public async Task XyzeFilesOfRaXyzeAreIdentifiedAndRejected()
    {
        var radiance = await RequiredTools.RadianceAsync();
        using var image = CreateImage(23, 9, seed: 3);
        var directory = InteropSettings.GetArtifactsDirectory("RadianceInteropTests");
        var source = directory / "xyze-source.hdr";
        var output = directory / "xyze.hdr";
        await image.SaveAsync(source, new RadianceEncoder(), XunitCancellationToken);
        await radiance.ToXyzeAsync(source, output, XunitCancellationToken);

        var data = await File.ReadAllBytesAsync(output, XunitCancellationToken);
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.True(info.Metadata.RadianceHeader!.IsXyz);
        Assert.Equal(new Size(23, 9), info.Size);
        Assert.Equal("XYZE samples", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
    }

    /// <summary>The RGB colors of an image, the rows from the top.</summary>
    private static float[] GetColors(Image<RgbaFloat> image)
    {
        var colors = new float[image.Width * image.Height * 3];
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.Frames[0][x, y];
                var offset = ((y * image.Width) + x) * 3;
                (colors[offset], colors[offset + 1], colors[offset + 2]) = (pixel.R, pixel.G, pixel.B);
            }
        }

        return colors;
    }

    /// <summary>
    /// Orders the colors of a picture in the standard orientation as the scanlines of a file of another scan order: the
    /// origin of the coordinates of Radiance is the bottom-left corner.
    /// </summary>
    private static float[] ToFileOrder(float[] colors, int width, int height, string scanOrder)
    {
        var majorIsX = scanOrder[1] == 'X';
        var count = majorIsX ? width : height;
        var length = majorIsX ? height : width;
        var result = new float[colors.Length];
        for (var i = 0; i < count; i++)
        {
            var major = scanOrder[0] == '-' ? count - 1 - i : i;
            for (var j = 0; j < length; j++)
            {
                var minor = scanOrder[3] == '-' ? length - 1 - j : j;
                var (x, y) = majorIsX ? (major, minor) : (minor, major);
                colors.AsSpan(((((height - 1 - y) * width) + x) * 3), 3).CopyTo(result.AsSpan(((i * length) + j) * 3));
            }
        }

        return result;
    }

    private static Image<RgbaFloat> CreateImage(int width, int height, int seed)
    {
        var random = new Random(seed);
        var image = new Image<RgbaFloat>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Colors from 1E-6 to 1E6, runs of equal pixels, black, one color 0
                var scale = (float)Math.Pow(10, random.Next(-6, 7));
                image.Frames[0][x, y] = (x + y) % 9 == 0 ? new RgbaFloat(0f, 0f, 0f, 1f)
                    : x % 4 == 3 ? new RgbaFloat(2f, 1f, 0.5f, 1f)
                    : new RgbaFloat((float)random.NextDouble() * scale, (float)random.NextDouble() * scale, x % 5 == 0 ? 0f : (float)random.NextDouble() * scale, 1f);
            }
        }

        return image;
    }

    /// <summary>Checks that a file holds the RGBE pixels "Real Pixels" gives for the colors of an image.</summary>
    private static RadiancePicture AssertRealPixels(Image<RgbaFloat> image, byte[] data)
    {
        var picture = ReferenceRadiance.Read(data);
        Assert.Equal((image.Width, image.Height), (picture.Width, picture.Height));
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.Frames[0][x, y];
                Assert.Equal(RadianceFileBuilder.ToRgbe(pixel.R, pixel.G, pixel.B), picture.Rgbe.Span.Slice(((y * image.Width) + x) * 4, 4).ToArray());
            }
        }

        return picture;
    }

    private static void AssertImageIOReadsBottoms(byte[] rgbe, Half[] decoded, string context)
    {
        for (var i = 0; i < rgbe.Length / 4; i++)
        {
            var expected = ((Half)Bottom(rgbe, i, 0), (Half)Bottom(rgbe, i, 1), (Half)Bottom(rgbe, i, 2), (Half)1);
            Assert.Equal(expected, (decoded[i * 4], decoded[(i * 4) + 1], decoded[(i * 4) + 2], decoded[(i * 4) + 3]), context);
        }
    }

    private static float Bottom(byte[] rgbe, int pixel, int component) => ReferenceRadiance.ToFloat(rgbe[(pixel * 4) + component], rgbe[(pixel * 4) + 3], middle: false);

    private static float ReadFloat(byte[] data, int index) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(4 * index));
}
