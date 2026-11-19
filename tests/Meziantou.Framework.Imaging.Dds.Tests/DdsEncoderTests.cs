using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// DDS encoding: the headers are checked field by field against the DDS reference of Microsoft Learn, and the files are
/// read back by the decoder of the library (whose own tests compare it with DirectXTex and OpenImageIO). The interop tests
/// decode the output with OpenImageIO.
/// </summary>
public sealed class DdsEncoderTests
{
    [Fact]
    public void DefaultOutputIsLosslessRgba8WithALegacyHeader()
    {
        using var image = CreateRgba32(5, 3, seed: 1);
        var data = Save(image, new DdsEncoder());
        Assert.Equal("DDS "u8.ToArray(), data[..4]);
        Assert.Equal(124u, ReadUInt32(data, 4));
        Assert.Equal(0x1u | 0x2 | 0x4 | 0x8 | 0x1000, ReadUInt32(data, 8));
        Assert.Equal(3u, ReadUInt32(data, 12));
        Assert.Equal(5u, ReadUInt32(data, 16));
        Assert.Equal(20u, ReadUInt32(data, 20));
        Assert.Equal(1u, ReadUInt32(data, 28));

        // DDS_PIXELFORMAT: DDPF_RGB | DDPF_ALPHAPIXELS, 32 bits, the masks of R8G8B8A8
        Assert.Equal(0x41u, ReadUInt32(data, 80));
        Assert.Equal(32u, ReadUInt32(data, 88));
        Assert.Equal(new uint[] { 0xFF, 0xFF00, 0xFF0000, 0xFF000000 }, [ReadUInt32(data, 92), ReadUInt32(data, 96), ReadUInt32(data, 100), ReadUInt32(data, 104)]);
        Assert.Equal(0x1000u, ReadUInt32(data, 108));
        Assert.HasCount(128 + (5 * 3 * 4), data);

        using var decoded = Image.Load<Rgba32>(data);
        AssertEqualPixels(image, decoded);
    }

    [Theory]
    [InlineData(DdsTextureFormat.R8G8B8A8Unorm, DdsHeaderVersion.Dx10, 28u)]
    [InlineData(DdsTextureFormat.R8G8B8A8UnormSrgb, DdsHeaderVersion.Automatic, 29u)]
    [InlineData(DdsTextureFormat.B8G8R8A8Unorm, DdsHeaderVersion.Automatic, 0u)]
    [InlineData(DdsTextureFormat.B8G8R8A8UnormSrgb, DdsHeaderVersion.Automatic, 91u)]
    [InlineData(DdsTextureFormat.B8G8R8A8Unorm, DdsHeaderVersion.Dx10, 87u)]
    public void EightBitFormatsAreLossless(DdsTextureFormat format, DdsHeaderVersion version, uint dxgiFormat)
    {
        using var image = CreateRgba32(6, 7, seed: 2);
        var data = Save(image, new DdsEncoder { TextureFormat = format, HeaderVersion = version });
        Assert.Equal(dxgiFormat != 0, ReadUInt32(data, 84) == 0x30315844);
        if (dxgiFormat != 0)
        {
            Assert.Equal(dxgiFormat, ReadUInt32(data, 128));
            Assert.Equal(3u, ReadUInt32(data, 132));
            Assert.Equal(1u, ReadUInt32(data, 140));
        }

        using var decoded = Image.Load<Rgba32>(data);
        AssertEqualPixels(image, decoded);
        Assert.Equal(format is DdsTextureFormat.R8G8B8A8UnormSrgb or DdsTextureFormat.B8G8R8A8UnormSrgb, decoded.Metadata.Cicp is not null);
    }

    [Fact]
    public void SixteenBitAndFloatingPointPixelsKeepTheirPrecision()
    {
        using var wide = new Image<Rgba64>(3, 2);
        wide.Frames[0][0, 0] = new Rgba64(1, 2, 65535, 40000);
        wide.Frames[0][2, 1] = new Rgba64(12345, 0, 7, 65535);
        var data = Save(wide, new DdsEncoder());
        Assert.Equal(36u, ReadUInt32(data, 84));
        using (var decoded = Image.Load<Rgba64>(data))
        {
            AssertEqualPixels(wide, decoded);
        }

        using var floats = new Image<RgbaFloat>(2, 2) { Metadata = { TransferFunction = ColorTransferFunction.Linear } };
        floats.Frames[0][0, 0] = new RgbaFloat(-3.5f, 1e20f, 0.1f, 1f);
        floats.Frames[0][1, 1] = new RgbaFloat(float.PositiveInfinity, 0, 0.5f, 0.25f);
        data = Save(floats, new DdsEncoder());
        Assert.Equal(116u, ReadUInt32(data, 84));
        using (var decoded = Image.Load<RgbaFloat>(data))
        {
            AssertEqualPixels(floats, decoded);
        }

        using var halves = new Image<RgbaHalf>(2, 1) { Metadata = { TransferFunction = ColorTransferFunction.Linear } };
        halves.Frames[0][1, 0] = new RgbaHalf((Half)1.5f, (Half)(-2f), (Half)65504f, (Half)1f);
        data = Save(halves, new DdsEncoder());
        Assert.Equal(113u, ReadUInt32(data, 84));
        using (var decoded = Image.Load<RgbaHalf>(data))
        {
            AssertEqualPixels(halves, decoded);
        }
    }

    [Fact]
    public void HalvesKeepTheBitsOfNotANumber()
    {
        // Signaling and quiet not-a-numbers of both signs, whose payloads a conversion through floats would quiet
        ushort[] samples = [0x7D0A, 0xFD76, 0x7E01, 0xFC01, 0x7C00, 0x8000, 0x0001, 0x7BFF];
        byte[] bytes = [.. samples.SelectMany(BitConverter.GetBytes)];
        using var halves = Image.ImportPixelBytes<RgbaHalf>(bytes, 2, 1);
        halves.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var data = Save(halves, new DdsEncoder());
        Assert.Equal(113u, ReadUInt32(data, 84));
        using var decoded = Image.Load<RgbaHalf>(data);
        var actual = new byte[bytes.Length];
        decoded.Frames[0].CopyPixelBytesTo(actual);
        Assert.Equal(bytes, actual);
    }

    [Fact]
    public void GrayPixelsAreOneChannel()
    {
        using var gray = new Image<Gray8>(4, 1);
        for (var x = 0; x < 4; x++)
        {
            gray.Frames[0][x, 0] = new Gray8((byte)(x * 60));
        }

        var data = Save(gray, new DdsEncoder());
        Assert.Equal(0x20000u, ReadUInt32(data, 80));
        Assert.Equal(8u, ReadUInt32(data, 88));
        Assert.Equal(new byte[] { 0, 60, 120, 180 }, data[128..]);
        using var decoded = Image.Load<Gray8>(data);
        AssertEqualPixels(gray, decoded);

        using var wide = new Image<Gray16>(2, 1);
        wide.Frames[0][1, 0] = new Gray16(54321);
        data = Save(wide, new DdsEncoder());
        Assert.Equal(16u, ReadUInt32(data, 88));
        using var decodedWide = Image.Load<Gray16>(data);
        AssertEqualPixels(wide, decodedWide);
    }

    [Fact]
    public void ChannelsTheFormatLacksNeedASetting()
    {
        using var image = CreateRgba32(4, 4, seed: 3);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DdsEncoder { TextureFormat = DdsTextureFormat.R8Unorm }));
        Assert.Equal("Channel reduction", exception.Feature);
        Assert.Contains("AllowChannelReduction", exception.Message, StringComparison.Ordinal);

        // With the setting, red is kept and read back as gray
        var data = Save(image, new DdsEncoder { TextureFormat = DdsTextureFormat.R8Unorm, AllowChannelReduction = true });
        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(image.Frames[0][1, 2].R, decoded.Frames[0][1, 2].G);

        // Two channels: blue must be 0 and alpha opaque
        using var redGreen = new Image<Rgba32>(1, 1);
        redGreen.Frames[0][0, 0] = new Rgba32(10, 20, 0, 255);
        using (var decodedRedGreen = Image.Load<Rgba32>(Save(redGreen, new DdsEncoder { TextureFormat = DdsTextureFormat.R8G8Unorm })))
        {
            Assert.Equal(new Rgba32(10, 20, 0, 255), decodedRedGreen.Frames[0][0, 0]);
        }

        redGreen.Frames[0][0, 0] = new Rgba32(10, 20, 1, 255);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(redGreen, new DdsEncoder { TextureFormat = DdsTextureFormat.R8G8Unorm }));
    }

    [Fact]
    public void LostPrecisionNeedsASetting()
    {
        using var image = new Image<Rgba64>(2, 2);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DdsEncoder { TextureFormat = DdsTextureFormat.R8G8B8A8Unorm }));
        Assert.Equal("Bit depth reduction", exception.Feature);
        image.Frames[0][0, 0] = new Rgba64(0x1234, 0xFFFF, 0x8080, 0xFFFF);
        using var decoded = Image.Load<Rgba32>(Save(image, new DdsEncoder { TextureFormat = DdsTextureFormat.R8G8B8A8Unorm, AllowBitDepthReduction = true }));
        Assert.Equal(new Rgba32(0x12, 0xFF, 0x80, 0xFF), decoded.Frames[0][0, 0]);
    }

    [Fact]
    public void FloatingPointFormatsStoreLinearLight()
    {
        // An integer image is labeled sRGB: floating-point samples would be read as linear light
        using var image = CreateRgba32(2, 2, seed: 4);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DdsEncoder { TextureFormat = DdsTextureFormat.R16G16B16A16Float }));
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using var decoded = Image.Load<RgbaFloat>(Save(image, new DdsEncoder { TextureFormat = DdsTextureFormat.R32G32B32A32Float }));
        Assert.Equal(image.Frames[0][1, 1].R / 255f, decoded.Frames[0][1, 1].R);

        // Halves stop at 65,504
        using var large = new Image<RgbaFloat>(1, 1) { Metadata = { TransferFunction = ColorTransferFunction.Linear } };
        large.Frames[0][0, 0] = new RgbaFloat(70000f, 0, 0, 1);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Save(large, new DdsEncoder { TextureFormat = DdsTextureFormat.R16G16B16A16Float, AllowBitDepthReduction = true }));
        Assert.Contains("65,504", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLegacyHeaderCannotDescribeEveryFormat()
    {
        using var image = CreateRgba32(4, 4, seed: 5);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DdsEncoder { TextureFormat = DdsTextureFormat.R8G8B8A8UnormSrgb, HeaderVersion = DdsHeaderVersion.Legacy }));
        Assert.Contains("DX10", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedMipmapsAverageInLinearLight()
    {
        using var image = new Image<Rgba32>(4, 2);
        for (var x = 0; x < 4; x++)
        {
            image.Frames[0][x, 0] = x % 2 == 0 ? new Rgba32(255, 255, 255, 255) : new Rgba32(0, 0, 0, 255);
            image.Frames[0][x, 1] = new Rgba32(0, 0, 0, 255);
        }

        var data = Save(image, new DdsEncoder { GenerateMipmaps = true });
        Assert.Equal(3u, ReadUInt32(data, 28));
        Assert.Equal(0x401008u, ReadUInt32(data, 108));
        Assert.HasCount(128 + ((8 + 2 + 1) * 4), data);
        using var collection = ImageCollection.Load(data);
        Assert.Equal([new Size(4, 2), new Size(2, 1), new Size(1, 1)], collection.Entries.Select(entry => entry.Size));
        Assert.Equal([0, 1, 2], collection.Entries.Select(entry => entry.TextureSurface!.MipLevel));

        // A quarter of white is 0.25 in linear light: sRGB 137, not 64 (the average of the codes)
        using var level1 = collection[1].Decode<Rgba32>();
        Assert.Equal(new Rgba32(137, 137, 137, 255), level1.Frames[0][0, 0]);
        using var level2 = collection[2].Decode<Rgba32>();
        Assert.Equal(new Rgba32(137, 137, 137, 255), level2.Frames[0][0, 0]);
    }

    [Fact]
    public void MipmapsOfOddSizesCoverEveryTexel()
    {
        // A level of 3 texels becomes 1 texel: the three are averaged with equal weights
        using var image = new Image<RgbaFloat>(3, 1) { Metadata = { TransferFunction = ColorTransferFunction.Linear } };
        image.Frames[0][0, 0] = new RgbaFloat(3, 0, 0, 1);
        image.Frames[0][1, 0] = new RgbaFloat(6, 0, 0, 1);
        image.Frames[0][2, 0] = new RgbaFloat(0, 0, 0, 1);
        using var collection = ImageCollection.Load(Save(image, new DdsEncoder { GenerateMipmaps = true }));
        using var level = collection[1].Decode<RgbaFloat>();
        Assert.Equal(new RgbaFloat(3, 0, 0, 1), level.Frames[0][0, 0]);
    }

    [Fact]
    public void TransparentTexelsDoNotBleedIntoMipmaps()
    {
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][0, 0] = new Rgba32(255, 0, 0, 255);
        image.Frames[0][1, 0] = new Rgba32(0, 255, 0, 0);
        using var collection = ImageCollection.Load(Save(image, new DdsEncoder { GenerateMipmaps = true }));
        using var level = collection[1].Decode<Rgba32>();
        Assert.Equal(new Rgba32(255, 0, 0, 128), level.Frames[0][0, 0]);
    }

    [Fact]
    public void CubeMapsAndArraysAreSavedFromACollection()
    {
        using var collection = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        var images = new List<Image>();
        try
        {
            for (var layer = 1; layer >= 0; layer--)
            {
                foreach (var face in Enum.GetValues<CubeMapFace>())
                {
                    var image = CreateRgba32(8, 8, seed: (layer * 10) + (int)face);
                    images.Add(image);
                    collection.Add(image, new TextureSurface(layer, face));
                }
            }

            var data = SaveCollection(collection, new DdsEncoder { GenerateMipmaps = true });

            // An array of cube maps needs the DX10 header: TEXTURE2D, DDS_RESOURCE_MISC_TEXTURECUBE, two cube maps
            Assert.Equal(0x30315844u, ReadUInt32(data, 84));
            Assert.Equal(0xFE00u, ReadUInt32(data, 112));
            Assert.Equal(new uint[] { 28, 3, 4, 2 }, [ReadUInt32(data, 128), ReadUInt32(data, 132), ReadUInt32(data, 136), ReadUInt32(data, 140)]);

            using var loaded = ImageCollection.Load(data);
            Assert.Equal(2 * 6 * 4, loaded.Count);
            foreach (var entry in loaded.Entries.Where(entry => entry.TextureSurface!.MipLevel == 0))
            {
                var surface = entry.TextureSurface!;
                var source = images[((1 - surface.ArrayIndex) * 6) + (int)surface.Face!.Value];
                using var decoded = entry.Decode<Rgba32>();
                AssertEqualPixels((Image<Rgba32>)source, decoded);
            }
        }
        finally
        {
            foreach (var image in images)
            {
                image.Dispose();
            }
        }
    }

    [Fact]
    public void ALoadedTextureIsSavedAgain()
    {
        using var image = CreateRgba32(4, 4, seed: 6);
        var data = Save(image, new DdsEncoder { GenerateMipmaps = true });
        using var collection = ImageCollection.Load(data);
        Assert.Equal(data, SaveCollection(collection, new DdsEncoder()));
    }

    [Fact]
    public void SurfacesOfAnotherKindOfPixelsAreRejectedByTheDefaultFormat()
    {
        // The first surface, linear floats, chooses a floating-point format: an sRGB surface would be stored as linear light
        using var floats = new Image<RgbaFloat>(4, 4, new RgbaFloat(0.25f, 0.5f, 0.75f, 1f));
        using var srgb = CreateRgba32(4, 4, seed: 4);
        using var collection = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        collection.Add(floats, new TextureSurface(0));
        collection.Add(srgb, new TextureSurface(1));
        var exception = Assert.Throws<ArgumentException>(() => SaveCollection(collection, new DdsEncoder()));
        Assert.Contains("another texture format", exception.Message, StringComparison.Ordinal);

        // An explicit texture format is the same for every surface: a linear-light surface of integers is stored as floats
        using var linear = CreateRgba32(4, 4, seed: 5);
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using var explicitFormat = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        explicitFormat.Add(floats, new TextureSurface(0));
        explicitFormat.Add(linear, new TextureSurface(1));
        using var loaded = ImageCollection.Load(SaveCollection(explicitFormat, new DdsEncoder { TextureFormat = DdsTextureFormat.R32G32B32A32Float }));
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void TheStructureOfACollectionIsChecked()
    {
        using var image = CreateRgba32(4, 4, seed: 7);
        using var collection = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        Assert.Throws<InvalidOperationException>(() => collection.Add(image));
        collection.Add(image, new TextureSurface(face: CubeMapFace.PositiveX));
        var exception = Assert.Throws<ArgumentException>(() => SaveCollection(collection, new DdsEncoder()));
        Assert.Contains("NegativeX", exception.Message, StringComparison.Ordinal);

        using var flat = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        flat.Add(image, new TextureSurface());
        flat.Add(image, new TextureSurface(mipLevel: 1));
        exception = Assert.Throws<ArgumentException>(() => SaveCollection(flat, new DdsEncoder()));
        Assert.Contains("2x2", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => SaveCollection(flat, new DdsEncoder { GenerateMipmaps = true }));

        using var duplicate = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        duplicate.Add(image, new TextureSurface());
        duplicate.Add(image, new TextureSurface());
        Assert.Throws<ArgumentException>(() => SaveCollection(duplicate, new DdsEncoder()));

        Assert.Throws<ArgumentOutOfRangeException>(() => new TextureSurface(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextureSurface(face: (CubeMapFace)6));
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        Assert.Throws<InvalidOperationException>(() => pages.Add(image, new TextureSurface()));
        pages.Add(image);
        Assert.Throws<ArgumentException>(() => SaveCollection(pages, new DdsEncoder()));
    }

    [Theory]
    [InlineData(DdsTextureFormat.Bc1Unorm, TextureCompressionQuality.Fast, 30)]
    [InlineData(DdsTextureFormat.Bc1UnormSrgb, TextureCompressionQuality.Thorough, 30)]
    [InlineData(DdsTextureFormat.Bc2Unorm, TextureCompressionQuality.Fast, 30)]
    [InlineData(DdsTextureFormat.Bc3Unorm, TextureCompressionQuality.Thorough, 31)]
    [InlineData(DdsTextureFormat.Bc7Unorm, TextureCompressionQuality.Fast, 37)]
    [InlineData(DdsTextureFormat.Bc7UnormSrgb, TextureCompressionQuality.Thorough, 37)]
    public void BlockCompressedFormatsRoundTripWithinTheirBounds(DdsTextureFormat format, TextureCompressionQuality quality, double minimumPsnr)
    {
        // A smooth image whose sizes are not multiples of 4: the edge blocks repeat the last texels
        using var image = new Image<Rgba32>(38, 21);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                // BC1 stores 1-bit alpha: its image is opaque
                var alpha = format is DdsTextureFormat.Bc1Unorm or DdsTextureFormat.Bc1UnormSrgb ? 255 : 255 - (x * 3);
                image.Frames[0][x, y] = new Rgba32((byte)(x * 6), (byte)(y * 12), (byte)(128 + (64 * Math.Sin(x / 5.0))), (byte)alpha);
            }
        }

        var encoder = new DdsEncoder { TextureFormat = format, Quality = quality };
        var data = Save(image, encoder);
        Assert.Equal(data, Save(image, encoder));
        using var decoded = Image.Load<Rgba32>(data);
        Assert.InRange(GetPsnr(image, decoded), minimumPsnr, double.PositiveInfinity);
    }

    [Theory]
    [InlineData(DdsTextureFormat.Bc4Unorm)]
    [InlineData(DdsTextureFormat.Bc5Unorm)]
    [InlineData(DdsTextureFormat.Bc4Snorm)]
    [InlineData(DdsTextureFormat.Bc5Snorm)]
    [InlineData(DdsTextureFormat.Bc6HUf16)]
    [InlineData(DdsTextureFormat.Bc6HSf16)]
    public void ChannelAndHdrFormatsRoundTripWithinTheirBounds(DdsTextureFormat format)
    {
        var signed = format is DdsTextureFormat.Bc4Snorm or DdsTextureFormat.Bc5Snorm or DdsTextureFormat.Bc6HSf16;
        var gray = format is DdsTextureFormat.Bc4Unorm or DdsTextureFormat.Bc4Snorm;
        var redGreen = format is DdsTextureFormat.Bc5Unorm or DdsTextureFormat.Bc5Snorm;
        var hdr = format is DdsTextureFormat.Bc6HUf16 or DdsTextureFormat.Bc6HSf16;

        // The unsigned integer formats take the floats as 16-bit samples, labeled sRGB; the others store linear light
        var floatingPoint = signed || hdr;
        using var image = new Image<RgbaFloat>(16, 12) { Metadata = { TransferFunction = floatingPoint ? ColorTransferFunction.Linear : ColorTransferFunction.Srgb } };
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                // BC6H colors lie on a line in each block, as the one-subset modes store them
                var r = x / 15f;
                var g = gray ? r : hdr ? r / 2 : y / 11f;
                var b = gray ? r : redGreen ? 0 : r / 4;
                if (signed && hdr)
                {
                    // Negative colors: BC6H interpolates the half bit patterns, coarse across 0
                    (r, g, b) = (-r, -g, -b);
                }
                else if (signed)
                {
                    (r, g) = ((r * 2) - 1, gray ? (r * 2) - 1 : (g * 2) - 1);
                    b = gray ? r : b;
                }

                image.Frames[0][x, y] = new RgbaFloat(r, g, b, 1);
            }
        }

        var data = Save(image, new DdsEncoder { TextureFormat = format, AllowBitDepthReduction = true, FloatingPointConversion = floatingPoint ? null : new PixelConversionOptions() });
        using var decoded = Image.Load<RgbaFloat>(data);
        var maximum = 0f;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var expected = image.Frames[0][x, y];
                var actual = decoded.Frames[0][x, y];
                maximum = Math.Max(maximum, Math.Max(Math.Abs(expected.R - actual.R), Math.Abs(expected.G - actual.G)));
            }
        }

        Assert.InRange(maximum, 0f, signed ? 0.03f : 0.02f);
    }

    [Fact]
    public void Bc1KeepsOneBitAlpha()
    {
        using var image = new Image<Rgba32>(8, 8);
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                image.Frames[0][x, y] = (x + y) % 3 == 0 ? new Rgba32(0, 0, 0, 0) : new Rgba32(200, 100, 50, 255);
            }
        }

        using var decoded = Image.Load<Rgba32>(Save(image, new DdsEncoder { TextureFormat = DdsTextureFormat.Bc1Unorm }));
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                Assert.Equal(image.Frames[0][x, y].A, decoded.Frames[0][x, y].A);
            }
        }
    }

    internal static double GetPsnr(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        double sum = 0;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                var a = expected.Frames[0][x, y];
                var b = actual.Frames[0][x, y];
                sum += Square(a.R - b.R) + Square(a.G - b.G) + Square(a.B - b.B) + Square(a.A - b.A);
            }
        }

        var mse = sum / (4.0 * expected.Width * expected.Height);
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 / mse);

        static double Square(int value) => value * value;
    }

    [Fact]
    public void SaveSelectsTheEncoderOfTheExtension()
    {
        Assert.IsType<DdsEncoder>(ImageEncoder.FromFileExtension(".dds"));
        Assert.IsType<DdsEncoder>(ImageEncoder.Create(ImageFormat.Dds));
    }

    internal static Image<Rgba32> CreateRgba32(int width, int height, int seed)
    {
        var image = new Image<Rgba32>(width, height);
        var state = (uint)seed * 2654435761u;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                state = (state * 1664525) + 1013904223;
                image.Frames[0][x, y] = new Rgba32((byte)(state >> 24), (byte)(state >> 16), (byte)(state >> 8), (byte)state);
            }
        }

        return image;
    }

    internal static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var output = new MemoryStream();
        image.Save(output, encoder);
        return output.ToArray();
    }

    internal static byte[] SaveCollection(ImageCollection collection, ImageEncoder encoder)
    {
        using var output = new MemoryStream();
        collection.Save(output, encoder);
        return output.ToArray();
    }

    private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private static void AssertEqualPixels<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged, IEquatable<TPixel>
    {
        Assert.Equal(expected.Size, actual.Size);
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected.Frames[0][x, y], actual.Frames[0][x, y]);
            }
        }
    }
}
