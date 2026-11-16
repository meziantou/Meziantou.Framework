using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// OpenEXR encoding: the layout of the file (hand-checked bytes of an uncompressed file), the samples of every pixel format
/// read back by the library for every compression, the choice of the pixel type and its reduction, the labels the format
/// can store, and the seekable destination of compressed output. OpenEXR and FFmpeg read the output in the interop tests.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class ExrEncoderTests
{
    [Fact]
    public void AnUncompressedFileHasTheLayoutOfTheSpecification()
    {
        using var image = new Image<RgbaHalf>(2, 2);
        image.Frames[0][1, 0] = new RgbaHalf((Half)1f, (Half)2f, (Half)3f, (Half)0.5f);
        var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.None });

        Assert.Equal([0x76, 0x2F, 0x31, 0x01, 2, 0, 0, 0], data[..8]);
        var attributes = ReadAttributes(data, out var tableOffset);
        Assert.Equal(["channels", "compression", "dataWindow", "displayWindow", "lineOrder", "pixelAspectRatio", "screenWindowCenter", "screenWindowWidth"], attributes.Select(attribute => attribute.Name));
        Assert.Equal("chlist", attributes[0].Type);
        Assert.HasCount((4 * (2 + 16)) + 1, attributes[0].Value);
        Assert.Equal([0], attributes[1].Value);
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0], attributes[2].Value);

        // Two chunks of one line: y, size, then A, B, G and R
        var first = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(tableOffset));
        var second = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(tableOffset + 8));
        Assert.Equal(tableOffset + 16, first);
        Assert.Equal(first + 8 + 16, second);
        Assert.Equal(data.Length, second + 8 + 16);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)first)));
        Assert.Equal(16, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)first + 4)));
        // The colors are premultiplied by alpha
        var samples = data.AsSpan((int)first + 8, 16);
        Assert.Equal((Half)0.5f, BinaryPrimitives.ReadHalfLittleEndian(samples[2..]));
        Assert.Equal((Half)1.5f, BinaryPrimitives.ReadHalfLittleEndian(samples[6..]));
        Assert.Equal((Half)1f, BinaryPrimitives.ReadHalfLittleEndian(samples[10..]));
        Assert.Equal((Half)0.5f, BinaryPrimitives.ReadHalfLittleEndian(samples[14..]));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan((int)second)));
    }

    [Theory]
    [InlineData(OpenExrCompression.None)]
    [InlineData(OpenExrCompression.Rle)]
    [InlineData(OpenExrCompression.Zips)]
    [InlineData(OpenExrCompression.Zip)]
    [InlineData(OpenExrCompression.Piz)]
    public void FloatingPointSamplesAreStoredAsTheyAre(OpenExrCompression compression)
    {
        // Alphas of 0, 1/2 and 1 make the premultiplication exact (a pixel whose alpha is 0 keeps its color)
        float[] specials = [0f, -0f, 1f, -2.5f, 65504f, 1e-3f, 3e38f, float.PositiveInfinity, float.NegativeInfinity, float.NaN];
        foreach (var (width, height) in new[] { (1, 1), (37, 45), (64, 33) })
        {
            var random = new Random(width * height);
            using var image = new Image<RgbaFloat>(width, height);
            using var half = new Image<RgbaHalf>(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    float Next() => (x + y) % 7 == 0 ? specials[random.Next(specials.Length)] : (float)((random.NextDouble() * 8) - 2);
                    var pixel = new RgbaFloat(Next(), Next(), y * 0.25f, (x % 3) * 0.5f);
                    image.Frames[0][x, y] = pixel;
                    half.Frames[0][x, y] = new RgbaHalf((Half)pixel.R, (Half)pixel.G, (Half)pixel.B, (Half)pixel.A);
                }
            }

            AssertSameBits(image, Encode(image, new OpenExrEncoder { Compression = compression }));
            AssertSameBits(half, Encode(half, new OpenExrEncoder { Compression = compression }));
        }
    }

    [Fact]
    public void IntegerPixelsAreStoredAsTheirFloatingPointValues()
    {
        using var rgba = new Image<Rgba32>(5, 3);
        rgba.Metadata.TransferFunction = ColorTransferFunction.Linear;
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                rgba.Frames[0][x, y] = new Rgba32((byte)(x * 60), (byte)(y * 100), 7, (byte)(255 - x));
            }
        }

        // 8-bit samples: half samples (loaded as RgbaFloat, since there is alpha), exact back
        var data = Encode(rgba, new OpenExrEncoder());
        Assert.Equal(16, Image.Identify(data).BitsPerComponent);
        using (var back = Image.Load<Rgba32>(data))
        {
            Assert.Equal(rgba.Frames[0][4, 2], back.Frames[0][4, 2]);
            Assert.Equal(rgba.Frames[0][1, 1], back.Frames[0][1, 1]);
        }

        // 16-bit samples: float samples, exact back
        using var deep = rgba.CloneAs<Rgba64>();
        deep.Frames[0][2, 2] = new Rgba64(12345, 1, 65534, 32768);
        data = Encode(deep, new OpenExrEncoder());
        Assert.Equal(PixelFormat.RgbaFloat, Image.Identify(data).PixelFormat);
        using (var back = Image.Load<Rgba64>(data))
        {
            Assert.Equal(new Rgba64(12345, 1, 65534, 32768), back.Frames[0][2, 2]);
        }

        // Gray: a luminance channel; RGB: no alpha channel
        using var gray = new Image<Gray16>(3, 2);
        gray.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var grayInfo = Image.Identify(Encode(gray, new OpenExrEncoder()));
        Assert.Equal(ImageColorModel.Grayscale, grayInfo.ColorModel);
        Assert.Equal(32, grayInfo.BitsPerComponent);
        using var rgb = new Image<Rgb24>(2, 2);
        rgb.Metadata.TransferFunction = ColorTransferFunction.Linear;
        Assert.Equal(ImageColorModel.Rgb, Image.Identify(Encode(rgb, new OpenExrEncoder { PixelType = OpenExrPixelType.Float })).ColorModel);
    }

    [Fact]
    public void HalfSamplesOfWiderPixelsNeedAnExplicitReduction()
    {
        using var image = new Image<RgbaFloat>(2, 1);
        image.Frames[0][0, 0] = new RgbaFloat(1.0001f, 70000f, float.NaN, 1f);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, new OpenExrEncoder { PixelType = OpenExrPixelType.Half }));

        // 70,000 is beyond the largest half
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(image, new OpenExrEncoder { PixelType = OpenExrPixelType.Half, AllowBitDepthReduction = true }));
        image.Frames[0][0, 0] = new RgbaFloat(1.0001f, 60000f, float.NaN, 1f);
        using var back = Image.Load<RgbaHalf>(Encode(image, new OpenExrEncoder { PixelType = OpenExrPixelType.Half, AllowBitDepthReduction = true }));
        Assert.Equal((Half)1f, back.Frames[0][0, 0].R);
        Assert.Equal((Half)60000f, back.Frames[0][0, 0].G);
        Assert.True(Half.IsNaN(back.Frames[0][0, 0].B));

        // Wider samples are always exact; FloatingPointConversion does not apply
        using var half = new Image<RgbaHalf>(1, 1);
        half.Frames[0][0, 0] = new RgbaHalf((Half)5f, (Half)0f, (Half)0f, (Half)1f);
        using var wide = Image.Load<RgbaFloat>(Encode(half, new OpenExrEncoder { PixelType = OpenExrPixelType.Float, FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip } }));
        Assert.Equal(5f, wide.Frames[0][0, 0].R);
    }

    [Fact]
    public void OnlyLinearLightCanBeStored()
    {
        // An sRGB label (the default of integer pixels), an ICC profile or another transfer function would change the meaning of the samples
        using var srgb = new Image<Rgba32>(1, 1);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(srgb, new OpenExrEncoder()));
        Assert.Equal(ImageFormat.OpenExr, Image.Identify(Encode(srgb, new OpenExrEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported })).Format);

        using var profiled = new Image<RgbaFloat>(1, 1);
        profiled.Metadata.IccProfile = IccProfile.Srgb;
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(profiled, new OpenExrEncoder()));

        using var pq = new Image<RgbaFloat>(1, 1);
        pq.Metadata.Cicp = new CicpColorDescription(9, 16, 0, fullRange: true);
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(pq, new OpenExrEncoder()));

        using var text = new Image<RgbaFloat>(1, 1);
        text.Metadata.TextEntries.Add(new ImageTextEntry("Comment", "hello"));
        Assert.Throws<UnsupportedImageFeatureException>(() => Encode(text, new OpenExrEncoder()));

        // Linear BT.2020: the chromaticities attribute, read back as the same description
        using var bt2020 = new Image<RgbaFloat>(1, 1);
        bt2020.Metadata.Cicp = new CicpColorDescription(9, 8, 0, fullRange: true);
        var data = Encode(bt2020, new OpenExrEncoder());
        var chromaticities = ReadAttributes(data, out _).Single(attribute => attribute.Name == "chromaticities");
        Assert.Equal("chromaticities", chromaticities.Type);
        Assert.Equal(0.708f, BinaryPrimitives.ReadSingleLittleEndian(chromaticities.Value));
        using var back = Image.Load(data);
        Assert.Equal(new CicpColorDescription(9, 8, 0, fullRange: true), back.Metadata.Cicp);
    }

    [Fact]
    public void CompressedOutputNeedsASeekableDestination()
    {
        using var image = new Image<RgbaHalf>(3, 3);
        Assert.Throws<ArgumentException>(() => image.Save(new TestOutputStream(), new OpenExrEncoder()));
        using var stream = new TestOutputStream();
        image.Save(stream, new OpenExrEncoder { Compression = OpenExrCompression.None });
        Assert.Equal(ImageFormat.OpenExr, Image.DetectFormat(stream.ToArray()));
    }

    [Fact]
    public void DataThatDoNotShrinkAreStoredAsTheyAre()
    {
        var random = new Random(3);
        using var image = new Image<RgbaFloat>(16, 16);
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                image.Frames[0][x, y] = new RgbaFloat((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
            }
        }

        var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.Rle });
        ReadAttributes(data, out var tableOffset);
        var offset = (int)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(tableOffset));
        Assert.Equal(16 * 4 * 4, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4)));
        using var back = Image.Load<RgbaFloat>(data);
        Assert.Equal(image.Frames[0][5, 7].A, back.Frames[0][5, 7].A);
    }

    [Fact]
    public void TheExtensionSelectsTheEncoder()
    {
        var directory = Directory.CreateTempSubdirectory("meziantou-exr-");
        try
        {
            using var image = new Image<RgbaHalf>(4, 4);
            var path = Path.Combine(directory.FullName, "image.EXR");
            image.Save(path);
            Assert.Equal(ImageFormat.OpenExr, Image.Identify(path).Format);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void InvalidSettingsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenExrEncoder { Compression = (OpenExrCompression)10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenExrEncoder { PixelType = (OpenExrPixelType)2 });
        Assert.Equal(OpenExrCompression.Zip, new OpenExrEncoder().Compression);
        Assert.Null(new OpenExrEncoder().PixelType);

        // A random order is only valid for tiles; nothing is written
        using var image = new Image<RgbaHalf>(2, 2);
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentException>(() => image.Save(stream, new OpenExrEncoder { LineOrder = OpenExrLineOrder.RandomY }));
        Assert.Equal(0, stream.Length);
        Assert.Equal(ImageFormat.OpenExr, Image.Identify(Encode(image, new OpenExrEncoder { LineOrder = OpenExrLineOrder.RandomY, Tiling = new OpenExrTiling() })).Format);
    }

    /// <summary>
    /// The chunks of an image of at least 65,536 pixels are coded by several workers when the configuration allows it: the
    /// file is the one of one worker, for every compression, float and half samples, pixels converted to floats first, a
    /// partial last chunk (300x251 pixels) and a gray image.
    /// </summary>
    [Theory]
    [InlineData(OpenExrCompression.None)]
    [InlineData(OpenExrCompression.Rle)]
    [InlineData(OpenExrCompression.Zips)]
    [InlineData(OpenExrCompression.Zip)]
    [InlineData(OpenExrCompression.Piz)]
    [InlineData(OpenExrCompression.Pxr24)]
    [InlineData(OpenExrCompression.B44)]
    [InlineData(OpenExrCompression.B44A)]
    [InlineData(OpenExrCompression.Dwaa)]
    [InlineData(OpenExrCompression.Dwab)]
    public void FilesWrittenByWorkersAreTheFilesOfOneWorker(OpenExrCompression compression)
    {
        const int Width = 300;
        const int Height = 251;
        var random = new Random(251);
        var floats = new RgbaFloat[Width * Height];
        var bytes = new Rgb24[Width * Height];
        var grays = new Gray16[Width * Height];
        for (var i = 0; i < floats.Length; i++)
        {
            // Smooth colors with some noise, so that the chunks shrink by different amounts
            var x = i % Width;
            var y = i / Width;
            floats[i] = new RgbaFloat((x / 100f) + (float)random.NextDouble(), y / 50f, (float)random.NextDouble() * 4, (x + y) % 3 * 0.5f);
            bytes[i] = new Rgb24((byte)x, (byte)(y + random.Next(4)), (byte)random.Next(256));
            grays[i] = new Gray16((ushort)((x * y) + random.Next(16)));
        }

        foreach (var (pixels, pixelType) in new (Func<ImageConfiguration, Image> Create, OpenExrPixelType? PixelType)[]
        {
            (configuration => Image.ImportPixelData<RgbaFloat>(floats, Width, Height, configuration: configuration), null),
            (configuration => Image.ImportPixelData<RgbaFloat>(floats, Width, Height, configuration: configuration), OpenExrPixelType.Half),
            (configuration => Image.ImportPixelData<Rgb24>(bytes, Width, Height, configuration: configuration), null),
            (configuration => Image.ImportPixelData<Gray16>(grays, Width, Height, configuration: configuration), null),
        })
        {
            foreach (var tiling in new OpenExrTiling?[] { null, new() { TileWidth = 64, TileHeight = 40, LevelMode = OpenExrLevelMode.MipmapLevels } })
            {
                var encoder = new OpenExrEncoder { Compression = compression, PixelType = pixelType, AllowBitDepthReduction = pixelType is not null, Tiling = tiling, LineOrder = OpenExrLineOrder.DecreasingY };
                var expected = EncodeWith(encoder, 1);
                foreach (var workers in (int[])[2, 3, 16])
                {
                    Assert.Equal(expected, EncodeWith(encoder, workers));
                }
            }

            byte[] EncodeWith(OpenExrEncoder encoder, int workers)
            {
                using var image = pixels(new ImageConfiguration { MaxDegreeOfParallelism = workers });
                image.Metadata.TransferFunction = ColorTransferFunction.Linear;
                return Encode(image, encoder);
            }
        }
    }

    /// <summary>
    /// The samples of an image of at least 65,536 pixels are checked by several workers, which report the first sample of the
    /// image that half samples cannot hold, as one worker does, and write nothing.
    /// </summary>
    [Fact]
    public void ValidationByWorkersReportsTheFirstSampleBeyondTheRange()
    {
        foreach (var workers in (int[])[1, 3, 16])
        {
            using var image = new Image<RgbaFloat>(300, 251, new RgbaFloat(0.5f, 1f, 2f, 1f), new ImageConfiguration { MaxDegreeOfParallelism = workers });
            image.Frames[0][17, 200] = new RgbaFloat(1e6f, 0f, 0f, 1f);
            image.Frames[0][5, 120] = new RgbaFloat(0f, 70000f, 0f, 1f);
            using var stream = new MemoryStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new OpenExrEncoder { PixelType = OpenExrPixelType.Half, AllowBitDepthReduction = true }));
            Assert.Contains("The color 70000 of the row 120 ", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, stream.Length);
        }
    }

    /// <summary>The workers stop when the encoding is canceled.</summary>
    [Fact]
    public async Task WorkersHonorCancellation()
    {
        foreach (var workers in (int[])[1, 16])
        {
            using var image = new Image<RgbaHalf>(300, 251, new RgbaHalf((Half)0.5f, (Half)1f, (Half)2f, Half.One), new ImageConfiguration { MaxDegreeOfParallelism = workers });
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
            using var cancellation = new CancellationTokenSource();
            using var stream = new CancelingStream(cancellation, threshold: 50_000);
            await Assert.ThrowsAny<OperationCanceledException>(() => image.SaveAsync(stream, new OpenExrEncoder { Compression = OpenExrCompression.None }, cancellation.Token));
        }
    }

    [Theory]
    [InlineData(PixelFormat.GrayHalf, PixelFormat.GrayHalf, 16)]
    [InlineData(PixelFormat.GrayFloat, PixelFormat.GrayFloat, 32)]
    [InlineData(PixelFormat.LaHalf, PixelFormat.LaFloat, 16)]
    [InlineData(PixelFormat.LaFloat, PixelFormat.LaFloat, 32)]
    public void FloatingPointGrayPixelsAreStoredAsALuminanceChannel(PixelFormat format, PixelFormat loadedFormat, int bits)
    {
        // Highlights and negative levels are kept; alphas that are powers of two premultiply exactly
        LaFloat[] pixels = [new(0, 1), new(0.25f, 0.5f), new(0.5f, 0.25f), new(1, 1), new(2.5f, 0.5f), new(-0.75f, 1)];
        using var source = Image.ImportPixelData<LaFloat>(pixels, 3, 2);
        // The formats without alpha get the levels flattened onto black
        using var image = source.CloneAs(format, new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var data = Encode(image, new OpenExrEncoder());

        var info = Image.Identify(data);
        var hasAlpha = PixelFormats.HasAlpha(format);
        Assert.Equal((loadedFormat, hasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale, bits), (info.PixelFormat, info.ColorModel, info.BitsPerComponent));

        using var loaded = Image.Load(data);
        Assert.Equal(loadedFormat, loaded.PixelFormat);
        using var expected = image.CloneAs<LaFloat>();
        using var actual = loaded.CloneAs<LaFloat>();
        var (expectedPixels, actualPixels) = (new LaFloat[6], new LaFloat[6]);
        expected.Frames[0].CopyPixelDataTo(expectedPixels);
        actual.Frames[0].CopyPixelDataTo(actualPixels);
        Assert.Equal(expectedPixels, actualPixels);
    }

    private static byte[] Encode(Image image, OpenExrEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static void AssertSameBits(Image expected, byte[] data)
    {
        // Images with alpha load as RgbaFloat, whose values a half image converts to exactly
        using var actual = Image.Load(data);
        using var left = expected is Image<RgbaHalf> half ? half.CloneAs<RgbaFloat>() : ((Image<RgbaFloat>)expected).CloneAs<RgbaFloat>();
        using var right = actual is Image<RgbaHalf> other ? other.CloneAs<RgbaFloat>() : ((Image<RgbaFloat>)actual).CloneAs<RgbaFloat>();
        var leftBytes = new byte[expected.Width * expected.Height * 16];
        var rightBytes = new byte[leftBytes.Length];
        left.Frames[0].CopyPixelBytesTo(leftBytes);
        right.Frames[0].CopyPixelBytesTo(rightBytes);
        Assert.Equal(leftBytes, rightBytes);
    }

    [Fact]
    public void ColorsArePremultipliedByAlpha()
    {
        // Straight colors are multiplied by alpha, and divided back when read; a pixel whose alpha is 0 keeps its color
        using var image = new Image<RgbaFloat>(3, 1);
        image.Frames[0][0, 0] = new RgbaFloat(4f, 2f, 1f, 0.25f);
        image.Frames[0][1, 0] = new RgbaFloat(3f, 5f, 7f, 0f);
        image.Frames[0][2, 0] = new RgbaFloat(0.1f, 0.2f, 0.3f, 0.3f);
        var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.None });
        ReadAttributes(data, out var tableOffset);
        var start = (int)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(tableOffset)) + 8;
        float Sample(int channel, int x) => BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(start + (4 * ((channel * 3) + x))));
        Assert.Equal([0.25f, 0f, 0.3f], [Sample(0, 0), Sample(0, 1), Sample(0, 2)]); // A
        Assert.Equal([0.25f, 7f, (float)((double)0.3f * 0.3f)], [Sample(1, 0), Sample(1, 1), Sample(1, 2)]); // B
        Assert.Equal([1f, 3f, (float)((double)0.1f * 0.3f)], [Sample(3, 0), Sample(3, 1), Sample(3, 2)]); // R

        using var back = Image.Load<RgbaFloat>(data);
        Assert.Equal(image.Frames[0][0, 0], back.Frames[0][0, 0]);
        Assert.Equal(image.Frames[0][1, 0], back.Frames[0][1, 0]);
        Assert.InRange(back.Frames[0][2, 0].R, 0.1f - 1e-8f, 0.1f + 1e-8f);
    }

    [Theory]
    [InlineData(PixelFormat.RgbaFloat, OpenExrPixelType.Float)]
    [InlineData(PixelFormat.RgbaFloat, OpenExrPixelType.Half)]
    [InlineData(PixelFormat.RgbaHalf, OpenExrPixelType.Half)]
    [InlineData(PixelFormat.RgbaHalf, OpenExrPixelType.Float)]
    public void PremultipliedColorsAreRoundedOnceAndDividedByTheStoredAlpha(PixelFormat format, OpenExrPixelType pixelType)
    {
        // Any alpha: the color is multiplied by the alpha that is stored (rounded to the sample type), the product rounded
        // to the sample type, then divided by the stored alpha when read. An alpha that rounds to a half 0 keeps its color.
        const int Width = 61;
        var random = new Random(42);
        using var source = new Image<RgbaFloat>(Width, 3);
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var alpha = x switch
                {
                    0 => 1e-8f,
                    1 => 1.75f,
                    2 => 3e-5f,
                    _ => (float)random.NextDouble(),
                };
                source.Frames[0][x, y] = new RgbaFloat((float)((random.NextDouble() * 200) - 100), (float)random.NextDouble(), (float)(random.NextDouble() * 1e-3), alpha);
            }
        }

        using var image = format == PixelFormat.RgbaHalf ? (Image)source.CloneAs<RgbaHalf>() : source.CloneAs<RgbaFloat>();
        using var pixels = image.CloneAs<RgbaFloat>();
        var data = Encode(image, new OpenExrEncoder { PixelType = pixelType, AllowBitDepthReduction = true });
        using var back = Image.Load<RgbaFloat>(data);
        var inexact = 0;
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = pixels.Frames[0][x, y];
                var actual = back.Frames[0][x, y];
                double alpha = pixelType == OpenExrPixelType.Half ? (double)(Half)pixel.A : pixel.A;
                Assert.Equal((float)alpha, actual.A);
                Assert.Equal(Expected(pixel.R), actual.R);
                Assert.Equal(Expected(pixel.G), actual.G);
                Assert.Equal(Expected(pixel.B), actual.B);
                if (actual != pixel)
                {
                    inexact++;
                }

                float Expected(float color)
                {
                    if (alpha == 0)
                        return pixelType == OpenExrPixelType.Half ? (float)(Half)color : color;

                    var stored = pixelType == OpenExrPixelType.Half ? (double)(Half)(color * alpha) : (float)(color * alpha);
                    return (float)(stored / alpha);
                }
            }
        }

        // The round trip is not exact in general, but the product of two halves is exact in a float
        Assert.Equal(format == PixelFormat.RgbaHalf && pixelType == OpenExrPixelType.Float, inexact == 0);
    }

    [Fact]
    public void PremultipliedColorsBeyondTheRangeOfTheSampleTypeAreRejected()
    {
        // Alpha is not limited to [0, 1]: 60,000 is a half, but 60,000 × 2 is not
        using var half = new Image<RgbaHalf>(2, 1);
        half.Frames[0][1, 0] = new RgbaHalf((Half)1f, (Half)(-60000f), (Half)0f, (Half)2f);
        AssertRejected(half, new OpenExrEncoder());
        using (var wide = Image.Load<RgbaFloat>(Encode(half, new OpenExrEncoder { PixelType = OpenExrPixelType.Float })))
        {
            Assert.Equal(new RgbaFloat(1f, -60000f, 0f, 2f), wide.Frames[0][1, 0]);
        }

        // The same for float samples, and for half samples of floats whose alpha is rounded first
        using var single = new Image<RgbaFloat>(2, 1);
        single.Frames[0][1, 0] = new RgbaFloat(3e38f, 0f, 0f, 2f);
        AssertRejected(single, new OpenExrEncoder());
        single.Frames[0][1, 0] = new RgbaFloat(0f, 0f, 40000f, 1.9f);
        AssertRejected(single, new OpenExrEncoder { PixelType = OpenExrPixelType.Half, AllowBitDepthReduction = true });

        // A finite color multiplied by an alpha that is not finite would be lost
        single.Frames[0][1, 0] = new RgbaFloat(0f, 0.5f, 0f, float.PositiveInfinity);
        AssertRejected(single, new OpenExrEncoder());
        single.Frames[0][1, 0] = new RgbaFloat(0.5f, 0f, 0f, float.NaN);
        AssertRejected(single, new OpenExrEncoder());

        // Colors that are not finite stay as they are, and a zero alpha does not multiply its colors
        single.Frames[0][0, 0] = new RgbaFloat(float.PositiveInfinity, float.NaN, 0f, 2f);
        single.Frames[0][1, 0] = new RgbaFloat(3e38f, -3e38f, 0f, 0f);
        using var back = Image.Load<RgbaFloat>(Encode(single, new OpenExrEncoder()));
        Assert.Equal(float.PositiveInfinity, back.Frames[0][0, 0].R);
        Assert.True(float.IsNaN(back.Frames[0][0, 0].G));
        Assert.Equal(single.Frames[0][1, 0], back.Frames[0][1, 0]);

        static void AssertRejected(Image image, OpenExrEncoder encoder)
        {
            using var stream = new MemoryStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, encoder));
            Assert.Equal(ImageFormat.OpenExr, exception.Format);
            Assert.Contains("multiplied by its alpha", exception.Message, StringComparison.Ordinal);
            Assert.Equal(0, stream.Length);
        }
    }

    [Fact]
    public void AnUncompressedTiledFileHasTheLayoutOfTheSpecification()
    {
        // 3x2 pixels in tiles of 2x2: two tiles, the right one clipped to 1x2
        using var image = new Image<RgbaHalf>(3, 2);
        image.Frames[0][2, 1] = new RgbaHalf((Half)1f, (Half)2f, (Half)3f, (Half)1f);
        var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.None, Tiling = new OpenExrTiling { TileWidth = 2, TileHeight = 2 } });

        Assert.Equal([0x76, 0x2F, 0x31, 0x01, 2, 2, 0, 0], data[..8]);
        var attributes = ReadAttributes(data, out var tableOffset);
        var tiles = attributes.Single(attribute => attribute.Name == "tiles");
        Assert.Equal("tiledesc", tiles.Type);
        Assert.Equal([2, 0, 0, 0, 2, 0, 0, 0, 0], tiles.Value);

        // Each tile: its coordinates, its level, its size, then for each line A, B, G and R
        var chunks = ReadTiles(data, tableOffset, 2);
        Assert.Equal((0, 0, 0, 0), chunks[0].Coordinates);
        Assert.HasCount(2 * 2 * 4 * 2, chunks[0].Data);
        Assert.Equal((1, 0, 0, 0), chunks[1].Coordinates);
        Assert.HasCount(1 * 2 * 4 * 2, chunks[1].Data);
        Assert.Equal(data.Length, chunks[1].Offset + 20 + chunks[1].Data.Length);
        var line = chunks[1].Data[8..];
        Assert.Equal([(Half)1f, (Half)3f, (Half)2f, (Half)1f], new[] { 0, 2, 4, 6 }.Select(i => BinaryPrimitives.ReadHalfLittleEndian(line.AsSpan(i))));
    }

    public static TheoryData<OpenExrCompression, int, int, OpenExrLevelMode, OpenExrLevelRounding, OpenExrLineOrder> TiledCases()
    {
        var data = new TheoryData<OpenExrCompression, int, int, OpenExrLevelMode, OpenExrLevelRounding, OpenExrLineOrder>();
        foreach (var compression in (OpenExrCompression[])[OpenExrCompression.None, OpenExrCompression.Rle, OpenExrCompression.Zips, OpenExrCompression.Zip, OpenExrCompression.Piz])
        {
            data.Add(compression, 16, 16, OpenExrLevelMode.OneLevel, OpenExrLevelRounding.Down, OpenExrLineOrder.IncreasingY);
            data.Add(compression, 5, 3, OpenExrLevelMode.MipmapLevels, OpenExrLevelRounding.Down, OpenExrLineOrder.DecreasingY);
            data.Add(compression, 7, 64, OpenExrLevelMode.RipmapLevels, OpenExrLevelRounding.Up, OpenExrLineOrder.RandomY);
        }

        data.Add(OpenExrCompression.Zip, 1, 1, OpenExrLevelMode.MipmapLevels, OpenExrLevelRounding.Up, OpenExrLineOrder.IncreasingY);
        data.Add(OpenExrCompression.Piz, 100, 100, OpenExrLevelMode.RipmapLevels, OpenExrLevelRounding.Down, OpenExrLineOrder.DecreasingY);
        return data;
    }

    [Theory]
    [MemberData(nameof(TiledCases))]
    public void TiledFilesStoreTheSamplesOfTheImage(OpenExrCompression compression, int tileWidth, int tileHeight, OpenExrLevelMode levelMode, OpenExrLevelRounding rounding, OpenExrLineOrder lineOrder)
    {
        // The full-resolution level decodes to the samples of the image, whatever the tiles, the levels and the order
        foreach (var (width, height) in new[] { (1, 1), (37, 45), (64, 33) })
        {
            var random = new Random(width * height);
            using var image = new Image<RgbaFloat>(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    image.Frames[0][x, y] = new RgbaFloat((float)((random.NextDouble() * 8) - 2), x * 0.5f, y * 0.25f, (x % 3) * 0.5f);
                }
            }

            using var half = image.CloneAs<RgbaHalf>();
            var tiling = new OpenExrTiling { TileWidth = tileWidth, TileHeight = tileHeight, LevelMode = levelMode, LevelRounding = rounding };
            var data = Encode(image, new OpenExrEncoder { Compression = compression, Tiling = tiling, LineOrder = lineOrder });
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
            Assert.Equal((width, height), (info.Width, info.Height));
            AssertSameBits(image, data);
            AssertSameBits(half, Encode(half, new OpenExrEncoder { Compression = compression, Tiling = tiling, LineOrder = lineOrder }));
        }
    }

    [Fact]
    public void MipmapLevelsAreAreaAveragesOfTheStoredSamples()
    {
        // 5x4 pixels with alpha, levels rounded down: 2x2, then 1x1. The colors are premultiplied before they are averaged
        using var image = new Image<RgbaFloat>(5, 4);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                image.Frames[0][x, y] = new RgbaFloat(x + (10 * y), 1f, -x, (x + y) % 2 == 0 ? 1f : 0.5f);
            }
        }

        var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.None, PixelType = OpenExrPixelType.Float, Tiling = new OpenExrTiling { TileWidth = 8, TileHeight = 8, LevelMode = OpenExrLevelMode.MipmapLevels } });
        ReadAttributes(data, out var tableOffset);
        var chunks = ReadTiles(data, tableOffset, 3);
        Assert.Equal([(0, 0, 0, 0), (0, 0, 1, 1), (0, 0, 2, 2)], chunks.Select(chunk => chunk.Coordinates));
        Assert.HasCount(2 * 2 * 4 * 4, chunks[1].Data);
        Assert.HasCount(4 * 4, chunks[2].Data);

        // The stored samples of the full resolution, then their area average over 2.5x2 pixels and over the whole image
        double Stored(int x, int y, int component)
        {
            var pixel = image.Frames[0][x, y];
            return component switch
            {
                0 => pixel.R * pixel.A,
                1 => pixel.G * pixel.A,
                2 => pixel.B * pixel.A,
                _ => pixel.A,
            };
        }

        double Average(double left, double right, double top, double bottom, int component)
        {
            double sum = 0;
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 5; x++)
                {
                    var width = Math.Max(0, Math.Min(right, x + 1) - Math.Max(left, x));
                    var height = Math.Max(0, Math.Min(bottom, y + 1) - Math.Max(top, y));
                    sum += width * height * Stored(x, y, component);
                }
            }

            return sum / ((right - left) * (bottom - top));
        }

        // A, B, G, R in name order
        int[] components = [3, 2, 1, 0];
        for (var y = 0; y < 2; y++)
        {
            for (var c = 0; c < 4; c++)
            {
                for (var x = 0; x < 2; x++)
                {
                    var actual = BinaryPrimitives.ReadSingleLittleEndian(chunks[1].Data.AsSpan(4 * ((((y * 4) + c) * 2) + x)));
                    Assert.Equal((float)Average(x * 2.5, (x + 1) * 2.5, y * 2, (y + 1) * 2, components[c]), actual, 5);
                }
            }
        }

        for (var c = 0; c < 4; c++)
        {
            Assert.Equal((float)Average(0, 5, 0, 4, components[c]), BinaryPrimitives.ReadSingleLittleEndian(chunks[2].Data.AsSpan(4 * c)), 5);
        }
    }

    [Fact]
    public void HalfLevelsAverageTheHalvesOfTheirParent()
    {
        // 4x1 opaque pixels: the 2x1 level stores the halves of 1.00048828125 (a tie, rounded to the even half 1) and
        // 1.0009765625; the 1x1 level averages these halves, a tie again, not the unrounded averages (1.000732421875)
        using var image = new Image<RgbaFloat>(4, 1);
        float[] reds = [1f, 1.0009765625f, 1.0009765625f, 1.0009765625f];
        for (var x = 0; x < 4; x++)
        {
            image.Frames[0][x, 0] = new RgbaFloat(reds[x], 0f, 0f, 1f);
        }

        var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.None, PixelType = OpenExrPixelType.Half, AllowBitDepthReduction = true, Tiling = new OpenExrTiling { TileWidth = 4, TileHeight = 4, LevelMode = OpenExrLevelMode.MipmapLevels } });
        ReadAttributes(data, out var tableOffset);
        var chunks = ReadTiles(data, tableOffset, 3);

        // A, B, G, R in name order: R is the last plane of the line
        Assert.Equal([(Half)1f, (Half)1.0009765625f], new[] { 12, 14 }.Select(i => BinaryPrimitives.ReadHalfLittleEndian(chunks[1].Data.AsSpan(i))));
        Assert.Equal((Half)1f, BinaryPrimitives.ReadHalfLittleEndian(chunks[2].Data.AsSpan(6)));
    }

    [Fact]
    public void RipmapLevelsHaveEveryPairOfSizes()
    {
        // 4x2 pixels: widths 4, 2, 1 and heights 2, 1, in the order of the rows of levels
        using var image = new Image<RgbaHalf>(4, 2, new RgbaHalf((Half)0.5f, (Half)0.25f, (Half)2f, (Half)1f));
        var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.None, Tiling = new OpenExrTiling { TileWidth = 2, TileHeight = 2, LevelMode = OpenExrLevelMode.RipmapLevels } });
        ReadAttributes(data, out var tableOffset);
        var chunks = ReadTiles(data, tableOffset, 8);
        Assert.Equal([(0, 0, 0, 0), (1, 0, 0, 0), (0, 0, 1, 0), (0, 0, 2, 0), (0, 0, 0, 1), (1, 0, 0, 1), (0, 0, 1, 1), (0, 0, 2, 1)], chunks.Select(chunk => chunk.Coordinates));
        Assert.Equal(data.Length, chunks[^1].Offset + 20 + chunks[^1].Data.Length);

        // A flat image has flat levels
        foreach (var chunk in chunks)
        {
            Assert.Equal((Half)1f, BinaryPrimitives.ReadHalfLittleEndian(chunk.Data));
            Assert.Equal((Half)0.5f, BinaryPrimitives.ReadHalfLittleEndian(chunk.Data.AsSpan(chunk.Data.Length - 2)));
        }
    }

    [Fact]
    public void ChunksAreWrittenInTheLineOrder()
    {
        // The offset table is in the order of the chunks; the file holds them from the bottom for DecreasingY
        using var image = new Image<RgbaHalf>(4, 40);
        foreach (var tiling in new OpenExrTiling?[] { null, new() { TileWidth = 2, TileHeight = 16 } })
        {
            var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.Piz, LineOrder = OpenExrLineOrder.DecreasingY, Tiling = tiling });
            var attributes = ReadAttributes(data, out var tableOffset);
            Assert.Equal([1], attributes.Single(attribute => attribute.Name == "lineOrder").Value);
            var count = tiling is null ? 2 : 6;
            var offsets = Enumerable.Range(0, count).Select(i => BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(tableOffset + (8 * i)))).ToArray();
            Assert.Equal(tableOffset + (8 * count), offsets.Min());
            Assert.True(offsets[^1] < offsets[0]);
            using var back = Image.Load<RgbaHalf>(data);
            Assert.Equal(image.Frames[0][3, 39], back.Frames[0][3, 39]);
        }
    }

    public static TheoryData<OpenExrCompression, OpenExrPixelType, double> LossyCases() => new()
    {
        { OpenExrCompression.Pxr24, OpenExrPixelType.Half, 0 },
        { OpenExrCompression.Pxr24, OpenExrPixelType.Float, 1.0 / 32768 },
        { OpenExrCompression.B44, OpenExrPixelType.Half, 1.0 / 64 },
        { OpenExrCompression.B44A, OpenExrPixelType.Half, 1.0 / 64 },
        { OpenExrCompression.B44, OpenExrPixelType.Float, 0 },
        { OpenExrCompression.Dwaa, OpenExrPixelType.Half, 0.05 },
        { OpenExrCompression.Dwab, OpenExrPixelType.Half, 0.05 },
        { OpenExrCompression.Dwaa, OpenExrPixelType.Float, 0.05 },
    };

    [Theory]
    [MemberData(nameof(LossyCases))]
    public void LossyCompressionsStaySmallAndCloseToTheSource(OpenExrCompression compression, OpenExrPixelType pixelType, double tolerance)
    {
        // A smooth opaque image with some noise: every sample within the relative tolerance; alpha is exact but for B44 halves
        const int Width = 70;
        const int Height = 45;
        var random = new Random(70);
        using var image = new Image<RgbaFloat>(Width, Height);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var noise = (float)random.NextDouble() * 0.01f;
                image.Frames[0][x, y] = new RgbaFloat(0.1f + (x / 70f) + noise, 0.5f + (0.4f * MathF.Sin(y / 7f)), 2f + (y / 45f), 1f);
            }
        }

        foreach (var tiling in new OpenExrTiling?[] { null, new() { TileWidth = 32, TileHeight = 16 } })
        {
            var data = Encode(image, new OpenExrEncoder { Compression = compression, PixelType = pixelType, AllowBitDepthReduction = true, Tiling = tiling });
            Assert.Equal([(byte)compression], ReadAttributes(data, out _).Single(attribute => attribute.Name == "compression").Value);
            var lossless = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.None, PixelType = pixelType, AllowBitDepthReduction = true, Tiling = tiling });
            if (tolerance > 0 && compression != OpenExrCompression.Pxr24)
            {
                Assert.HasCountLessThan(lossless.Length / 2, data);
            }

            using var back = Image.Load<RgbaFloat>(data);
            using var source = Image.Load<RgbaFloat>(lossless);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var expected = source.Frames[0][x, y];
                    var actual = back.Frames[0][x, y];
                    foreach (var (left, right) in new[] { (expected.R, actual.R), (expected.G, actual.G), (expected.B, actual.B), (expected.A, actual.A) })
                    {
                        Assert.True(Math.Abs(left - right) <= tolerance * Math.Max(1, Math.Abs(left)), $"({x}, {y}): {left} {right}");
                    }
                }
            }
        }
    }

    [Fact]
    public void TheDwaLevelTradesSizeForErrors()
    {
        // Larger levels give smaller files; the level is written as the dwaCompressionLevel attribute
        var random = new Random(5);
        using var image = new Image<RgbaFloat>(64, 64);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                image.Frames[0][x, y] = new RgbaFloat((float)random.NextDouble(), (x + y) / 128f, 0.5f, 1f);
            }
        }

        var sizes = new List<int>();
        foreach (var level in new[] { 0f, 45f, 300f })
        {
            var data = Encode(image, new OpenExrEncoder { Compression = OpenExrCompression.Dwab, DwaCompressionLevel = level, PixelType = OpenExrPixelType.Half, AllowBitDepthReduction = true });
            Assert.Equal(level, BinaryPrimitives.ReadSingleLittleEndian(ReadAttributes(data, out _).Single(attribute => attribute.Name == "dwaCompressionLevel").Value));
            sizes.Add(data.Length);
        }

        Assert.True(sizes[0] > sizes[1] && sizes[1] > sizes[2], string.Join(' ', sizes));
    }

    private static List<(long Offset, (int Tx, int Ty, int Lx, int Ly) Coordinates, byte[] Data)> ReadTiles(byte[] data, int tableOffset, int count)
    {
        var result = new List<(long, (int, int, int, int), byte[])>();
        for (var i = 0; i < count; i++)
        {
            var offset = (int)BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(tableOffset + (8 * i)));
            var fields = data.AsSpan(offset);
            var coordinates = (BinaryPrimitives.ReadInt32LittleEndian(fields), BinaryPrimitives.ReadInt32LittleEndian(fields[4..]), BinaryPrimitives.ReadInt32LittleEndian(fields[8..]), BinaryPrimitives.ReadInt32LittleEndian(fields[12..]));
            var size = BinaryPrimitives.ReadInt32LittleEndian(fields[16..]);
            result.Add((offset, coordinates, fields.Slice(20, size).ToArray()));
        }

        return result;
    }

    private static List<(string Name, string Type, byte[] Value)> ReadAttributes(byte[] data, out int tableOffset)
    {
        var result = new List<(string, string, byte[])>();
        var position = 8;
        while (data[position] != 0)
        {
            var name = ReadString(data, ref position);
            var type = ReadString(data, ref position);
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(position));
            result.Add((name, type, data.AsSpan(position + 4, size).ToArray()));
            position += 4 + size;
        }

        tableOffset = position + 1;
        return result;

        static string ReadString(byte[] data, ref int position)
        {
            var end = Array.IndexOf(data, (byte)0, position);
            var value = Encoding.ASCII.GetString(data, position, end - position);
            position = end + 1;
            return value;
        }
    }

    /// <summary>A stream that cancels a token once it has received a number of bytes.</summary>
    private sealed class CancelingStream(CancellationTokenSource cancellation, int threshold) : MemoryStream
    {
        // The span and asynchronous writes of a derived memory stream end in this one
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            if (Length >= threshold)
            {
                cancellation.Cancel();
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
    }
}
