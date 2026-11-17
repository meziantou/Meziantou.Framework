using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Dpx;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// DPX encoding: the headers are read with the offsets of SMPTE ST 268, and the image data compared with the packing of
/// <see cref="DpxFileBuilder.PackLine"/>, independent of the library.
/// </summary>
public sealed class DpxEncoderTests
{
    [Theory]
    [InlineData(PixelFormat.Rgb24, 50, 8)]
    [InlineData(PixelFormat.Rgb48, 50, 16)]
    [InlineData(PixelFormat.Gray8, 6, 8)]
    [InlineData(PixelFormat.Gray16, 6, 16)]
    [InlineData(PixelFormat.RgbaFloat, 50, 32)]
    [InlineData(PixelFormat.RgbaHalf, 50, 32)]
    [InlineData(PixelFormat.GrayFloat, 6, 32)]
    [InlineData(PixelFormat.GrayHalf, 6, 32)]
    public void TheDefaultDepthIsTheOneOfThePixelFormat(PixelFormat format, int descriptor, int bits)
    {
        using var source = CreatePattern(5, 3);
        using var image = source.CloneAs(format, options: null);
        var data = Save(image, new DpxEncoder());
        Assert.Equal(descriptor, data[800]);
        Assert.Equal(bits, data[803]);
        Assert.Equal("SDPX", System.Text.Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal("V2.0", System.Text.Encoding.ASCII.GetString(data, 8, 4));
        Assert.Equal((uint)data.Length, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)));
        Assert.Equal(8192u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));
        Assert.Equal(8192u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(808)));
        Assert.Equal(uint.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(660)));

        // The decoder reads back the pixels as they were
        using var decoded = Image.Load(data);
        if (PixelFormats.IsFloatingPoint(format))
        {
            // A floating-point luma element loads as GrayFloat
            Assert.Equal(PixelFormats.IsGrayscale(format) ? PixelFormat.GrayFloat : PixelFormat.RgbaFloat, decoded.PixelFormat);
            using var expected = image.CloneAs<RgbaFloat>();
            using var actual = decoded.CloneAs<RgbaFloat>();
            Assert.Equal(GetBytes(expected), GetBytes(actual));
        }
        else
        {
            Assert.Equal(format, decoded.PixelFormat);
            Assert.Equal(GetBytes(image), GetBytes(decoded));
        }
    }

    [Fact]
    public void OpaqueImagesAreRgbAndTranslucentOnesRgba()
    {
        using var opaque = CreatePattern(3, 2);
        Assert.Equal(50, Save(opaque, new DpxEncoder())[800]);

        using var translucent = CreatePattern(3, 2);
        translucent.Frames[0][1, 1] = new Rgba32(1, 2, 3, 4);
        var data = Save(translucent, new DpxEncoder());
        Assert.Equal(51, data[800]);
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(new Rgba32(1, 2, 3, 4), decoded.Frames[0][1, 1]);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(12, true)]
    [InlineData(10, false)]
    [InlineData(12, false)]
    public void DeepComponentsAreFilledWithMethodA(int bits, bool bigEndian)
    {
        using var source = CreatePattern(5, 3);
        using var image = source.CloneAs<Rgba64>();
        var data = Save(image, new DpxEncoder { BitDepth = bits, AllowBitDepthReduction = true, ByteOrder = bigEndian ? DpxByteOrder.BigEndian : DpxByteOrder.LittleEndian });
        Assert.Equal(bigEndian ? "SDPX" : "XPDS", System.Text.Encoding.ASCII.GetString(data, 0, 4));
        Assert.Equal(bits, data[803]);
        Assert.Equal(1, bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(804)) : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(804)));

        // The code values are the nearest ones, round(v × (2^n - 1) / 65535), packed as the standard packs them
        var max = (1UL << bits) - 1;
        var expected = new List<byte>();
        for (var y = 0; y < image.Height; y++)
        {
            var components = new List<uint>();
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image.Frames[0][x, y];
                foreach (var value in new[] { pixel.R, pixel.G, pixel.B })
                {
                    components.Add((uint)(((2UL * value * max) + 65535) / (2 * 65535UL)));
                }
            }

            expected.AddRange(DpxFileBuilder.PackLine(components, bits, packing: 1, multiComponent: true, bigEndian));
        }

        Assert.Equal(expected.ToArray(), data.AsSpan(8192).ToArray());
    }

    [Fact]
    public void LumaTenBitWordsStartAtTheBottom()
    {
        using var image = new Image<Gray16>(4, 1);
        for (var x = 0; x < 4; x++)
        {
            image.Frames[0][x, 0] = new Gray16((ushort)(x * 21845));
        }

        var data = Save(image, new DpxEncoder { BitDepth = 10, AllowBitDepthReduction = true });
        Assert.Equal(6, data[800]);
        Assert.Equal(DpxFileBuilder.PackLine([0, 341, 682, 1023], 10, 1, multiComponent: false, bigEndian: true), data.AsSpan(8192).ToArray());
    }

    [Fact]
    public void FewerBitsThanTheSamplesRequireAnExplicitSetting()
    {
        using var source = CreatePattern(2, 2);
        using var image = source.CloneAs<Rgba64>();
        Assert.Equal("Bit depth reduction", Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DpxEncoder { BitDepth = 10 })).Feature);

        // 8-bit samples written with more bits lose nothing
        var data = Save(source, new DpxEncoder { BitDepth = 16 });
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(GetBytes(source), GetBytes(decoded));
    }

    [Fact]
    public void IntegerSamplesWrittenAsFloatsAreFractionsOfOne()
    {
        using var image = new Image<Gray8>(2, 1);
        image.Frames[0][0, 0] = new Gray8(51);
        image.Frames[0][1, 0] = new Gray8(255);
        var data = Save(image, new DpxEncoder { BitDepth = 32 });
        Assert.Equal(51 / 255f, BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(8192)));
        Assert.Equal(1f, BinaryPrimitives.ReadSingleBigEndian(data.AsSpan(8196)));
    }

    [Fact]
    public void FloatingPointPixelsWithIntegerComponentsFollowTheConversionSetting()
    {
        using var image = new Image<RgbaFloat>(1, 1);
        image.Frames[0][0, 0] = new RgbaFloat(0.5f, 2f, -1f, 1f);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DpxEncoder { BitDepth = 16 }));
        var data = Save(image, new DpxEncoder { BitDepth = 16, FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip } });
        Assert.Equal(16, data[803]);
        using var decoded = Image.Load<Rgba64>(data);
        Assert.Equal(new Rgba64(32768, 65535, 0, 65535), decoded.Frames[0][0, 0]);
    }

    [Theory]
    [InlineData(ColorTransferFunction.Srgb, null, 0, 6)]
    [InlineData(ColorTransferFunction.Linear, null, 2, 6)]
    [InlineData(ColorTransferFunction.Srgb, "1/1", 6, 6)]
    [InlineData(ColorTransferFunction.Srgb, "5/6", 7, 7)]
    [InlineData(ColorTransferFunction.Srgb, "6/6", 8, 8)]
    public void TheTransferAndTheColorimetryComeFromTheMetadata(ColorTransferFunction transfer, string? cicp, int transferCode, int colorimetricCode)
    {
        using var image = CreatePattern(2, 1);
        image.Metadata.TransferFunction = transfer;
        if (cicp is not null)
        {
            var parts = cicp.Split('/');
            image.Metadata.Cicp = new CicpColorDescription(byte.Parse(parts[0], CultureInfo.InvariantCulture), byte.Parse(parts[1], CultureInfo.InvariantCulture), 0, fullRange: true);
        }

        var data = Save(image, new DpxEncoder());
        Assert.Equal(transferCode, data[801]);
        Assert.Equal(colorimetricCode, data[802]);
        using var decoded = Image.Load(data);
        Assert.Equal(transfer, decoded.Metadata.TransferFunction);
        Assert.Equal(image.Metadata.Cicp, decoded.Metadata.Cicp);
    }

    [Fact]
    public void ADescriptionWithoutCodesIsMetadataTheFormatCannotStore()
    {
        using var image = CreatePattern(2, 1);
        image.Metadata.Cicp = new CicpColorDescription(9, 16, 0, fullRange: true);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DpxEncoder()));
        var data = Save(image, new DpxEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.Equal(0, data[801]);
    }

    [Fact]
    public void TheOrientationAndThePixelAspectRatioAreWritten()
    {
        using var image = CreatePattern(3, 2);
        image.Metadata.Orientation = ExifOrientation.LeftBottom;
        image.Metadata.PixelAspectRatio = new PixelAspectRatio(4, 3);
        var data = Save(image, new DpxEncoder());
        Assert.Equal(6, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(768)));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(1628)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(1632)));
        using var decoded = Image.Load(data);
        Assert.Equal(ExifOrientation.LeftBottom, decoded.Metadata.Orientation);
        Assert.Equal(new PixelAspectRatio(4, 3), decoded.Metadata.PixelAspectRatio);
        Assert.Equal("Meziantou.Framework.Imaging", decoded.Metadata.DpxHeader!.Creator);
    }

    [Fact]
    public void AnimationsAreRejected()
    {
        using var image = CreatePattern(2, 2);
        image.InsertFrame(1, image.Frames[0]);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DpxEncoder()));
    }

    [Fact]
    public void TheExtensionSelectsTheEncoder()
    {
        Assert.IsType<DpxEncoder>(ImageEncoder.FromFileExtension(".dpx"));
        Assert.IsType<DpxEncoder>(ImageEncoder.Create(ImageFormat.Dpx));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DpxEncoder { BitDepth = 14 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DpxEncoder { ByteOrder = (DpxByteOrder)2 });
        Assert.Null(new DpxEncoder().BitDepth);
        Assert.Equal(DpxByteOrder.BigEndian, new DpxEncoder().ByteOrder);
    }

    private static Image<Rgba32> CreatePattern(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.Frames[0][x, y] = new Rgba32((byte)(x * 50), (byte)(255 - (y * 70)), (byte)((x * 31) + (y * 17)), 255);
            }
        }

        return image;
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static byte[] GetBytes(Image image)
    {
        var frame = image.Frames[0];
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        return bytes;
    }
}
