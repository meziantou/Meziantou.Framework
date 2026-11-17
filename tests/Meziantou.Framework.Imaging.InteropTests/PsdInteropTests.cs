using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Psd;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// PSD files written by the independent writers of the pinned tools, decoded by the library, and the composites of the
/// corpus read by OpenImageIO:
/// <list type="bullet">
/// <item><description>ImageMagick writes RGB, grayscale, indexed and bitmap documents of 8 and 16 bits, raw and PackBits, with and without a layer, PSD and PSB: the library decodes the pattern it was given;</description></item>
/// <item><description>an RGBA image ImageMagick writes (its writer does not matte the colors) is read by the library as ImageMagick reads it back (both un-matte the colors the same way);</description></item>
/// <item><description><c>psd-tools write</c> writes every color mode, depth and compression: the library decodes the samples <c>psd-tools dump</c> gives back, CMYK inks included (stored inverted);</description></item>
/// <item><description>OpenImageIO decodes the valid fixtures of the corpus to the pixels of their references, but for the differences it is known for.</description></item>
/// </list>
/// </summary>
public sealed class PsdInteropTests
{
    private static readonly (int Width, int Height)[] Sizes = [(1, 1), (7, 3), (130, 5)];

    public static TheoryData<string, string, int, bool> MagickVariants() => new()
    {
        { "rgb", "rgb", 8, false },
        { "rgb", "rgb", 16, false },
        { "gray", "gray", 8, false },
        { "gray", "gray", 16, false },
        { "rgb-rle", "rgb", 8, true },
        { "gray-rle", "gray", 16, true },
    };

    [Theory]
    [MemberData(nameof(MagickVariants))]
    public async Task FilesWrittenByImageMagickDecodeToTheirPattern(string name, string input, int depth, bool rle)
    {
        var tools = await PsdTools.LocateAsync(XunitCancellationToken);
        var directory = InteropSettings.GetArtifactsDirectory("PsdInteropTests");
        foreach (var (width, height) in Sizes)
        {
            foreach (var (container, layers) in new[] { ("psd", true), ("psd", false), ("psb", true) })
            {
                var channels = input == "gray" ? 1 : 3;
                var samples = Pattern(width * height * channels, depth == 16 ? 65535 : 255, width + depth);
                var source = directory / string.Create(CultureInfo.InvariantCulture, $"magick-{name}-{depth}-{width}x{height}-{container}-{layers}.raw");
                var output = source.ChangeExtension("." + container);
                await File.WriteAllBytesAsync(source, ToLittleEndian(samples, depth), XunitCancellationToken);
                List<string> arguments = ["-size", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"), "-depth", depth.ToString(CultureInfo.InvariantCulture), "-endian", "LSB", input + ":" + source, "-endian", "MSB"];
                if (rle)
                {
                    arguments.AddRange(["-compress", "RLE"]);
                }

                if (!layers)
                {
                    arguments.AddRange(["-define", "psd:write-layers=false"]);
                }

                arguments.Add((container == "psb" ? "PSB:" : "") + output);
                await tools.MagickAsync(arguments, XunitCancellationToken);

                using var image = await Image.LoadAsync<Rgba64>(output, cancellationToken: XunitCancellationToken);
                var scale = depth == 16 ? 1 : 257;
                for (var i = 0; i < width * height; i++)
                {
                    var pixel = image.Frames[0][i % width, i / width];
                    var expected = channels == 1
                        ? new Rgba64((ushort)(samples[i] * scale), (ushort)(samples[i] * scale), (ushort)(samples[i] * scale), ushort.MaxValue)
                        : new Rgba64((ushort)(samples[i * 3] * scale), (ushort)(samples[(i * 3) + 1] * scale), (ushort)(samples[(i * 3) + 2] * scale), ushort.MaxValue);
                    Assert.Equal(expected, pixel);
                }
            }
        }
    }

    [Fact]
    public async Task BitmapAndIndexedFilesWrittenByImageMagickDecodeToTheirPattern()
    {
        var tools = await PsdTools.LocateAsync(XunitCancellationToken);
        var directory = InteropSettings.GetArtifactsDirectory("PsdInteropTests");
        foreach (var (width, height) in Sizes)
        {
            // Black and white: a bitmap document, its rows padded with 1 bits
            var bits = Pattern(width * height, 1, width).Select(v => v == 0 ? 0 : 255).ToArray();
            var source = directory / string.Create(CultureInfo.InvariantCulture, $"magick-bitmap-{width}x{height}.raw");
            await File.WriteAllBytesAsync(source, ToLittleEndian(bits, 8), XunitCancellationToken);
            var bitmap = source.ChangeExtension(".psd");
            await tools.MagickAsync(["-size", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"), "-depth", "8", "gray:" + source, "-endian", "MSB", "-type", "Bilevel", "-depth", "1", bitmap], XunitCancellationToken);
            using (var image = await Image.LoadAsync<Gray8>(bitmap, cancellationToken: XunitCancellationToken))
            {
                Assert.Equal(ImageColorModel.Grayscale, Image.Identify(bitmap).ColorModel);
                Assert.Equal(bits.Select(v => (byte)v).ToArray(), GetBytes(image));
            }

            // A few colors: an indexed document (raw: ImageMagick writes PackBits rows of indexes that it rejects itself)
            var colors = Pattern(width * height, 3, width + 1).SelectMany(v => new[] { v * 80, 255 - (v * 60), v * 20 }).ToArray();
            source = directory / string.Create(CultureInfo.InvariantCulture, $"magick-indexed-{width}x{height}.raw");
            await File.WriteAllBytesAsync(source, ToLittleEndian(colors, 8), XunitCancellationToken);
            var indexed = source.ChangeExtension(".psd");
            await tools.MagickAsync(["-size", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"), "-depth", "8", "rgb:" + source, "-endian", "MSB", "-type", "Palette", indexed], XunitCancellationToken);
            Assert.Equal(ImageColorModel.Indexed, Image.Identify(indexed).ColorModel);
            using var rgb = await Image.LoadAsync<Rgb24>(indexed, cancellationToken: XunitCancellationToken);
            Assert.Equal(colors.Select(v => (byte)v).ToArray(), GetBytes(rgb));
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public async Task TransparencyIsReadAsImageMagickReadsIt(int depth)
    {
        var tools = await PsdTools.LocateAsync(XunitCancellationToken);
        var directory = InteropSettings.GetArtifactsDirectory("PsdInteropTests");
        var max = depth == 16 ? 65535 : 255;
        foreach (var (width, height) in Sizes)
        {
            var samples = Pattern(width * height * 4, max, width * 3);
            var source = directory / string.Create(CultureInfo.InvariantCulture, $"magick-rgba-{depth}-{width}x{height}.raw");
            await File.WriteAllBytesAsync(source, ToLittleEndian(samples, depth), XunitCancellationToken);
            var output = source.ChangeExtension(".psd");
            await tools.MagickAsync(["-size", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"), "-depth", depth.ToString(CultureInfo.InvariantCulture), "-endian", "LSB", "rgba:" + source, "-endian", "MSB", "-compress", "RLE", output], XunitCancellationToken);

            // ImageMagick writes the colors as they are and un-mattes them when it reads them, as the library does: the stored
            // samples are the pattern, and both decoders give the same pixels
            var stored = await tools.MagickDecodeAsync(output, depth, unblend: false, XunitCancellationToken);
            Assert.Equal(ToLittleEndian(samples, depth), stored);
            var expected = await tools.MagickDecodeAsync(output, depth, unblend: true, XunitCancellationToken);
            using var image = await Image.LoadAsync(output, cancellationToken: XunitCancellationToken);
            Assert.Equal(depth == 16 ? PixelFormat.Rgba64 : PixelFormat.Rgba32, image.PixelFormat);
            var actual = GetBytes(image);
            var maxError = 0;
            for (var i = 0; i < samples.Length; i++)
            {
                var a = depth == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(actual.AsSpan(i * 2)) : actual[i];
                var e = depth == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(i * 2)) : expected[i];
                maxError = Math.Max(maxError, Math.Abs(a - e));
            }

            // ImageMagick un-mattes in floating point and rounds its 16-bit quantum to the output depth
            Assert.True(maxError <= 1, string.Create(CultureInfo.InvariantCulture, $"{width}x{height}: the library and ImageMagick differ by {maxError}"));
        }
    }

    public static TheoryData<string, int, int> PsdToolsVariants() => new()
    {
        { "RGB", 3, 8 },
        { "RGB", 3, 16 },
        { "RGB", 3, 32 },
        { "GRAYSCALE", 1, 8 },
        { "GRAYSCALE", 1, 16 },
        { "GRAYSCALE", 1, 32 },
        { "DUOTONE", 1, 16 },
        { "CMYK", 4, 8 },
        { "CMYK", 4, 16 },
    };

    [Theory]
    [MemberData(nameof(PsdToolsVariants))]
    public async Task FilesWrittenByPsdToolsDecodeToTheirStoredSamples(string mode, int channels, int depth)
    {
        var tools = await PsdTools.LocateAsync(XunitCancellationToken);
        var directory = InteropSettings.GetArtifactsDirectory("PsdInteropTests");
        foreach (var (width, height) in Sizes)
        {
            foreach (var compression in new[] { "RAW", "RLE" })
            {
                foreach (var version in new[] { 1, 2 })
                {
                    var folder = directory / string.Create(CultureInfo.InvariantCulture, $"psd-tools-{mode}-{depth}-{width}x{height}-{compression}-{version}");
                    Directory.CreateDirectory(folder);
                    var count = width * height * channels;
                    var raw = depth == 32
                        ? [.. Pattern(count, 10_000, width).SelectMany(v => BitConverter.GetBytes(v / 2_500f))]
                        : ToLittleEndian(Pattern(count, depth == 16 ? 65535 : 255, width + version), depth);
                    await File.WriteAllBytesAsync(folder / "composite.raw", raw, XunitCancellationToken);
                    var description = string.Create(CultureInfo.InvariantCulture, $$"""{"version": {{version}}, "width": {{width}}, "height": {{height}}, "depth": {{depth}}, "colorMode": "{{mode}}", "channels": {{channels}}, "compression": "{{compression}}", "composite": "composite.raw", "hasComposite": true, "iccProfile": null, "palette": null, "transparentIndex": null, "mergedAlpha": false, "layerBlock": null, "layers": []}""");
                    await File.WriteAllTextAsync(folder / "header.json", description, XunitCancellationToken);
                    var output = folder / (version == 2 ? "out.psb" : "out.psd");
                    await tools.PsdToolsWriteAsync(folder / "header.json", output, XunitCancellationToken);
                    var dumped = await tools.PsdToolsDumpAsync(output, folder / "dump", XunitCancellationToken);
                    Assert.Equal(raw, dumped);
                    var data = await File.ReadAllBytesAsync(output, XunitCancellationToken);
                    AssertStoredSamples(data, dumped, mode, channels, depth, width, height);
                }
            }
        }
    }

    [Fact]
    public async Task OpenImageIOReadsTheFixturesAsTheirReferences()
    {
        var tools = await PsdTools.LocateAsync(XunitCancellationToken);
        var directory = InteropSettings.GetArtifactsDirectory("PsdInteropTests");
        var differences = new List<string>();
        foreach (var id in GoldenCorpus.Default.GetIds(format: "psd", kind: FixtureKinds.Valid))
        {
            var fixture = GoldenCorpus.Default.Get(id);
            var input = directory / (id.Replace('/', '-') + ".psd");
            await File.WriteAllBytesAsync(input, fixture.ReadInput(), XunitCancellationToken);
            var picture = ReferencePsd.Read(fixture.ReadInput());
            var tiff = input.ChangeExtension(".tif");
            try
            {
                await tools.OiioToTiffAsync(input, picture.Depth switch { 32 => "float", 16 => "uint16", _ => "uint8" }, tiff, XunitCancellationToken);
            }
            catch (InvalidOperationException)
            {
                differences.Add(id + ": rejected");
                continue;
            }

            using var reference = Image.Load(fixture.ReadInput());
            Image oiio;
            try
            {
                oiio = Image.Load(tiff);
            }
            catch (InvalidImageContentException)
            {
                differences.Add(id + ": unreadable TIFF");
                continue;
            }

            using var _ = oiio;
            if (!SamePixels(reference, oiio))
            {
                differences.Add(id + GetVisibleDifference(reference, oiio) switch
                {
                    0 => ": colors where alpha is 0",
                    <= 1f / 255 => ": rounding",
                    _ => ": pixels",
                });
            }
        }

        Assert.Equal(KnownOpenImageIODifferences, differences);
    }

    // What OpenImageIO 3.2.2 reads differently, measured on the fixtures. It reads the transparency index, the merged alpha of
    // the RGB composites (it removes the white matte into premultiplied colors) and the extra channels as the library does.
    public static TheoryData<string> EncoderCases =>
    [
        "rgb8-raw",
        "rgb8-rle",
        "gray16-rle",
        "rgba8-rle",
        "rgba8-raw-psb",
        "la8-raw",
        "la16-rle",
        "rgba16-rle",
        "rgb16-raw-psb",
        "cmyk8-rle",
        "cmyk16-raw",
        "rgbaf-rle",
        "rgbf-raw",
    ];

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task EncoderOutputIsReadByPsdToolsAndImageMagick(string name)
    {
        var tools = await PsdTools.LocateAsync(XunitCancellationToken);
        var directory = InteropSettings.GetArtifactsDirectory("PsdInteropTests") / ("encoder-" + name);
        Directory.CreateDirectory(directory);
        var (image, encoder) = CreateEncoderCase(name);
        using (image)
        {
            var path = directory / (encoder.LargeDocument ? "out.psb" : "out.psd");
            image.Save(path, encoder);
            var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
            var picture = ReferencePsd.Read(data);

            // psd-tools gives the stored samples of the composite, and of the layer an image with transparency has
            var dump = directory / "dump";
            if (Directory.Exists(dump))
            {
                Directory.Delete(dump, recursive: true);
            }

            await tools.PsdToolsDumpAllAsync(path, dump, XunitCancellationToken);
            var header = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(dump / "header.json", XunitCancellationToken)).RootElement;
            Assert.Equal(picture.Version, header.GetProperty("version").GetInt32());
            Assert.Equal(picture.Depth, header.GetProperty("depth").GetInt32());
            Assert.Equal(picture.ChannelCount, header.GetProperty("channels").GetInt32());
            Assert.True(header.GetProperty("hasComposite").GetBoolean());
            Assert.Equal(picture.HasMergedAlpha, header.GetProperty("mergedAlpha").GetBoolean());
            Assert.Equal(Interleave(picture.Channels, picture.Depth, picture.Width * picture.Height), await File.ReadAllBytesAsync(dump / "composite.raw", XunitCancellationToken));
            var layers = header.GetProperty("layers");
            Assert.Equal(picture.HasMergedAlpha ? 1 : 0, layers.GetArrayLength());
            if (picture.HasMergedAlpha)
            {
                // The layer holds the colors as they are: its channels are red, green and blue (or gray), then the transparency
                var layer = layers[0];
                Assert.Equal("Layer 0", layer.GetProperty("name").GetString());
                Assert.Equal(picture.Height, layer.GetProperty("bottom").GetInt32());
                Assert.Equal(picture.Width, layer.GetProperty("right").GetInt32());
                Assert.Equal(GetStraightSamples(image, picture), await File.ReadAllBytesAsync(dump / layer.GetProperty("file").GetString()!, XunitCancellationToken));
            }

            // ImageMagick reads the composite (it un-mattes it as the library does) and the layer, as 16-bit RGBA; it
            // converts CMYK to RGB, and its floating-point composites are not compared
            if (picture.Mode == 4 || picture.Depth == 32)
                return;

            // ImageMagick does not un-matte gray composites with transparency (PsdCorpus.MagickDifferences): it gives the
            // stored samples
            using var decoded = picture.Depth == 8 ? (Image)Image.Load<Rgba32>(data) : Image.Load<Rgba64>(data);
            var composite = await tools.MagickDecodeAsync(path, picture.Depth, unblend: true, XunitCancellationToken);
            Assert.Equal(picture.Mode == 1 && picture.HasMergedAlpha ? GetMattedGray(picture) : GetBytes(decoded), composite);
            if (picture.HasMergedAlpha)
            {
                using var exact = picture.Depth == 8 ? (Image)image.CloneAs<Rgba32>() : image.CloneAs<Rgba64>();
                var output = directory / "layer.rgba";
                await tools.MagickAsync([path + "[1]", "-depth", picture.Depth.ToString(CultureInfo.InvariantCulture), "-endian", "LSB", "rgba:" + output], XunitCancellationToken);
                Assert.Equal(GetBytes(exact), await File.ReadAllBytesAsync(output, XunitCancellationToken));
            }
        }
    }

    /// <summary>Gets the image and the settings of an encoder case: pixels, compression and container.</summary>
    private static (Image Image, Formats.PsdEncoder Encoder) CreateEncoderCase(string name)
    {
        const int Width = 37;
        const int Height = 9;
        var parts = name.Split('-');
        var transparent = parts[0] is "rgba8" or "la8" or "la16" or "rgba16" or "rgbaf";
        using var pattern = new Image<Rgba64>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var alpha = transparent ? (ushort)(x * 65535 / (Width - 1)) : ushort.MaxValue;
                pattern.Frames[0][x, y] = new Rgba64((ushort)(x * 1771), (ushort)(65535 - (y * 7000)), (ushort)(((x / 3) * 3000) + (y * 911)), alpha);
            }
        }

        Image image = parts[0] switch
        {
            "rgb8" or "rgba8" => pattern.CloneAs<Rgba32>(),
            "gray16" => pattern.CloneAs<Gray16>(),
            "la8" => pattern.CloneAs<La16>(),
            "la16" => pattern.CloneAs<La32>(),
            "rgba16" => pattern.CloneAs<Rgba64>(),
            "rgb16" => pattern.CloneAs<Rgb48>(),
            "cmyk8" => CreateCmyk(pattern, is16Bit: false),
            "cmyk16" => CreateCmyk(pattern, is16Bit: true),
            _ => pattern.CloneAs<RgbaFloat>(),
        };

        if (image.PixelFormat == PixelFormat.RgbaFloat)
        {
            image.Metadata.TransferFunction = Metadata.ColorTransferFunction.Linear;
        }

        var encoder = new Formats.PsdEncoder
        {
            Compression = parts[1] == "rle" ? Formats.PsdCompression.RunLength : Formats.PsdCompression.None,
            LargeDocument = parts.Length > 2 && parts[2] == "psb",
        };

        return (image, encoder);
    }

    private static Image CreateCmyk(Image<Rgba64> pattern, bool is16Bit)
    {
        Image image = is16Bit ? new Image<Cmyk64>(pattern.Width, pattern.Height) : new Image<Cmyk32>(pattern.Width, pattern.Height);
        for (var y = 0; y < pattern.Height; y++)
        {
            for (var x = 0; x < pattern.Width; x++)
            {
                var p = pattern.Frames[0][x, y];
                if (image is Image<Cmyk64> deep)
                {
                    deep.Frames[0][x, y] = new Cmyk64(p.R, p.G, p.B, (ushort)(x * 100));
                }
                else
                {
                    ((Image<Cmyk32>)image).Frames[0][x, y] = new Cmyk32((byte)(p.R >> 8), (byte)(p.G >> 8), (byte)(p.B >> 8), (byte)x);
                }
            }
        }

        image.Metadata.IccProfile = Metadata.IccProfile.Cgats001CompatibleCmyk;
        return image;
    }

    /// <summary>Gets the stored samples of a gray composite with transparency as RGBA samples, little-endian: the gray repeated, then the transparency.</summary>
    private static byte[] GetMattedGray(PsdPicture picture)
    {
        var size = picture.Depth / 8;
        var count = picture.Width * picture.Height;
        var result = new byte[count * 4 * size];
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < 4; c++)
            {
                var channel = picture.Channels[c == 3 ? 1 : 0];
                for (var b = 0; b < size; b++)
                {
                    result[(((i * 4) + c) * size) + b] = channel[(i * size) + (size - 1 - b)];
                }
            }
        }

        return result;
    }

    /// <summary>Interleaves the stored (big-endian) samples of channels, as psd-tools dumps them: little-endian.</summary>
    private static byte[] Interleave(IReadOnlyList<byte[]> channels, int depth, int count)
    {
        var size = depth / 8;
        var result = new byte[count * channels.Count * size];
        for (var i = 0; i < count; i++)
        {
            for (var c = 0; c < channels.Count; c++)
            {
                for (var b = 0; b < size; b++)
                {
                    result[(((i * channels.Count) + c) * size) + b] = channels[c][(i * size) + (size - 1 - b)];
                }
            }
        }

        return result;
    }

    /// <summary>Gets the straight samples of an image (gray or RGB, then alpha), little-endian, at the depth of the file.</summary>
    private static byte[] GetStraightSamples(Image image, PsdPicture picture)
    {
        var colors = picture.ColorChannelCount;
        var count = picture.Width * picture.Height;
        var size = picture.Depth / 8;
        var result = new byte[count * (colors + 1) * size];
        using var floats = image.CloneAs<RgbaFloat>();
        for (var i = 0; i < count; i++)
        {
            var pixel = floats.Frames[0][i % picture.Width, i / picture.Width];
            float[] values = colors == 1 ? [pixel.R, pixel.A] : [pixel.R, pixel.G, pixel.B, pixel.A];
            for (var c = 0; c < values.Length; c++)
            {
                var destination = result.AsSpan(((i * values.Length) + c) * size);
                switch (picture.Depth)
                {
                    case 8:
                        destination[0] = (byte)Math.Round(values[c] * 255, MidpointRounding.AwayFromZero);
                        break;
                    case 16:
                        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)Math.Round(values[c] * 65535, MidpointRounding.AwayFromZero));
                        break;
                    default:
                        BinaryPrimitives.WriteSingleLittleEndian(destination, values[c]);
                        break;
                }
            }
        }

        return result;
    }

    private static readonly string[] KnownOpenImageIODifferences =
    [
        "psd/duotone8: rejected", // "unsupported color mode 8"
        "psd/graya8-matted: unreadable TIFF", // It names the gray and alpha channels R and G, and writes no ExtraSamples tag
        "psd/indexed-transparent: colors where alpha is 0", // It sets the colors of the transparent pixels to 0
        "psd/resources: rejected", // The thumbnail resource (1036), which the library skips, has zero bits per pixel
        "psd/resources-gray-icc: rejected", // It reads only the image resources of signature 8BIM, not MeSa
        "psd/rgba16-matted-lr16: rounding", // It removes the matte in floating point and rounds the colors differently
        "psd/rgba32-lr32: colors where alpha is 0",
        "psd/rgba8-matted: rounding",
        "psd/rgba8-spot-channel: colors where alpha is 0",
    ];

    // The largest difference of the colors of the pixels whose alpha is not 0, when the alphas are the same
    private static float GetVisibleDifference(Image expected, Image actual)
    {
        using var left = expected.CloneAs<RgbaFloat>();
        using var right = actual.CloneAs<RgbaFloat>();
        var difference = 0f;
        for (var y = 0; y < left.Height; y++)
        {
            for (var x = 0; x < left.Width; x++)
            {
                var a = left.Frames[0][x, y];
                var b = right.Frames[0][x, y];
                if (a.A != b.A)
                    return float.PositiveInfinity;

                if (a.A != 0)
                {
                    difference = Math.Max(difference, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                }
            }
        }

        return difference;
    }

    private static bool SamePixels(Image expected, Image actual)
    {
        if (expected.Size != actual.Size)
            return false;

        // 32-bit documents decode to RgbaFloat, or GrayFloat for gray ones, with samples outside [0, 1]
        if (PixelFormats.IsFloatingPoint(expected.PixelFormat) || PixelFormats.IsFloatingPoint(actual.PixelFormat))
        {
            using var expectedFloats = expected.CloneAs<RgbaFloat>();
            using var actualFloats = actual.CloneAs<RgbaFloat>();
            return GetBytes(expectedFloats).AsSpan().SequenceEqual(GetBytes(actualFloats));
        }

        using var left = expected.CloneAs<Rgba64>();
        using var right = actual.CloneAs<Rgba64>();
        return GetBytes(left).AsSpan().SequenceEqual(GetBytes(right));
    }

    private static void AssertStoredSamples(byte[] data, byte[] dumped, string mode, int channels, int depth, int width, int height)
    {
        var count = width * height;
        if (mode == "CMYK")
        {
            // The inks are the stored samples inverted: the largest one is no ink
            using var inks = Image.Load<Cmyk64>(data);
            var scale = depth == 16 ? 1 : 257;
            for (var i = 0; i < count; i++)
            {
                int Ink(int c) => (depth == 16 ? 65535 : 255) - (depth == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(dumped.AsSpan(((i * 4) + c) * 2)) : dumped[(i * 4) + c]);
                Assert.Equal(new Cmyk64((ushort)(Ink(0) * scale), (ushort)(Ink(1) * scale), (ushort)(Ink(2) * scale), (ushort)(Ink(3) * scale)), inks.Frames[0][i % width, i / width]);
            }

            return;
        }

        if (depth == 32)
        {
            using var floats = Image.Load<RgbaFloat>(data);
            for (var i = 0; i < count; i++)
            {
                float Sample(int c) => BinaryPrimitives.ReadSingleLittleEndian(dumped.AsSpan(((i * channels) + (channels == 1 ? 0 : c)) * 4));
                Assert.Equal(new RgbaFloat(Sample(0), Sample(1), Sample(2), 1f), floats.Frames[0][i % width, i / width]);
            }

            return;
        }

        using var image = Image.Load<Rgba64>(data);
        for (var i = 0; i < count; i++)
        {
            ushort Sample(int c) => depth == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(dumped.AsSpan(((i * channels) + (channels == 1 ? 0 : c)) * 2)) : (ushort)(dumped[(i * channels) + (channels == 1 ? 0 : c)] * 257);
            Assert.Equal(new Rgba64(Sample(0), Sample(1), Sample(2), ushort.MaxValue), image.Frames[0][i % width, i / width]);
        }
    }

    /// <summary>A deterministic pattern of samples in [0, max], with the extremes.</summary>
    private static int[] Pattern(int count, int max, int seed)
    {
        var samples = new int[count];
        for (var i = 0; i < count; i++)
        {
            samples[i] = (i % 11) switch
            {
                0 => 0,
                1 => max,
                _ => (int)(((((long)i * 2654435761L) + (seed * 97)) >> 5) % (max + 1)),
            };
        }

        return samples;
    }

    private static byte[] ToLittleEndian(int[] samples, int depth)
    {
        var bytes = new byte[samples.Length * (depth == 16 ? 2 : 1)];
        for (var i = 0; i < samples.Length; i++)
        {
            if (depth == 16)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), (ushort)samples[i]);
            }
            else
            {
                bytes[i] = (byte)samples[i];
            }
        }

        return bytes;
    }

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
