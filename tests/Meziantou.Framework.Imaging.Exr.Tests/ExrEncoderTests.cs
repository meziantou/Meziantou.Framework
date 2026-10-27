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
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenExrEncoder { Compression = (OpenExrCompression)5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenExrEncoder { PixelType = (OpenExrPixelType)2 });
        Assert.Equal(OpenExrCompression.Zip, new OpenExrEncoder().Compression);
        Assert.Null(new OpenExrEncoder().PixelType);
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
}
