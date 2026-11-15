using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Psd;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// PSD encoding: the structure of the files is read by <see cref="ReferencePsd"/>, an independent reader, and the pixels
/// are decoded back by the library.
/// </summary>
public sealed class PsdEncoderTests
{
    [Theory]
    [InlineData(PixelFormat.Gray8, 1, 8, PsdCompression.None)]
    [InlineData(PixelFormat.Gray8, 1, 8, PsdCompression.RunLength)]
    [InlineData(PixelFormat.Gray16, 1, 16, PsdCompression.RunLength)]
    [InlineData(PixelFormat.Rgb24, 3, 8, PsdCompression.None)]
    [InlineData(PixelFormat.Rgb24, 3, 8, PsdCompression.RunLength)]
    [InlineData(PixelFormat.Rgb48, 3, 16, PsdCompression.None)]
    [InlineData(PixelFormat.Rgb48, 3, 16, PsdCompression.RunLength)]
    [InlineData(PixelFormat.Rgba32, 3, 8, PsdCompression.RunLength)]
    [InlineData(PixelFormat.Bgra32, 3, 8, PsdCompression.RunLength)]
    [InlineData(PixelFormat.Rgba64, 3, 16, PsdCompression.RunLength)]
    public void OpaqueImagesAreFlatDocuments(PixelFormat format, int mode, int depth, PsdCompression compression)
    {
        using var source = CreatePattern(37, 5);
        using var image = source.CloneAs(format, options: null);
        var data = Save(image, new PsdEncoder { Compression = compression });
        var picture = ReferencePsd.Read(data);
        Assert.Equal(1, picture.Version);
        Assert.Equal(mode, picture.Mode);
        Assert.Equal(depth, picture.Depth);
        Assert.Equal(mode, picture.ChannelCount);
        Assert.Equal((int)compression, picture.Compression);
        Assert.Null(picture.LayerCount);
        Assert.False(picture.HasNoRealComposite);
        Assert.True(picture.Resources.ContainsKey(1057));

        using var decoded = Image.Load(data);
        var expectedFormat = format switch
        {
            PixelFormat.Rgba32 or PixelFormat.Bgra32 => PixelFormat.Rgb24,
            PixelFormat.Rgba64 => PixelFormat.Rgb48,
            _ => format,
        };

        Assert.Equal(expectedFormat, decoded.PixelFormat);
        using var expected = image.CloneAs<Rgba64>();
        using var actual = decoded.CloneAs<Rgba64>();
        Assert.Equal(GetBytes(expected), GetBytes(actual));
        Assert.Equal(ImageFormat.Psd, Image.DetectFormat(data));
    }

    [Theory]
    [InlineData(PixelFormat.Cmyk32)]
    [InlineData(PixelFormat.Cmyk64)]
    public void CmykPixelsAreStoredInverted(PixelFormat format)
    {
        var is16Bit = format == PixelFormat.Cmyk64;
        using var image = is16Bit ? (Image)new Image<Cmyk64>(3, 2) : new Image<Cmyk32>(3, 2);
        for (var i = 0; i < 6; i++)
        {
            var v = (i * 37) + 5;
            if (image is Image<Cmyk64> deep)
            {
                deep.Frames[0][i % 3, i / 3] = new Cmyk64((ushort)(v * 251), (ushort)(v * 13), (ushort)(65535 - v), (ushort)(v * 100));
            }
            else
            {
                ((Image<Cmyk32>)image).Frames[0][i % 3, i / 3] = new Cmyk32((byte)v, (byte)(v * 3), (byte)(255 - v), (byte)(v / 2));
            }
        }

        var bytes = GetBytes(image);
        image.Metadata.IccProfile = IccProfile.Cgats001CompatibleCmyk;
        var data = Save(image, new PsdEncoder());
        var picture = ReferencePsd.Read(data);
        Assert.Equal(4, picture.Mode);
        Assert.Equal(4, picture.ChannelCount);
        Assert.Equal(is16Bit ? 16 : 8, picture.Depth);

        // The cyan of the first pixel: no ink is the largest value
        var cyan = is16Bit ? BinaryPrimitives.ReadUInt16BigEndian(picture.Channels[0]) : picture.Channels[0][0];
        var stored = is16Bit ? BitConverter.ToUInt16(bytes) : bytes[0];
        Assert.Equal((is16Bit ? 65535 : 255) - stored, cyan);

        using var decoded = is16Bit ? (Image)Image.Load<Cmyk64>(data) : Image.Load<Cmyk32>(data);
        Assert.Equal(bytes, GetBytes(decoded));
        Assert.Equal(IccProfile.Cgats001CompatibleCmyk.Data, decoded.Metadata.IccProfile!.Data);
    }

    [Theory]
    [InlineData(PsdCompression.None)]
    [InlineData(PsdCompression.RunLength)]
    public void TransparencyIsTheMergedAlphaOfOneLayer(PsdCompression compression)
    {
        using var image = CreatePattern(5, 3);
        image.Frames[0][1, 1] = new Rgba32(10, 20, 30, 0);
        image.Frames[0][2, 1] = new Rgba32(200, 100, 50, 128);
        image.Frames[0][3, 2] = new Rgba32(1, 2, 3, 1);
        var data = Save(image, new PsdEncoder { Compression = compression });
        var picture = ReferencePsd.Read(data);
        Assert.Equal(3, picture.Mode);
        Assert.Equal(4, picture.ChannelCount);
        Assert.Equal(-1, picture.LayerCount);
        Assert.True(picture.HasMergedAlpha);

        // The colors of the composite are blended over white: c × α + 255 × (1 − α)
        var red = picture.Channels[0];
        Assert.Equal(255, red[(1 * 5) + 1]);
        Assert.Equal((byte)Math.Round(((200 * 128) + (255 * 127)) / 255.0, MidpointRounding.AwayFromZero), red[(1 * 5) + 2]);
        Assert.Equal(image.Frames[0][0, 0].R, red[0]);

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(PixelFormat.Rgba32, Image.Identify(data).PixelFormat);
        Assert.Equal(picture.GetRgba().Select(v => (byte)v).ToArray(), GetBytes(decoded));
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                var expected = image.Frames[0][x, y];
                var actual = decoded.Frames[0][x, y];
                Assert.Equal(expected.A, actual.A);
                if (expected.A == 255)
                {
                    Assert.Equal(expected, actual);
                }
                else if (expected.A >= 128)
                {
                    // The matte rounds the colors once: un-matting gives them back within 255 / (2α)
                    Assert.True(Math.Abs(expected.R - actual.R) <= 1 && Math.Abs(expected.G - actual.G) <= 1 && Math.Abs(expected.B - actual.B) <= 1, $"{expected} {actual}");
                }
            }
        }
    }

    [Fact]
    public void TheLayerHoldsTheColorsAsTheyAre()
    {
        using var image = CreatePattern(4, 2);
        image.Frames[0][1, 0] = new Rgba32(200, 100, 50, 77);
        var data = Save(image, new PsdEncoder { Compression = PsdCompression.None });

        // The layer record follows the header, the resources and the lengths of the layer section and layer info
        var resources = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(30));
        var layerInfo = 30 + 4 + resources + 4 + 4;
        Assert.Equal(-1, BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(layerInfo)));
        var record = layerInfo + 2;
        Assert.Equal(2, BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(record + 8)));
        Assert.Equal(4, BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(record + 12)));
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 16)));
        var channels = record + 18;
        short[] ids = [-1, 0, 1, 2];
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(ids[i], BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(channels + (i * 6))));
            Assert.Equal(2u + 8, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(channels + (i * 6) + 2)));
        }

        var blend = channels + 24;
        Assert.Equal("8BIMnorm", Encoding.ASCII.GetString(data, blend, 8));
        Assert.Equal(255, data[blend + 8]);
        var extra = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(blend + 12));
        var name = blend + 16 + 8;
        Assert.Equal("Layer 0", Encoding.ASCII.GetString(data, name + 1, data[name]));

        // The channel data: the transparency, then red as it is
        var channelData = blend + 16 + extra;
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(channelData)));
        Assert.Equal(77, data[channelData + 2 + 1]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(channelData + 10)));
        Assert.Equal(200, data[channelData + 10 + 2 + 1]);
    }

    [Theory]
    [InlineData(PixelFormat.La16, 1, 8)]
    [InlineData(PixelFormat.La32, 1, 16)]
    [InlineData(PixelFormat.Rgba64, 3, 16)]
    [InlineData(PixelFormat.RgbaFloat, 3, 32)]
    public void DeepTransparentDocumentsHaveTheirLayerInATaggedBlock(PixelFormat format, int mode, int depth)
    {
        using var pattern = CreatePattern(6, 4);
        pattern.Frames[0][2, 2] = new Rgba32(90, 90, 90, 100);
        pattern.Frames[0][3, 1] = new Rgba32(0, 0, 0, 0);
        using var image = mode == 1 ? pattern.CloneAs<La16>().CloneAs(format, options: null) : pattern.CloneAs(format, options: null);
        if (depth == 32)
        {
            image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        var data = Save(image, new PsdEncoder());
        var picture = ReferencePsd.Read(data);
        Assert.Equal(mode, picture.Mode);
        Assert.Equal(depth, picture.Depth);
        Assert.Equal(mode + 1, picture.ChannelCount);
        Assert.Equal(-1, picture.LayerCount);
        if (depth != 8)
        {
            Assert.NotEqual(-1, data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(depth == 16 ? "8BIMLr16" : "8BIMLr32")));
        }

        using var decoded = Image.Load(data);
        using var expected = image.CloneAs<RgbaFloat>();
        using var actual = decoded.CloneAs<RgbaFloat>();
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var e = expected.Frames[0][x, y];
                var a = actual.Frames[0][x, y];
                Assert.Equal(e.A, a.A);
                if (e.A > 0)
                {
                    var tolerance = depth == 32 ? 1e-6f : 1f / (depth == 8 ? 255 : 65535) / e.A;
                    Assert.True(Math.Abs(e.R - a.R) <= tolerance && Math.Abs(e.G - a.G) <= tolerance && Math.Abs(e.B - a.B) <= tolerance, $"({x}, {y}): {e} {a}");
                }
            }
        }
    }

    [Fact]
    public void FloatingPointSamplesAreLinearAndExact()
    {
        using var image = new Image<RgbaFloat>(3, 1);
        image.Frames[0][0, 0] = new RgbaFloat(0.25f, 2.5f, -0.125f, 1);
        image.Frames[0][1, 0] = new RgbaFloat(1e-7f, 1, 0.333f, 1);
        image.Frames[0][2, 0] = new RgbaFloat(0, 0.5f, 1, 1);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var data = Save(image, new PsdEncoder());
        var picture = ReferencePsd.Read(data);
        Assert.Equal(32, picture.Depth);
        Assert.Equal(3, picture.ChannelCount);
        using var decoded = Image.Load<RgbaFloat>(data);
        Assert.Equal(ColorTransferFunction.Linear, decoded.Metadata.TransferFunction);
        Assert.Equal(GetBytes(image), GetBytes(decoded));

        // Samples labeled sRGB would be read as linear light without an ICC profile
        image.Metadata.TransferFunction = ColorTransferFunction.Srgb;
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new PsdEncoder()));
        image.Metadata.IccProfile = IccProfile.Srgb;
        Assert.Equal(IccProfile.Srgb.Data, Image.Identify(Save(image, new PsdEncoder())).Metadata.IccProfile!.Data);
    }

    [Fact]
    public void LargeDocumentsArePsbFiles()
    {
        using var source = CreatePattern(9, 4);
        source.Frames[0][4, 2] = new Rgba32(4, 5, 6, 7);
        foreach (var compression in new[] { PsdCompression.None, PsdCompression.RunLength })
        {
            var data = Save(source, new PsdEncoder { LargeDocument = true, Compression = compression });
            var picture = ReferencePsd.Read(data);
            Assert.Equal(2, picture.Version);
            Assert.Equal(-1, picture.LayerCount);
            using var decoded = Image.Load<Rgba32>(data);
            Assert.Equal(picture.GetRgba().Select(v => (byte)v).ToArray(), GetBytes(decoded));
            Assert.Equal(source.Frames[0][0, 0], decoded.Frames[0][0, 0]);
        }

        var encoder = Assert.IsType<PsdEncoder>(ImageEncoder.FromFileExtension(".psb"));
        Assert.True(encoder.LargeDocument);
        Assert.False(Assert.IsType<PsdEncoder>(ImageEncoder.FromFileExtension("psd")).LargeDocument);
        Assert.IsType<PsdEncoder>(ImageEncoder.Create(ImageFormat.Psd));
    }

    [Fact]
    public void ImagesLongerThanThirtyThousandPixelsArePsbFiles()
    {
        using var image = new Image<Gray8>(30_001, 1);
        var data = Save(image, new PsdEncoder());
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4)));
        using var decoded = Image.Load(data);
        Assert.Equal(new Size(30_001, 1), decoded.Size);

        using var exact = new Image<Gray8>(30_000, 1);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(Save(exact, new PsdEncoder()).AsSpan(4)));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void LongPackBitsRowsRequireAPsbFile()
    {
        // Rows of 17,000 floating-point pixels of noise: about 68 KB per channel
        using var image = new Image<RgbaFloat>(17_000, 1);
        var random = new Random(42);
        for (var x = 0; x < image.Width; x++)
        {
            image.Frames[0][x, 0] = new RgbaFloat(random.NextSingle(), random.NextSingle(), random.NextSingle(), 1);
        }

        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        Assert.Equal("PackBits rows longer than 65,535 bytes", Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new PsdEncoder())).Feature);
        foreach (var encoder in new[] { new PsdEncoder { Compression = PsdCompression.None }, new PsdEncoder { LargeDocument = true } })
        {
            using var decoded = Image.Load<RgbaFloat>(Save(image, encoder));
            Assert.Equal(GetBytes(image), GetBytes(decoded));
        }
    }

    [Fact]
    public void MetadataIsWrittenAsImageResources()
    {
        using var image = CreatePattern(3, 2);
        var xmp = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>");
        byte[] exif = [(byte)'M', (byte)'M', 0, 42, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0];
        image.Metadata.IccProfile = IccProfile.Srgb;
        image.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(exif));
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        image.Metadata.Resolution = new ImageResolution(300, 150.5);
        var data = Save(image, new PsdEncoder());
        var picture = ReferencePsd.Read(data);
        Assert.Equal([1005, 1039, 1057, 1058, 1060], picture.Resources.Keys.Order());
        using var decoded = Image.Load(data);
        Assert.Equal(new ImageResolution(300, 150.5), decoded.Metadata.Resolution);
        Assert.Equal(IccProfile.Srgb.Data, decoded.Metadata.IccProfile!.Data);
        Assert.Equal(xmp, decoded.Metadata.XmpProfile!.Data.ToArray());
        Assert.NotNull(decoded.Metadata.ExifProfile);

        // The orientation is written in the EXIF profile
        using var oriented = CreatePattern(3, 2);
        oriented.Metadata.Orientation = ExifOrientation.RightTop;
        Assert.Equal(ExifOrientation.RightTop, Image.Identify(Save(oriented, new PsdEncoder())).Metadata.Orientation);

        // Text entries cannot be stored
        using var text = CreatePattern(3, 2);
        text.Metadata.TextEntries.Add(new ImageTextEntry("Comment", "Hello"));
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(text, new PsdEncoder()));
        Assert.Empty(Image.Identify(Save(text, new PsdEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported })).Metadata.TextEntries);
    }

    [Fact]
    public void TheSettingsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PsdEncoder { Compression = (PsdCompression)2 });
        Assert.Equal(PsdCompression.RunLength, new PsdEncoder().Compression);
        Assert.False(new PsdEncoder().LargeDocument);
        Assert.Equal(ImageFormat.Psd, new PsdEncoder().Format);

        using var image = CreatePattern(2, 2);
        image.InsertFrame(1, image.Frames[0]);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new PsdEncoder()));
    }

    [Fact]
    public void SaveWithTransparency()
    {
        using var image = new Image<Rgba32>(64, 64);
        image.Frames[0][10, 10] = new Rgba32(255, 0, 0, 128);

        // A transparent image gets a layer holding the image; ".psb" writes the large document format
        using var stream = new MemoryStream();
        image.Save(stream, new PsdEncoder { Compression = PsdCompression.RunLength });

        stream.Position = 0;
        using var decoded = Image.Load<Rgba32>(stream);
        Assert.Equal(128, decoded.Frames[0][10, 10].A);
    }

    private static Image<Rgba32> CreatePattern(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.Frames[0][x, y] = new Rgba32((byte)(x * 50), (byte)(255 - (y * 70)), (byte)(((x / 4) * 31) + (y * 17)), 255);
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
