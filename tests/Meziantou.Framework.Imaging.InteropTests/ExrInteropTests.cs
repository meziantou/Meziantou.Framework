using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// OpenEXR files read by the OpenEXR reference library (<c>exr-to-raw</c>) and exchanged with FFmpeg's independent EXR
/// codec (libavcodec <c>exr</c>):
/// <list type="bullet">
/// <item><description>the output of the encoder, for every compression and both sample types, decodes in OpenEXR to the samples it stores, bit for bit;</description></item>
/// <item><description>the output of the encoder, for every compression and both sample types, decodes in FFmpeg to the samples it stores (colors premultiplied by alpha), values above 1 and below 0 included;</description></item>
/// <item><description>the files FFmpeg writes (every compression it has, half and float samples) decode in the library to the samples FFmpeg was given (FFmpeg multiplies them by alpha, the library divides them back);</description></item>
/// <item><description>tiled output (every level mode, rounding and line order) passes <c>exrcheck</c>, decodes in OpenEXR to the samples it stores, and its mipmap levels, extracted by OpenImageIO, are the levels of the same file without compression;</description></item>
/// <item><description>lossy output (PXR24, B44, B44A, DWAA, DWAB, scan lines and tiles) passes <c>exrcheck</c> and decodes in OpenEXR to the samples the library decodes, bit for bit, within the documented tolerances of the source.</description></item>
/// </list>
/// The decoder is compared bit for bit with OpenEXR on the files of the corpus.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class ExrInteropTests
{
    public static TheoryData<OpenExrCompression, OpenExrPixelType> EncoderCases()
    {
        var data = new TheoryData<OpenExrCompression, OpenExrPixelType>();
        foreach (var compression in (OpenExrCompression[])[OpenExrCompression.None, OpenExrCompression.Rle, OpenExrCompression.Zips, OpenExrCompression.Zip, OpenExrCompression.Piz])
        {
            data.Add(compression, OpenExrPixelType.Half);
            data.Add(compression, OpenExrPixelType.Float);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task EncoderOutputDecodesExactlyInFFmpeg(OpenExrCompression compression, OpenExrPixelType pixelType)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        foreach (var (width, height) in new[] { (1, 1), (37, 45), (70, 33) })
        {
            using var image = CreateImage(width, height, pixelType, seed: (width * 100) + (int)compression);
            var path = InteropSettings.GetArtifactsDirectory("ExrInteropTests") / string.Create(CultureInfo.InvariantCulture, $"output-{compression}-{pixelType}-{width}x{height}.exr");
            await image.SaveAsync(path, new OpenExrEncoder { Compression = compression }, XunitCancellationToken);

            // The green, blue, red and alpha planes of the samples as they are stored (FFmpeg does not divide them by alpha),
            // in the type of the file: FFmpeg converts half samples to 32-bit floats through 16-bit integers
            var half = pixelType == OpenExrPixelType.Half;
            var frame = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, half ? "gbrapf16le" : "gbrapf32le", width, height, XunitCancellationToken));
            var expected = GetPremultipliedSamples(image);
            var plane = width * height;
            for (var i = 0; i < plane; i++)
            {
                var actual = (Read(frame, (2 * plane) + i, half), Read(frame, i, half), Read(frame, plane + i, half), Read(frame, (3 * plane) + i, half));
                Assert.Equal(expected[i], actual);
            }
        }
    }

    [Theory]
    [InlineData(PixelFormat.GrayFloat)]
    [InlineData(PixelFormat.GrayHalf)]
    [InlineData(PixelFormat.LaFloat)]
    public async Task GrayOutputDecodesExactlyInOpenExr(PixelFormat format)
    {
        // A luminance channel Y (and A): OpenEXR reads the stored samples, the levels premultiplied by alpha
        var openExr = await RequiredTools.OpenExrAsync();
        var random = new Random(31);
        var pixels = new LaFloat[37 * 5];
        for (var i = 0; i < pixels.Length; i++)
        {
            // Halves, so that every format holds them: highlights, negative levels, and alphas that are powers of two
            pixels[i] = new LaFloat((float)(Half)((random.NextDouble() * 6) - 1), format == PixelFormat.LaFloat ? 1f / (1 << random.Next(3)) : 1f);
        }

        using var source = Image.ImportPixelData<LaFloat>(pixels, 37, 5);
        using var image = source.CloneAs(format, options: null);
        image.Metadata.TransferFunction = Metadata.ColorTransferFunction.Linear;
        var path = InteropSettings.GetArtifactsDirectory("ExrInteropTests") / string.Create(CultureInfo.InvariantCulture, $"gray-{format}.exr");
        await image.SaveAsync(path, new OpenExrEncoder(), XunitCancellationToken);

        var part = await openExr.DecodeAsync(path, XunitCancellationToken);
        Assert.Equal(format == PixelFormat.LaFloat ? ["A", "Y"] : ["Y"], part.Channels.Keys.Order(StringComparer.Ordinal));
        for (var i = 0; i < pixels.Length; i++)
        {
            Assert.Equal(pixels[i].L * pixels[i].A, part.Channels["Y"][i]);
            if (format == PixelFormat.LaFloat)
            {
                Assert.Equal(pixels[i].A, part.Channels["A"][i]);
            }
        }

        // And the library reads it back in the gray format of its samples
        using var back = await Image.LoadAsync(path, cancellationToken: XunitCancellationToken);
        Assert.Equal(format, back.PixelFormat);
    }

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task EncoderOutputDecodesExactlyInOpenExr(OpenExrCompression compression, OpenExrPixelType pixelType)
    {
        var openExr = await RequiredTools.OpenExrAsync();
        foreach (var (width, height) in new[] { (1, 1), (37, 45), (70, 33), (5, 130) })
        {
            using var image = CreateImage(width, height, pixelType, seed: (width * 100) + (int)compression);
            var path = InteropSettings.GetArtifactsDirectory("ExrInteropTests") / string.Create(CultureInfo.InvariantCulture, $"openexr-{compression}-{pixelType}-{width}x{height}.exr");
            await image.SaveAsync(path, new OpenExrEncoder { Compression = compression }, XunitCancellationToken);

            var part = await openExr.DecodeAsync(path, XunitCancellationToken);
            Assert.Equal(compression.ToString().ToUpperInvariant(), part.Compression);
            Assert.Equal((width, height), (part.Width, part.Height));
            var expected = GetPremultipliedSamples(image);
            for (var i = 0; i < expected.Length; i++)
            {
                var actual = (part.Channels["R"][i], part.Channels["G"][i], part.Channels["B"][i], part.Channels["A"][i]);
                Assert.Equal(expected[i], actual);
            }
        }
    }

    public static TheoryData<OpenExrCompression, OpenExrLevelMode, OpenExrLevelRounding, OpenExrLineOrder> TiledCases()
    {
        var data = new TheoryData<OpenExrCompression, OpenExrLevelMode, OpenExrLevelRounding, OpenExrLineOrder>();
        foreach (var compression in (OpenExrCompression[])[OpenExrCompression.None, OpenExrCompression.Zip, OpenExrCompression.Piz, OpenExrCompression.Dwaa])
        {
            data.Add(compression, OpenExrLevelMode.OneLevel, OpenExrLevelRounding.Down, OpenExrLineOrder.IncreasingY);
            data.Add(compression, OpenExrLevelMode.MipmapLevels, OpenExrLevelRounding.Down, OpenExrLineOrder.DecreasingY);
            data.Add(compression, OpenExrLevelMode.MipmapLevels, OpenExrLevelRounding.Up, OpenExrLineOrder.RandomY);
            data.Add(compression, OpenExrLevelMode.RipmapLevels, OpenExrLevelRounding.Up, OpenExrLineOrder.IncreasingY);
        }

        data.Add(OpenExrCompression.Rle, OpenExrLevelMode.RipmapLevels, OpenExrLevelRounding.Down, OpenExrLineOrder.DecreasingY);
        data.Add(OpenExrCompression.Zips, OpenExrLevelMode.MipmapLevels, OpenExrLevelRounding.Down, OpenExrLineOrder.IncreasingY);
        data.Add(OpenExrCompression.Pxr24, OpenExrLevelMode.MipmapLevels, OpenExrLevelRounding.Down, OpenExrLineOrder.IncreasingY);
        data.Add(OpenExrCompression.B44A, OpenExrLevelMode.RipmapLevels, OpenExrLevelRounding.Down, OpenExrLineOrder.IncreasingY);
        data.Add(OpenExrCompression.Dwab, OpenExrLevelMode.MipmapLevels, OpenExrLevelRounding.Up, OpenExrLineOrder.DecreasingY);
        return data;
    }

    [Theory]
    [MemberData(nameof(TiledCases))]
    public async Task TiledOutputDecodesInOpenExr(OpenExrCompression compression, OpenExrLevelMode levelMode, OpenExrLevelRounding rounding, OpenExrLineOrder lineOrder)
    {
        var openExr = await RequiredTools.OpenExrAsync();
        var lossless = compression <= OpenExrCompression.Piz || compression == OpenExrCompression.Pxr24;
        foreach (var (width, height, tileWidth, tileHeight) in new[] { (1, 1, 64, 64), (37, 45, 16, 16), (70, 33, 32, 8) })
        {
            using var image = CreateImage(width, height, OpenExrPixelType.Half, seed: width + (int)compression, opaque: !lossless);
            var tiling = new OpenExrTiling { TileWidth = tileWidth, TileHeight = tileHeight, LevelMode = levelMode, LevelRounding = rounding };
            var name = string.Create(CultureInfo.InvariantCulture, $"tiled-{compression}-{levelMode}-{rounding}-{lineOrder}-{width}x{height}");
            var path = InteropSettings.GetArtifactsDirectory("ExrInteropTests") / (name + ".exr");
            await image.SaveAsync(path, new OpenExrEncoder { Compression = compression, Tiling = tiling, LineOrder = lineOrder }, XunitCancellationToken);
            await openExr.CheckAsync(path, XunitCancellationToken);

            // The full resolution: the stored samples of a lossless file, the samples the library decodes of a lossy one
            var part = await openExr.DecodeAsync(path, XunitCancellationToken);
            Assert.Equal(ExrName(compression), part.Compression);
            Assert.Equal((width, height), (part.Width, part.Height));
            var expected = lossless ? GetPremultipliedSamples(image) : GetDecodedSamples(path);
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], (part.Channels["R"][i], part.Channels["G"][i], part.Channels["B"][i], part.Channels["A"][i]));
            }

            // The levels of a mipmap: those of the same file without compression, for a lossless compression
            if (levelMode == OpenExrLevelMode.MipmapLevels && lossless)
            {
                var reference = InteropSettings.GetArtifactsDirectory("ExrInteropTests") / (name + "-none.exr");
                await image.SaveAsync(reference, new OpenExrEncoder { Compression = OpenExrCompression.None, Tiling = tiling }, XunitCancellationToken);
                for (var level = 1; level < 8 && Math.Max(width, height) >> (level - 1) > 1; level++)
                {
                    var actual = await openExr.DecodeMipLevelAsync(path, level, XunitCancellationToken);
                    var uncompressed = await openExr.DecodeMipLevelAsync(reference, level, XunitCancellationToken);
                    Assert.Equal((uncompressed.Width, uncompressed.Height), (actual.Width, actual.Height));
                    foreach (var channel in "ABGR")
                    {
                        Assert.Equal(uncompressed.Channels[channel.ToString()], actual.Channels[channel.ToString()]);
                    }
                }
            }
        }
    }

    public static TheoryData<OpenExrCompression, OpenExrPixelType, bool> LossyCases()
    {
        var data = new TheoryData<OpenExrCompression, OpenExrPixelType, bool>();
        foreach (var compression in (OpenExrCompression[])[OpenExrCompression.Pxr24, OpenExrCompression.B44, OpenExrCompression.B44A, OpenExrCompression.Dwaa, OpenExrCompression.Dwab])
        {
            foreach (var pixelType in (OpenExrPixelType[])[OpenExrPixelType.Half, OpenExrPixelType.Float])
            {
                data.Add(compression, pixelType, false);
                data.Add(compression, pixelType, true);
            }
        }

        return data;
    }

    /// <summary>
    /// Lossy output: OpenEXR decodes the samples the library decodes, bit for bit (both decoders are exact), and they are within
    /// the tolerance of the compression of the samples of the source: relative errors of 2^-15 for the float samples of
    /// PXR24 (half samples are exact), 1/32 for B44 (half samples; float samples are exact), 1/16 for DWA at its default level
    /// (smooth colors with noise of 1/100; alpha is exact).
    /// </summary>
    [Theory]
    [MemberData(nameof(LossyCases))]
    public async Task LossyOutputDecodesInOpenExrAsInTheLibrary(OpenExrCompression compression, OpenExrPixelType pixelType, bool tiled)
    {
        var openExr = await RequiredTools.OpenExrAsync();
        var tolerance = (compression, pixelType) switch
        {
            (OpenExrCompression.Pxr24, OpenExrPixelType.Half) => 0,
            (OpenExrCompression.Pxr24, _) => 1.0 / 32768,
            (OpenExrCompression.B44 or OpenExrCompression.B44A, OpenExrPixelType.Float) => 0,
            (OpenExrCompression.B44 or OpenExrCompression.B44A, _) => 1.0 / 32,
            _ => 1.0 / 16,
        };

        foreach (var (width, height) in new[] { (1, 1), (37, 45), (70, 300) })
        {
            using var image = CreateSmoothImage(width, height, pixelType);
            var path = InteropSettings.GetArtifactsDirectory("ExrInteropTests") / string.Create(CultureInfo.InvariantCulture, $"lossy-{compression}-{pixelType}-{(tiled ? "tiled" : "lines")}-{width}x{height}.exr");
            await image.SaveAsync(path, new OpenExrEncoder { Compression = compression, PixelType = pixelType, Tiling = tiled ? new OpenExrTiling { TileWidth = 32, TileHeight = 32 } : null }, XunitCancellationToken);
            await openExr.CheckAsync(path, XunitCancellationToken);

            var part = await openExr.DecodeAsync(path, XunitCancellationToken);
            Assert.Equal(ExrName(compression), part.Compression);
            var decoded = GetDecodedSamples(path);
            var source = GetPremultipliedSamples(image);
            for (var i = 0; i < decoded.Length; i++)
            {
                Assert.Equal(decoded[i], (part.Channels["R"][i], part.Channels["G"][i], part.Channels["B"][i], part.Channels["A"][i]));
                Assert.Equal(source[i].A, decoded[i].A);
                foreach (var (expected, actual) in new[] { (source[i].R, decoded[i].R), (source[i].G, decoded[i].G), (source[i].B, decoded[i].B) })
                {
                    Assert.True(Math.Abs(expected - actual) <= tolerance * Math.Max(1, Math.Abs(expected)), $"{compression} {pixelType} pixel {i}: {expected} {actual}");
                }
            }
        }
    }

    [Theory]
    [InlineData("none", "half")]
    [InlineData("rle", "half")]
    [InlineData("zip1", "half")]
    [InlineData("zip16", "half")]
    [InlineData("none", "float")]
    [InlineData("rle", "float")]
    [InlineData("zip1", "float")]
    [InlineData("zip16", "float")]
    public async Task FilesWrittenByFFmpegDecodeExactly(string compression, string format)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        const int Width = 23;
        const int Height = 41;

        // Planes of values that a half holds exactly, above 1 and below 0 included; alphas of 0, 1/2 and 1, by which FFmpeg
        // multiplies the colors exactly
        var random = new Random(7);
        var plane = Width * Height;
        var input = new byte[plane * 4 * 4];
        for (var i = 0; i < 4 * plane; i++)
        {
            var value = i < 3 * plane ? (float)(Half)((random.NextDouble() * 20) - 4) : (i % 3) * 0.5f;
            BinaryPrimitives.WriteSingleLittleEndian(input.AsSpan(4 * i), value);
        }

        var directory = InteropSettings.GetArtifactsDirectory("ExrInteropTests");
        var raw = directory / $"ffmpeg-{compression}-{format}.raw";
        var exr = directory / $"ffmpeg-{compression}-{format}.exr";
        await File.WriteAllBytesAsync(raw, input, XunitCancellationToken);
        var result = await ffmpeg.RunAsync(["-hide_banner", "-y", "-f", "rawvideo", "-pix_fmt", "gbrapf32le", "-s", $"{Width}x{Height}", "-i", raw, "-c:v", "exr", "-compression", compression, "-format", format, "-frames:v", "1", exr], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode, result.StandardError);

        // The library divides the colors by alpha again: only a pixel whose alpha is 0 lost its color
        using var image = Image.Load<RgbaFloat>(exr);
        for (var i = 0; i < plane; i++)
        {
            var pixel = image.Frames[0][i % Width, i / Width];
            var alpha = Read(input, (3 * plane) + i, half: false);
            float Color(int index) => alpha == 0 ? 0f : Read(input, index, half: false);
            Assert.Equal((Color((2 * plane) + i), Color(i), Color(plane + i), alpha), (pixel.R, pixel.G, pixel.B, pixel.A));
        }
    }

    private static Image CreateImage(int width, int height, OpenExrPixelType pixelType, int seed, bool opaque = false)
    {
        var random = new Random(seed);
        float Next() => (float)(Half)((random.NextDouble() * 40) - 8);
        var full = new Image<RgbaFloat>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                full.Frames[0][x, y] = (x + y) % 5 == 0 ? new RgbaFloat(1f, 1f, 1f) : new RgbaFloat(Next(), Next(), Next(), opaque ? 1f : (float)(Half)random.NextDouble());
            }
        }

        if (pixelType == OpenExrPixelType.Float)
            return full;

        using (full)
        {
            return full.CloneAs<RgbaHalf>();
        }
    }

    /// <summary>An opaque image of smooth colors (some above 1) with noise of 1/100, as half or float pixels.</summary>
    private static Image CreateSmoothImage(int width, int height, OpenExrPixelType pixelType)
    {
        var random = new Random(width * height);
        var full = new Image<RgbaFloat>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var noise = (float)random.NextDouble() * 0.01f;
                full.Frames[0][x, y] = new RgbaFloat(0.1f + (x / 40f) + noise, 0.5f + (0.4f * MathF.Sin(y / 7f)), 2f + (y / 45f) - noise, 1f);
            }
        }

        if (pixelType == OpenExrPixelType.Float)
            return full;

        using (full)
        {
            return full.CloneAs<RgbaHalf>();
        }
    }

    /// <summary>The samples the library decodes from a file (alpha is 1 or exact: the colors are the stored samples).</summary>
    private static (float R, float G, float B, float A)[] GetDecodedSamples(FullPath path)
    {
        using var image = Image.Load<RgbaFloat>(path);
        var result = new (float, float, float, float)[image.Width * image.Height];
        for (var i = 0; i < result.Length; i++)
        {
            var pixel = image.Frames[0][i % image.Width, i / image.Width];
            result[i] = (pixel.R * pixel.A, pixel.G * pixel.A, pixel.B * pixel.A, pixel.A);
        }

        return result;
    }

    private static string ExrName(OpenExrCompression compression) => compression.ToString().ToUpperInvariant();

    /// <summary>The samples the file stores: the colors multiplied by alpha, rounded to the type of the file, unless alpha is 0.</summary>
    private static (float R, float G, float B, float A)[] GetPremultipliedSamples(Image image)
    {
        var half = image is Image<RgbaHalf>;
        using var full = image is Image<RgbaFloat> source ? source.CloneAs<RgbaFloat>() : ((Image<RgbaHalf>)image).CloneAs<RgbaFloat>();
        var result = new (float, float, float, float)[image.Width * image.Height];
        for (var i = 0; i < result.Length; i++)
        {
            var pixel = full.Frames[0][i % image.Width, i / image.Width];
            float Store(float color)
            {
                var value = pixel.A == 0 ? color : (double)color * pixel.A;
                return half ? (float)(Half)value : (float)value;
            }

            result[i] = (Store(pixel.R), Store(pixel.G), Store(pixel.B), pixel.A);
        }

        return result;
    }

    private static float Read(byte[] data, int index, bool half)
        => half ? (float)BinaryPrimitives.ReadHalfLittleEndian(data.AsSpan(2 * index)) : BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(4 * index));
}
