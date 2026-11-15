using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Radiance;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Radiance HDR encoding: the files are read back by <see cref="ReferenceRadiance"/>, and their pixels compared with the
/// conversion of "Real Pixels" (<see cref="RadianceFileBuilder.ToRgbe"/>), both independent of the library.
/// </summary>
public sealed class RadianceEncoderTests
{
    [Theory]
    [InlineData(RadianceCompression.RunLength, 9, RadianceScanlineEncoding.NewRunLength)]
    [InlineData(RadianceCompression.RunLength, 7, RadianceScanlineEncoding.Flat)]
    [InlineData(RadianceCompression.None, 9, RadianceScanlineEncoding.Flat)]
    public void PixelsAreTheConversionOfRealPixels(RadianceCompression compression, int width, RadianceScanlineEncoding encoding)
    {
        using var image = Opaque(width, 3);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Runs of equal pixels, colors of very different magnitudes, black, values above 1
                image.Frames[0][x, y] = x < 4 ? new RgbaFloat(1f, 0.5f, 0.25f, 1f) : new RgbaFloat(x * 1000.5f, y * 0.001f, MathF.ScaleB(1, -x), 1f);
            }
        }

        image.Frames[0][4, 2] = new RgbaFloat(0f, 0f, 0f, 1f);
        image.Frames[0][5, 2] = new RgbaFloat(1e-33f, 1e-33f, 0f, 1f);
        var data = Save(image, new RadianceEncoder { Compression = compression });
        var picture = ReferenceRadiance.Read(data);
        Assert.Equal("-Y +X", picture.ScanOrder);
        Assert.Equal(Enumerable.Repeat(encoding, 3).ToArray(), picture.Encodings.ToArray());
        Assert.Equal("32-bit_rle_rgbe", picture.GetVariable("FORMAT"));
        Assert.Equal(" 0.6400 0.3300 0.3000 0.6000 0.1500 0.0600 0.3127 0.3290", picture.GetVariable("PRIMARIES"));
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = image.Frames[0][x, y];
                Assert.Equal(RadianceFileBuilder.ToRgbe(pixel.R, pixel.G, pixel.B), picture.Rgbe.Span.Slice(((y * width) + x) * 4, 4).ToArray());
            }
        }

        // Each color comes back within half a unit of the last place of the largest color of its pixel
        using var decoded = Image.Load<RgbaFloat>(data);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = image.Frames[0][x, y];
                var result = decoded.Frames[0][x, y];
                var max = Math.Max(source.R, Math.Max(source.G, source.B));
                var half = max <= 1e-32 ? 1e-32 : Math.ScaleB(1.0, Math.ILogB(max) - 8);
                Assert.True(Math.Abs(result.R - source.R) <= half && Math.Abs(result.G - source.G) <= half && Math.Abs(result.B - source.B) <= half, $"({x}, {y}): {source} became {result}.");
            }
        }
    }

    [Fact]
    public void TheHeaderIsTheStandardOne()
    {
        using var image = Opaque(2, 1);
        var data = Save(image, new RadianceEncoder());
        var header = Encoding.ASCII.GetString(data, 0, data.Length - 8);
        Assert.Equal("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\nPRIMARIES= 0.6400 0.3300 0.3000 0.6000 0.1500 0.0600 0.3127 0.3290\n\n-Y 1 +X 2\n", header);
        Assert.Equal(new byte[8], data.AsSpan(data.Length - 8).ToArray());
    }

    [Fact]
    public void LongScanlinesAreFlat()
    {
        using var image = Opaque(0x8000, 1);
        var picture = ReferenceRadiance.Read(Save(image, new RadianceEncoder()));
        Assert.Equal(RadianceScanlineEncoding.Flat, picture.Encodings[0]);
    }

    [Fact]
    public void LongRunsAndLiteralsRoundTrip()
    {
        using var image = Opaque(0x7FFF, 2);
        for (var x = 0; x < image.Width; x++)
        {
            image.Frames[0][x, 0] = new RgbaFloat(x < 1000 ? 1f : 1f + (x % 128 / 128f), x % 3, 0f, 1f);
            image.Frames[0][x, 1] = new RgbaFloat(x, 1f, x % 7 == 0 ? 3f : 0f, 1f);
        }

        var data = Save(image, new RadianceEncoder());
        var picture = ReferenceRadiance.Read(data);
        Assert.Equal(new[] { RadianceScanlineEncoding.NewRunLength, RadianceScanlineEncoding.NewRunLength }, picture.Encodings.ToArray());
        using var decoded = Image.Load<RgbaFloat>(data);
        Assert.Equal(picture.GetFloatPixels(), GetBytes(decoded));
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(-0.0001f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(1.7014118346046923E+38f)]
    [InlineData(float.MaxValue)]
    public void ColorsThatCannotBeStoredAreRejectedBeforeAnythingIsWritten(float value)
    {
        using var image = Opaque(3, 2);
        image.Frames[0][2, 1] = new RgbaFloat(1f, value, 0f, 1f);
        using var output = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(output, new RadianceEncoder()));
        Assert.Equal("Colors outside the range of RGBE", exception.Feature);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void TheLargestColorBelow2Pow127IsStored()
    {
        using var image = Opaque(1, 1);
        var largest = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(1.7014118346046923E+38f) - 1);
        image.Frames[0][0, 0] = new RgbaFloat(largest, 0f, 0f, 1f);
        var picture = ReferenceRadiance.Read(Save(image, new RadianceEncoder()));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, picture.Rgbe.ToArray());
    }

    [Fact]
    public void NonOpaquePixelsNeedABackground()
    {
        using var image = Opaque(2, 1);
        image.Frames[0][0, 0] = new RgbaFloat(4f, 2f, 1f, 0.5f);
        image.Frames[0][1, 0] = new RgbaFloat(4f, 2f, 1f, 1f);
        using var output = new MemoryStream();
        Assert.Equal("Alpha removal", Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(output, new RadianceEncoder())).Feature);
        Assert.Equal(0, output.Length);

        var picture = ReferenceRadiance.Read(Save(image, new RadianceEncoder { BackgroundColor = new Rgba64(65535, 0, 0, 65535) }));
        Assert.Equal(RadianceFileBuilder.ToRgbe(2.5, 1, 0.5), picture.Rgbe.Span.Slice(0, 4).ToArray());
        Assert.Equal(RadianceFileBuilder.ToRgbe(4, 2, 1), picture.Rgbe.Span.Slice(4, 4).ToArray());
        Assert.Throws<ArgumentException>(() => new RadianceEncoder { BackgroundColor = new Rgba64(0, 0, 0, 1) });
    }

    [Fact]
    public void IntegerPixelsAreTheirFractionsOfOne()
    {
        using var image = new Image<Rgba32>(2, 1);
        image.Frames[0][0, 0] = new Rgba32(255, 51, 0, 255);
        image.Frames[0][1, 0] = new Rgba32(1, 2, 3, 255);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var picture = ReferenceRadiance.Read(Save(image, new RadianceEncoder()));
        Assert.Equal(RadianceFileBuilder.ToRgbe(1, 51 / 255.0, 0), picture.Rgbe.Span.Slice(0, 4).ToArray());
        Assert.Equal(RadianceFileBuilder.ToRgbe(1 / 255.0, 2 / 255.0, 3 / 255.0), picture.Rgbe.Span.Slice(4, 4).ToArray());

        using var gray = new Image<Gray16>(1, 1);
        gray.Frames[0][0, 0] = new Gray16(32768);
        gray.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var expected = RadianceFileBuilder.ToRgbe(32768 / 65535.0, 32768 / 65535.0, 32768 / 65535.0);
        Assert.Equal(expected, ReferenceRadiance.Read(Save(gray, new RadianceEncoder())).Rgbe.ToArray());
    }

    [Fact]
    public void HalfPixelsAreWritten()
    {
        using var image = new Image<RgbaHalf>(1, 1);
        image.Frames[0][0, 0] = new RgbaHalf((Half)3.5f, (Half)0.125f, (Half)65504f, (Half)1f);
        var picture = ReferenceRadiance.Read(Save(image, new RadianceEncoder()));
        Assert.Equal(RadianceFileBuilder.ToRgbe(3.5, 0.125, 65504), picture.Rgbe.ToArray());
    }

    [Fact]
    public void SrgbSamplesAreRejectedByDefault()
    {
        // An integer image is labeled sRGB: its samples are not linear light
        using var image = new Image<Rgba32>(1, 1);
        image.Frames[0][0, 0] = new Rgba32(10, 20, 30, 255);
        using var output = new MemoryStream();
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(output, new RadianceEncoder()));
        Assert.NotEmpty(Save(image, new RadianceEncoder { MetadataHandling = MetadataHandling.Strip }));
    }

    [Theory]
    [InlineData(9, " 0.7080 0.2920 0.1700 0.7970 0.1310 0.0460 0.3127 0.3290")]
    [InlineData(11, " 0.6800 0.3200 0.2650 0.6900 0.1500 0.0600 0.3140 0.3510")]
    [InlineData(12, " 0.6800 0.3200 0.2650 0.6900 0.1500 0.0600 0.3127 0.3290")]
    public void LinearCicpPrimariesAreWritten(byte primaries, string expected)
    {
        using var image = Opaque(1, 1);
        image.Metadata.Cicp = new CicpColorDescription(primaries, 8, 0, fullRange: true);
        var data = Save(image, new RadianceEncoder());
        Assert.Equal(expected, ReferenceRadiance.Read(data).GetVariable("PRIMARIES"));

        // The decoder labels the pixels the same way
        using var decoded = Image.Load(data);
        Assert.Equal(image.Metadata.Cicp, decoded.Metadata.Cicp);
    }

    [Fact]
    public void TheRadianceHeaderIsIgnored()
    {
        using var image = Opaque(1, 1);
        image.Metadata.RadianceHeader = new RadianceHeader(isXyz: true, 2, [1, 1, 1], primaries: null, 1, 2.2, "-vh 45", "x", "+X -Y");
        var picture = ReferenceRadiance.Read(Save(image, new RadianceEncoder()));
        Assert.Null(picture.GetVariable("EXPOSURE"));
        Assert.Equal("-Y +X", picture.ScanOrder);
    }

    [Fact]
    public void AnimationsAreRejected()
    {
        using var image = Opaque(1, 1);
        image.AppendFrame();
        for (var y = 0; y < 1; y++)
        {
            image.Frames[1][0, y] = new RgbaFloat(0f, 0f, 0f, 1f);
        }
        using var output = new MemoryStream();
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(output, new RadianceEncoder()));
    }

    [Theory]
    [InlineData(".hdr")]
    [InlineData(".pic")]
    [InlineData(".HDR")]
    public void FileExtensionsSelectTheEncoder(string extension)
    {
        var encoder = Assert.IsType<RadianceEncoder>(ImageEncoder.FromFileExtension(extension));
        Assert.Equal(RadianceCompression.RunLength, encoder.Compression);
        Assert.Null(encoder.BackgroundColor);
        Assert.IsType<RadianceEncoder>(ImageEncoder.Create(ImageFormat.Radiance));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RadianceEncoder { Compression = (RadianceCompression)2 });
    }

    [Fact]
    public void SaveToAPathUsesTheExtension()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-radiance-"));
        try
        {
            var path = directory / "image.hdr";
            using var image = Opaque(9, 1);
            image.Frames[0][3, 0] = new RgbaFloat(2f, 1f, 0f, 1f);
            image.Save(path);
            Assert.Equal(ImageFormat.Radiance, Image.Identify(path).Format);
            using var decoded = Image.Load<RgbaFloat>(path);
            // A mantissa 0 is read as the middle of its interval too
            Assert.Equal(new RgbaFloat(2.0078125f, 1.0078125f, 0.0078125f, 1f), decoded.Frames[0][3, 0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SequentialWritersWriteTheSameBytes()
    {
        using var image = Opaque(10, 4);
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                image.Frames[0][x, y] = new RgbaFloat(x, y, x * y, 1f);
            }
        }

        var expected = Save(image, new RadianceEncoder());
        using var output = new MemoryStream();
        using (var writer = Image.CreateWriter<RgbaFloat>(output, new ImageWriterOptions(image.Size) { Encoder = new RadianceEncoder(), Metadata = image.Metadata }))
        {
            writer.WriteFrame(image.Frames[0]);
            writer.Complete();
        }

        Assert.Equal(expected, output.ToArray());
    }

    private static Image<RgbaFloat> Opaque(int width, int height)
    {
        var image = new Image<RgbaFloat>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.Frames[0][x, y] = new RgbaFloat(0f, 0f, 0f, 1f);
            }
        }

        return image;
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var output = new MemoryStream();
        image.Save(output, encoder);
        return output.ToArray();
    }

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
