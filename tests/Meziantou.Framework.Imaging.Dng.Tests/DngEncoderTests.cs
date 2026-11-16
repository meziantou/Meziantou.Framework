using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// DNG encoding: the tags are read from the image file directory with the offsets of TIFF 6.0, the stored samples with
/// <see cref="DngRawImage"/>, and the rendering with the decoder of the library. The interop tests validate the files with
/// the DNG SDK.
/// </summary>
public sealed class DngEncoderTests
{
    [Theory]
    [InlineData(DngCompression.None, null)]
    [InlineData(DngCompression.None, 16)]
    [InlineData(DngCompression.LosslessJpeg, null)]
    [InlineData(DngCompression.LosslessJpeg, 32)]
    public void SrgbSamplesAreStoredWithALinearizationTable(DngCompression compression, int? tileSize)
    {
        using var image = CreatePattern(37, 21);
        var data = Save(image, new DngEncoder { Compression = compression, TileSize = tileSize });
        var tags = ReadTags(data);
        Assert.Equal(34892u, tags[262][0]);
        Assert.Equal(compression == DngCompression.LosslessJpeg ? 7u : 1u, tags[259][0]);
        Assert.Equal([8u, 8u, 8u], tags[258]);
        Assert.HasCount(256, tags[50712]);
        Assert.Equal([65535u, 65535u, 65535u], tags[50717]);
        Assert.Equal(tileSize is not null || compression == DngCompression.LosslessJpeg, tags.ContainsKey(322));
        if (tags.TryGetValue(322, out var tileWidth))
        {
            // A tile is never larger than the image
            Assert.Equal((uint)Math.Min(tileSize ?? 256, 37), tileWidth[0]);
            Assert.Equal((uint)Math.Min(tileSize ?? 256, 21), tags[323][0]);
        }
        Assert.Equal(ImageFormat.Dng, Image.DetectFormat(data));

        // The stored samples are those of the image
        using (var raw = DngRawImage.Load(data))
        {
            Assert.False(raw.IsColorFilterArray);
            Assert.Equal(3, raw.SamplesPerPixel);
            Assert.Equal(8, raw.BitsPerSample);
            Assert.Equal(DngEncoderLinearization(8), raw.LinearizationTable!.ToArray());
            var row = new float[37 * 3];
            for (var y = 0; y < 21; y++)
            {
                raw.CopyStoredRow(y, row);
                for (var x = 0; x < 37; x++)
                {
                    var pixel = image.Frames[0][x, y];
                    Assert.Equal([pixel.R, pixel.G, pixel.B], row.AsSpan(x * 3, 3).ToArray().Select(v => (byte)v));
                }
            }
        }

        // The rendering is the image in linear light
        using var decoded = Image.Load<RgbaFloat>(data);
        Assert.Equal(ColorTransferFunction.Linear, decoded.Metadata.TransferFunction);
        Assert.Equal(new Size(37, 21), decoded.Size);
        for (var y = 0; y < 21; y++)
        {
            for (var x = 0; x < 37; x++)
            {
                var expected = image.Frames[0][x, y];
                var actual = decoded.Frames[0][x, y];
                AssertClose(SrgbToLinear(expected.R), actual.R, 3e-5);
                AssertClose(SrgbToLinear(expected.G), actual.G, 3e-5);
                AssertClose(SrgbToLinear(expected.B), actual.B, 3e-5);
            }
        }
    }

    [Fact]
    public void LinearImagesAreStoredWithoutTable()
    {
        using var source = CreatePattern(9, 5);
        using var image = source.CloneAs<Rgb48>();
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var data = Save(image, new DngEncoder());
        var tags = ReadTags(data);
        Assert.DoesNotContain(50712, tags.Keys);
        Assert.Equal([16u, 16u, 16u], tags[258]);
        Assert.Equal([65535u, 65535u, 65535u], tags[50717]);
        using var decoded = Image.Load<RgbaFloat>(data);
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                var expected = image.Frames[0][x, y];
                var actual = decoded.Frames[0][x, y];
                AssertClose(expected.R / 65535.0, actual.R, 3e-6);
                AssertClose(expected.G / 65535.0, actual.G, 3e-6);
                AssertClose(expected.B / 65535.0, actual.B, 3e-6);
            }
        }
    }

    [Theory]
    [InlineData(PixelFormat.Gray8, 8)]
    [InlineData(PixelFormat.Gray16, 16)]
    [InlineData(PixelFormat.La16, 8)]
    public void GrayImagesAreMonochrome(PixelFormat format, int bits)
    {
        using var source = CreatePattern(19, 7);
        using var image = source.CloneAs(format, options: null);
        var data = Save(image, new DngEncoder());
        var tags = ReadTags(data);
        Assert.Equal(1u, tags[277][0]);
        Assert.Equal((uint)bits, tags[258][0]);
        Assert.DoesNotContain(50721, tags.Keys);
        using var raw = DngRawImage.Load(data);
        Assert.Equal(1, raw.SamplesPerPixel);
        using var expected = image.CloneAs<Gray16>();
        var row = new float[19];
        for (var y = 0; y < 7; y++)
        {
            raw.CopyStoredRow(y, row);
            for (var x = 0; x < 19; x++)
            {
                Assert.Equal(bits == 8 ? expected.Frames[0][x, y].Value / 257 : expected.Frames[0][x, y].Value, (int)row[x]);
            }
        }

        using var decoded = Image.Load<RgbaFloat>(data);
        var pixel = decoded.Frames[0][3, 2];
        Assert.Equal(pixel.R, pixel.G);
        Assert.Equal(pixel.R, pixel.B);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DngCompression.None)]
    [InlineData(DngCompression.Deflate)]
    public void FloatingPointSamplesAreStoredAsTheyAre(DngCompression? compression)
    {
        using var image = new Image<RgbaFloat>(5, 3);
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                image.Frames[0][x, y] = new RgbaFloat(x * 0.75f, y * 0.125f, (x + y) / 7f, 1);
            }
        }

        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var data = Save(image, new DngEncoder { Compression = compression });
        var tags = ReadTags(data);
        Assert.Equal(compression == DngCompression.None ? 1u : 8u, tags[259][0]);
        Assert.Equal([3u, 3u, 3u], tags[339]);
        Assert.Equal([32u, 32u, 32u], tags[258]);
        using (var raw = DngRawImage.Load(data))
        {
            Assert.True(raw.IsFloatingPoint);
            var row = new float[15];
            raw.CopyStoredRow(1, row);
            Assert.Equal(0.75f, row[3]);
            Assert.Equal(0.125f, row[4]);
        }

        using var decoded = Image.Load<RgbaFloat>(data);
        for (var y = 0; y < 3; y++)
        {
            for (var x = 0; x < 5; x++)
            {
                var expected = image.Frames[0][x, y];
                var actual = decoded.Frames[0][x, y];
                AssertClose(expected.R, actual.R, 1e-5 * Math.Max(1, expected.R));
                AssertClose(expected.G, actual.G, 1e-5);
                AssertClose(expected.B, actual.B, 1e-5);
            }
        }

        // The highlights keep their values: a white level of 3 and an exposure of log2(3)
        Assert.Equal([3u, 3u, 3u], tags[50717]);
        Assert.Equal((uint)Math.Round(Math.Log2(3) * 1_000_000, MidpointRounding.AwayFromZero), tags[50730][0]);

        // Samples labeled sRGB would be read as linear light, and negative samples are no light
        image.Metadata.TransferFunction = ColorTransferFunction.Srgb;
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DngEncoder()));
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        image.Frames[0][0, 0] = new RgbaFloat(-0.5f, 0, 0, 1);
        Assert.Equal("Negative or non-finite samples", Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DngEncoder())).Feature);
    }

    [Fact]
    public void TheCameraProfileIsTheOneOfLinearBt709()
    {
        using var image = CreatePattern(4, 4);
        var tags = ReadTags(Save(image, new DngEncoder()));
        Assert.Equal([1u, 4u, 0u, 0u], tags[50706]);
        Assert.Equal([1u, 1u, 0u, 0u], tags[50707]);
        Assert.Equal(21u, tags[50778][0]);
        Assert.HasCount(9, tags[50721]);
        Assert.HasCount(9, tags[50964]);
        Assert.Equal(1u, tags[51110][0]);

        var info = Image.Identify(Save(image, new DngEncoder()));
        Assert.Equal(ImageFormat.Dng, info.Format);
        Assert.Equal("Meziantou.Framework.Imaging", info.Metadata.DngProperties!.UniqueCameraModel);
        Assert.Equal(new Version(1, 4, 0, 0), info.Metadata.DngProperties.Version);
    }

    [Fact]
    public void TheOrientationAndXmpAreWritten()
    {
        using var image = CreatePattern(6, 4);
        image.Metadata.Orientation = ExifOrientation.RightTop;
        var xmp = Encoding.UTF8.GetBytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>");
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob(xmp));
        var data = Save(image, new DngEncoder { Compression = DngCompression.None });
        using var decoded = Image.Load(data);
        Assert.Equal(ExifOrientation.RightTop, decoded.Metadata.Orientation);
        Assert.Equal(xmp, decoded.Metadata.XmpProfile!.Data.ToArray());

        // An ICC profile cannot describe the camera space
        using var icc = CreatePattern(2, 2);
        icc.Metadata.IccProfile = IccProfile.Srgb;
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(icc, new DngEncoder()));
        Assert.Null(Image.Identify(Save(icc, new DngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported })).Metadata.IccProfile);
    }

    [Fact]
    public void UnsupportedImagesAreRejectedBeforeAnyOutput()
    {
        using var transparent = CreatePattern(3, 3);
        transparent.Frames[0][1, 1] = new Rgba32(1, 2, 3, 4);
        Assert.Equal("Transparency", Assert.Throws<UnsupportedImageFeatureException>(() => Save(transparent, new DngEncoder())).Feature);

        using var floats = new Image<RgbaFloat>(2, 2);
        floats.Metadata.TransferFunction = ColorTransferFunction.Linear;
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(floats, new DngEncoder { Compression = DngCompression.LosslessJpeg }));
        using var integers = CreatePattern(2, 2);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(integers, new DngEncoder { Compression = DngCompression.Deflate }));

        using var cmyk = new Image<Cmyk32>(2, 2);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(cmyk, new DngEncoder()));

        // The image file directory follows the image data: the header gets its offset by a patch
        using var stream = new NonSeekableStream();
        Assert.Throws<ArgumentException>(() => integers.Save(stream, new DngEncoder()));
        Assert.Equal(0, stream.Written);
    }

    [Fact]
    public void TheSettingsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DngEncoder { Compression = (DngCompression)3 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DngEncoder { TileSize = 15 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DngEncoder { TileSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new DngEncoder { TileSize = 65552 });
        Assert.Null(new DngEncoder().Compression);
        Assert.Null(new DngEncoder().TileSize);
        Assert.IsType<DngEncoder>(ImageEncoder.FromFileExtension(".dng"));
        Assert.IsType<DngEncoder>(ImageEncoder.Create(ImageFormat.Dng));

        using var image = CreatePattern(2, 2);
        image.InsertFrame(1, image.Frames[0]);
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(image, new DngEncoder()));
    }

    private static ushort[] DngEncoderLinearization(int bits)
    {
        var count = 1 << bits;
        var table = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            table[i] = (ushort)Math.Round(SrgbToLinear(i * 255.0 / (count - 1)) * 65535, MidpointRounding.AwayFromZero);
        }

        return table;
    }

    private static double SrgbToLinear(double value)
    {
        var v = value / 255;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static void AssertClose(double expected, double actual, double tolerance)
        => Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected}, got {actual}");

    /// <summary>Reads the tags of the first image file directory of a little-endian TIFF file as unsigned numbers (the numerators of rationals).</summary>
    private static Dictionary<int, uint[]> ReadTags(byte[] data)
    {
        Assert.Equal("II*\0", Encoding.ASCII.GetString(data, 0, 4));
        var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        var tags = new Dictionary<int, uint[]>();
        for (var i = 0; i < count; i++)
        {
            var entry = data.AsSpan(offset + 2 + (i * 12), 12);
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            var n = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            var size = type switch
            {
                1 or 2 or 6 or 7 => 1,
                3 or 8 => 2,
                5 or 10 or 12 => 8,
                _ => 4,
            };

            var values = n * size <= 4 ? entry.Slice(8, 4) : data.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]), n * size);
            var result = new uint[n];
            for (var j = 0; j < n; j++)
            {
                result[j] = size switch
                {
                    1 => values[j],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(values[(j * 2)..]),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(values[(j * size)..]),
                };
            }

            tags[tag] = result;
        }

        return tags;
    }

    private static Image<Rgba32> CreatePattern(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.Frames[0][x, y] = new Rgba32((byte)(x * 7), (byte)(255 - (y * 11)), (byte)((x * 3) + (y * 13)), 255);
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

    private sealed class NonSeekableStream : Stream
    {
        public long Written { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Written += count;
    }
}
