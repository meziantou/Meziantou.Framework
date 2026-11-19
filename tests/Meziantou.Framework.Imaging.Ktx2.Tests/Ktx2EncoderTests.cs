using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// KTX 2.0 encoding: the header, the level index, the data format descriptor and the key/value data are checked byte by
/// byte against the KTX File Format Specification 2.0 and the descriptors KTX-Software 4.4 writes (<c>ktx create</c>), and
/// the files are read back by the decoder of the library. The interop tests validate them with <c>ktx validate</c>.
/// </summary>
public sealed class Ktx2EncoderTests
{
    [Fact]
    public void DefaultOutputIsLosslessSrgbRgba8()
    {
        using var image = CreateRgba32(5, 3, seed: 1);
        var data = Save(image, new Ktx2Encoder());
        Assert.Equal(new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A }, data[..12]);

        // vkFormat R8G8B8A8_SRGB, typeSize 1, 5x3, no depth, no layers, one face, one level, no supercompression
        Assert.Equal(new uint[] { 43, 1, 5, 3, 0, 0, 1, 1, 0 }, ReadUInt32s(data, 12, 9));

        // The data format descriptor follows the level index: RGBSDA, BT.709, sRGB, the samples of KTX-Software
        var dfdOffset = (int)ReadUInt32(data, 48);
        Assert.Equal(80 + 24, dfdOffset);
        Assert.Equal(92u, ReadUInt32(data, 52));
        byte[] dfd =
        [
            92, 0, 0, 0, 0, 0, 0, 0, 2, 0, 88, 0, 1, 1, 2, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 7, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0, 0, 0,
            8, 0, 7, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0, 0, 0,
            16, 0, 7, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0, 0, 0,
            24, 0, 7, 0x1F, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0, 0, 0,
        ];
        Assert.Equal(dfd, data[dfdOffset..(dfdOffset + 92)]);

        // The key/value data: KTXwriter only
        var kvd = data.AsSpan((int)ReadUInt32(data, 56), (int)ReadUInt32(data, 60)).ToArray();
        Assert.Equal(GetKeyValue("KTXwriter", "Meziantou.Framework.Imaging"), kvd);

        // The level: at a multiple of 4, its length, and the uncompressed length
        var (offset, length, uncompressed) = ReadLevel(data, 0);
        Assert.Equal(0ul, offset % 4);
        Assert.Equal(60ul, length);
        Assert.Equal(60ul, uncompressed);
        Assert.Equal((ulong)data.Length, offset + length);

        using var decoded = Image.Load<Rgba32>(data);
        AssertEqualPixels(image, decoded);
        Assert.Equal(ColorTransferFunction.Srgb, decoded.Metadata.TransferFunction);
        Assert.Equal(43, decoded.Metadata.Ktx2Header!.VkFormat);
    }

    [Fact]
    public void LinearImagesAreUnormWithTheLinearTransferFunction()
    {
        using var image = CreateRgba32(2, 2, seed: 2);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var data = Save(image, new Ktx2Encoder());
        Assert.Equal(37u, ReadUInt32(data, 12));
        Assert.Equal(1, data[(int)ReadUInt32(data, 48) + 14]);
        using var decoded = Image.Load<Rgba32>(data);
        AssertEqualPixels(image, decoded);
        Assert.Equal(ColorTransferFunction.Linear, decoded.Metadata.TransferFunction);
    }

    [Fact]
    public void GrayImagesAreRedWithASwizzle()
    {
        using var gray = new Image<Gray8>(3, 1);
        gray.Frames[0][0, 0] = new Gray8(10);
        gray.Frames[0][2, 0] = new Gray8(250);
        var data = Save(gray, new Ktx2Encoder());
        Assert.Equal(15u, ReadUInt32(data, 12));
        var kvd = data.AsSpan((int)ReadUInt32(data, 56), (int)ReadUInt32(data, 60)).ToArray();
        Assert.Equal([.. GetKeyValue("KTXswizzle", "rrr1"), .. GetKeyValue("KTXwriter", "Meziantou.Framework.Imaging")], kvd);
        using var decoded = Image.Load<Gray8>(data);
        AssertEqualPixels(gray, decoded);

        using var wide = new Image<Gray16>(2, 1);
        wide.Frames[0][1, 0] = new Gray16(54321);
        data = Save(wide, new Ktx2Encoder());
        Assert.Equal(70u, ReadUInt32(data, 12));

        // R16 has no sRGB variant: the descriptor carries the transfer function
        Assert.Equal(2, data[(int)ReadUInt32(data, 48) + 14]);
        using var decodedWide = Image.Load<Gray16>(data);
        AssertEqualPixels(wide, decodedWide);
    }

    [Fact]
    public void SixteenBitAndFloatingPointPixelsKeepTheirPrecision()
    {
        using var wide = new Image<Rgba64>(3, 2);
        wide.Frames[0][0, 0] = new Rgba64(1, 2, 65535, 40000);
        var data = Save(wide, new Ktx2Encoder());
        Assert.Equal(91u, ReadUInt32(data, 12));
        Assert.Equal(2u, ReadUInt32(data, 16));
        using (var decoded = Image.Load<Rgba64>(data))
        {
            AssertEqualPixels(wide, decoded);
        }

        using var floats = new Image<RgbaFloat>(2, 2) { Metadata = { TransferFunction = ColorTransferFunction.Linear } };
        floats.Frames[0][0, 0] = new RgbaFloat(-3.5f, 1e20f, 0.1f, 1f);
        data = Save(floats, new Ktx2Encoder());
        Assert.Equal(109u, ReadUInt32(data, 12));
        Assert.Equal(1, data[(int)ReadUInt32(data, 48) + 14]);
        using (var decoded = Image.Load<RgbaFloat>(data))
        {
            AssertEqualPixels(floats, decoded);
        }

        using var halves = new Image<RgbaHalf>(2, 1) { Metadata = { TransferFunction = ColorTransferFunction.Linear } };
        halves.Frames[0][1, 0] = new RgbaHalf((Half)1.5f, (Half)(-2f), (Half)65504f, (Half)1f);
        data = Save(halves, new Ktx2Encoder());
        Assert.Equal(97u, ReadUInt32(data, 12));
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
        var data = Save(halves, new Ktx2Encoder { Supercompression = Ktx2Supercompression.Zlib });
        Assert.Equal(97u, ReadUInt32(data, 12));
        using var decoded = Image.Load<RgbaHalf>(data);
        var actual = new byte[bytes.Length];
        decoded.Frames[0].CopyPixelBytesTo(actual);
        Assert.Equal(bytes, actual);
    }

    [Fact]
    public void MipLevelsAreStoredFromTheSmallest()
    {
        using var image = CreateRgba32(8, 4, seed: 3);
        var data = Save(image, new Ktx2Encoder { GenerateMipmaps = true });
        Assert.Equal(4u, ReadUInt32(data, 40));
        var levels = Enumerable.Range(0, 4).Select(level => ReadLevel(data, level)).ToArray();
        Assert.Equal([128ul, 32, 8, 4], levels.Select(level => level.Length));
        for (var level = 1; level < 4; level++)
        {
            Assert.Equal(levels[level].Offset + levels[level].Length, levels[level - 1].Offset);
        }

        using var collection = ImageCollection.Load(data);
        Assert.Equal([new Size(8, 4), new Size(4, 2), new Size(2, 1), new Size(1, 1)], collection.Entries.Select(entry => entry.Size));
        using var level0 = collection[0].Decode<Rgba32>();
        AssertEqualPixels(image, level0);
    }

    [Fact]
    public void ZlibSupercompressionDecodesToTheSameTexels()
    {
        using var image = CreateRgba32(16, 16, seed: 4);
        var plain = Save(image, new Ktx2Encoder { GenerateMipmaps = true });
        var data = Save(image, new Ktx2Encoder { GenerateMipmaps = true, Supercompression = Ktx2Supercompression.Zlib });
        Assert.Equal(3u, ReadUInt32(data, 44));
        Assert.Equal(ReadLevel(plain, 0).Length, ReadLevel(data, 0).Uncompressed);
        using var expected = ImageCollection.Load(plain);
        using var actual = ImageCollection.Load(data);
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            using var left = expected[i].Decode<Rgba32>();
            using var right = actual[i].Decode<Rgba32>();
            AssertEqualPixels(left, right);
        }
    }

    [Fact]
    public void OrientationAndPrimariesAreWritten()
    {
        using var image = CreateRgba32(2, 2, seed: 5);
        image.Metadata.Orientation = ExifOrientation.BottomLeft;
        image.Metadata.Cicp = new CicpColorDescription(12, 13, 0, fullRange: true);
        var data = Save(image, new Ktx2Encoder());
        var kvd = data.AsSpan((int)ReadUInt32(data, 56), (int)ReadUInt32(data, 60)).ToArray();
        Assert.Equal([.. GetKeyValue("KTXorientation", "ru"), .. GetKeyValue("KTXwriter", "Meziantou.Framework.Imaging")], kvd);
        Assert.Equal(10, data[(int)ReadUInt32(data, 48) + 13]);
        using var decoded = Image.Load(data);
        Assert.Equal(ExifOrientation.BottomLeft, decoded.Metadata.Orientation);
        Assert.Equal(12, decoded.Metadata.Cicp!.ColorPrimaries);

        // A transposition cannot be written
        image.Metadata.Orientation = ExifOrientation.LeftTop;
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new Ktx2Encoder()));
        Assert.NotEmpty(Save(image, new Ktx2Encoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
    }

    [Fact]
    public void CubeMapsAndArraysAreSavedFromACollection()
    {
        using var collection = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        var images = new List<Image<Rgba32>>();
        try
        {
            foreach (var face in Enum.GetValues<CubeMapFace>().Reverse())
            {
                var image = CreateRgba32(4, 4, seed: 20 + (int)face);
                images.Add(image);
                collection.Add(image, new TextureSurface(face: face));
            }

            var data = SaveCollection(collection, new Ktx2Encoder { GenerateMipmaps = true });
            Assert.Equal(new uint[] { 4, 4, 0, 0, 6, 3 }, ReadUInt32s(data, 20, 6));
            using var loaded = ImageCollection.Load(data);
            Assert.Equal(6 * 3, loaded.Count);
            foreach (var entry in loaded.Entries.Where(entry => entry.TextureSurface!.MipLevel == 0))
            {
                using var decoded = entry.Decode<Rgba32>();
                AssertEqualPixels(images[5 - (int)entry.TextureSurface!.Face!.Value], decoded);
            }

            using var array = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
            array.Add(images[0], new TextureSurface(1));
            array.Add(images[1], new TextureSurface(0));
            data = SaveCollection(array, new Ktx2Encoder());
            Assert.Equal(new uint[] { 4, 4, 0, 2, 1, 1 }, ReadUInt32s(data, 20, 6));
            using var loadedArray = ImageCollection.Load(data);
            using var layer1 = loadedArray.Entries.Single(entry => entry.TextureSurface!.ArrayIndex == 1).Decode<Rgba32>();
            AssertEqualPixels(images[0], layer1);
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
    public void SurfacesWithAnotherColorLabelAreRejectedUnlessDiscarded()
    {
        // A texture has one transfer function: a linear surface after an sRGB one would be labeled sRGB
        using var srgb = CreateRgba32(4, 4, seed: 1);
        using var linear = CreateRgba32(4, 4, seed: 2);
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using var collection = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        collection.Add(srgb, new TextureSurface(0));
        collection.Add(linear, new TextureSurface(1));
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => SaveCollection(collection, new Ktx2Encoder()));
        Assert.Equal("Metadata: labels of a surface", exception.Feature);

        // A policy that discards what the format cannot store labels every surface as the first one
        var data = SaveCollection(collection, new Ktx2Encoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        using var loaded = ImageCollection.Load(data);
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void SurfacesThatDescribeTheSameLabelDifferentlyAreSaved()
    {
        // Linear light, with and without the CICP description of BT.709 and linear light: one label in a KTX 2.0 file
        using var described = new Image<RgbaFloat>(4, 4, new RgbaFloat(0.25f, 0.5f, 0.75f, 1f));
        using var plain = new Image<RgbaFloat>(4, 4, new RgbaFloat(0.5f, 0.25f, 0.125f, 1f));
        described.Metadata.Cicp = new CicpColorDescription(1, 8, 0, fullRange: true);
        using var collection = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        collection.Add(described, new TextureSurface(0));
        collection.Add(plain, new TextureSurface(1));
        using var loaded = ImageCollection.Load(SaveCollection(collection, new Ktx2Encoder()));
        Assert.Equal(2, loaded.Count);
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
        var exception = Assert.Throws<ArgumentException>(() => SaveCollection(collection, new Ktx2Encoder()));
        Assert.Contains("another texture format", exception.Message, StringComparison.Ordinal);

        // An explicit texture format is the same for every surface: a linear-light surface of integers is stored as floats
        using var linear = CreateRgba32(4, 4, seed: 5);
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using var explicitFormat = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        explicitFormat.Add(floats, new TextureSurface(0));
        explicitFormat.Add(linear, new TextureSurface(1));
        using var loaded = ImageCollection.Load(SaveCollection(explicitFormat, new Ktx2Encoder { TextureFormat = Ktx2TextureFormat.R32G32B32A32Sfloat }));
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void ASparseArrayIndexIsRejectedBeforeItsSurfaceTableIsAllocated()
    {
        // One surface at the array index 10^8: a texture of 10^8 elements, of which the collection holds one
        using var image = CreateRgba32(1, 1, seed: 3);
        using var collection = ImageCollection.Create(ImageCollectionKind.TextureSurfaces);
        collection.Add(image, new TextureSurface(100_000_000));
        var exception = Assert.Throws<ArgumentException>(() => SaveCollection(collection, new Ktx2Encoder()));
        Assert.Contains("is missing", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ktx2TextureFormat.Bc1Rgb, 30)]
    [InlineData(Ktx2TextureFormat.Bc7, 37)]
    [InlineData(Ktx2TextureFormat.Etc2Rgb, 33)]
    [InlineData(Ktx2TextureFormat.Etc2Rgba, 33)]
    [InlineData(Ktx2TextureFormat.Astc4x4, 38)]
    [InlineData(Ktx2TextureFormat.Astc8x8, 28)]
    [InlineData(Ktx2TextureFormat.Astc12x12, 24)]
    public void BlockCompressedFormatsRoundTripWithinTheirBounds(Ktx2TextureFormat format, double minimumPsnr)
    {
        var opaque = format is Ktx2TextureFormat.Bc1Rgb or Ktx2TextureFormat.Etc2Rgb;
        using var image = new Image<Rgba32>(38, 27);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                image.Frames[0][x, y] = new Rgba32((byte)(x * 6), (byte)(y * 9), (byte)(128 + (64 * Math.Sin(x / 5.0))), opaque ? (byte)255 : (byte)(255 - (x * 3)));
            }
        }

        var encoder = new Ktx2Encoder { TextureFormat = format, Quality = TextureCompressionQuality.Fast };
        var data = Save(image, encoder);
        Assert.Equal(data, Save(image, encoder));
        using var decoded = Image.Load<Rgba32>(data);
        double sum = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var a = image.Frames[0][x, y];
                var b = decoded.Frames[0][x, y];
                sum += ((a.R - b.R) * (a.R - b.R)) + ((a.G - b.G) * (a.G - b.G)) + ((a.B - b.B) * (a.B - b.B)) + ((a.A - b.A) * (a.A - b.A));
            }
        }

        var mse = sum / (4.0 * image.Width * image.Height);
        Assert.InRange(mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 / mse), minimumPsnr, double.PositiveInfinity);
    }

    [Theory]
    [InlineData(Ktx2TextureFormat.EacR11Unorm)]
    [InlineData(Ktx2TextureFormat.EacR11G11Unorm)]
    [InlineData(Ktx2TextureFormat.EacR11Snorm)]
    [InlineData(Ktx2TextureFormat.EacR11G11Snorm)]
    public void EacRoundTripsWithinItsBound(Ktx2TextureFormat format)
    {
        var signed = format is Ktx2TextureFormat.EacR11Snorm or Ktx2TextureFormat.EacR11G11Snorm;
        var gray = format is Ktx2TextureFormat.EacR11Unorm or Ktx2TextureFormat.EacR11Snorm;
        using var image = new Image<RgbaFloat>(16, 12) { Metadata = { TransferFunction = ColorTransferFunction.Linear } };
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                // The unsigned formats take the floats as 16-bit samples
                var r = signed ? ((x / 15f) * 2) - 1 : x / 15f;
                var g = gray ? r : signed ? ((y / 11f) * 2) - 1 : y / 11f;
                image.Frames[0][x, y] = new RgbaFloat(r, g, gray ? r : 0, 1);
            }
        }

        var data = Save(image, new Ktx2Encoder { TextureFormat = format, AllowBitDepthReduction = true, FloatingPointConversion = signed ? null : new PixelConversionOptions() });
        using var decoded = Image.Load<RgbaFloat>(data);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                Assert.Equal(image.Frames[0][x, y].R, decoded.Frames[0][x, y].R, 0.02f);
                Assert.Equal(image.Frames[0][x, y].G, decoded.Frames[0][x, y].G, 0.02f);
            }
        }
    }

    [Fact]
    public void SaveSelectsTheEncoderOfTheExtension()
    {
        Assert.IsType<Ktx2Encoder>(ImageEncoder.FromFileExtension(".ktx2"));
        Assert.IsType<Ktx2Encoder>(ImageEncoder.Create(ImageFormat.Ktx2));
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

    private static byte[] GetKeyValue(string key, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(key + "\0" + value + "\0");
        var padded = new byte[4 + ((bytes.Length + 3) & ~3)];
        BinaryPrimitives.WriteUInt32LittleEndian(padded, (uint)bytes.Length);
        bytes.CopyTo(padded, 4);
        return padded;
    }

    private static (ulong Offset, ulong Length, ulong Uncompressed) ReadLevel(byte[] data, int level)
    {
        var entry = data.AsSpan(80 + (24 * level));
        return (BinaryPrimitives.ReadUInt64LittleEndian(entry), BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]), BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]));
    }

    private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

    private static uint[] ReadUInt32s(byte[] data, int offset, int count) => [.. Enumerable.Range(0, count).Select(i => ReadUInt32(data, offset + (4 * i)))];

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
