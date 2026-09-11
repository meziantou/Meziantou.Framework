namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class TiffImageLoaderTests
{
    private const ushort PhotometricBlackIsZero = 1;
    private const ushort PhotometricRgb = 2;
    private const ushort CompressionNone = 1;
    private const ushort CompressionLzw = 5;

    [Theory]
    [InlineData("tiff-rgb24-none")]
    [InlineData("tiff-rgb24-packbits")]
    [InlineData("tiff-rgb24-lzw")]
    public async Task Image_LoadAsync_TiffAndConvertedPng_AreIdentical(string scenario)
    {
        var tiffImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".tiff");
        var pngImage = await ImageTestData.LoadImageFixtureAsync(scenario + ".from-tiff.png");

        Assert.Equal(tiffImage.Width, pngImage.Width);
        Assert.Equal(tiffImage.Height, pngImage.Height);
        Assert.Equal(tiffImage.Pixels.ToArray(), pngImage.Pixels.ToArray());
        Assert.Equal(tiffImage, pngImage);
    }

    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenTiffIsTruncated()
    {
        var data = ImageTestData.ReadImageFixture("tiff-rgb24-none.tiff");
        var truncatedData = data[..^8];

        await Assert.ThrowsAsync<InvalidDataException>(() => Image.LoadAsync(new MemoryStream(truncatedData)));
    }

    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenTiffIsBigTiff()
    {
        byte[] bigTiffHeader =
        [
            (byte)'I', (byte)'I',
            (byte)43, (byte)0,
            (byte)8, (byte)0,
            (byte)0, (byte)0,
        ];

        await Assert.ThrowsAsync<NotSupportedException>(() => Image.LoadAsync(new MemoryStream(bigTiffHeader)));
    }

    [Fact]
    public void Image_Load_AppendsTheFirstCharacterOfTheCodeThatIsNotInTheTableYet()
    {
        // 256 clears the table, then 'A', 'B' and 258 ("AB") build it up. 260 is not in the table yet: it
        // stands for the previous string followed by that string's own first character, so "AB" + "A".
        var stripData = ImageTestData.EncodeTiffLzwCodes([256, 'A', 'B', 258, 260, 257]);
        var tiffData = ImageTestData.CreateTiff(
            width: 7,
            height: 1,
            samplesPerPixel: 1,
            photometricInterpretation: PhotometricBlackIsZero,
            compression: CompressionLzw,
            stripData);

        var image = Image.Load(tiffData);

        Assert.Equal(CreateGrayscalePixels("ABABABA"), image.Pixels.ToArray());
    }

    [Fact]
    public void Image_Load_WidensTheLzwCodeOneEntryBeforeTheTableNeedsIt()
    {
        // The 254 literals after the clear code fill the table up to 511, which is where a TIFF writer
        // starts emitting 10-bit codes. A decoder that waits for 512 reads the two codes that follow at the
        // wrong width and decodes everything after them from the wrong bits.
        var codes = new List<int> { 256 };
        for (var value = 0; value <= 255; value++)
        {
            codes.Add(value);
        }

        var stripData = ImageTestData.EncodeTiffLzwCodes(codes);
        var tiffData = ImageTestData.CreateTiff(
            width: 256,
            height: 1,
            samplesPerPixel: 1,
            photometricInterpretation: PhotometricBlackIsZero,
            compression: CompressionLzw,
            stripData);

        var image = Image.Load(tiffData);

        var expectedPixels = new Argb[256];
        for (var value = 0; value < expectedPixels.Length; value++)
        {
            expectedPixels[value] = new Argb(0xFF, (byte)value, (byte)value, (byte)value);
        }

        Assert.Equal(expectedPixels, image.Pixels.ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData((ushort)0)]
    public void Image_Load_IgnoresTheFourthTiffSampleWhenItIsNotAnAlphaChannel(ushort? extraSample)
    {
        var tiffData = CreateRgbaTiff([10, 20, 30, 0], extraSample);

        var image = Image.Load(tiffData);

        Assert.Equal([new Argb(0xFF, 10, 20, 30)], image.Pixels.ToArray());
    }

    [Fact]
    public void Image_Load_UsesTheFourthTiffSampleAsAlphaWhenItIsUnassociated()
    {
        var tiffData = CreateRgbaTiff([10, 20, 30, 128], extraSample: 2);

        var image = Image.Load(tiffData);

        Assert.Equal([new Argb(128, 10, 20, 30)], image.Pixels.ToArray());
    }

    [Fact]
    public void Image_Load_UndoesThePremultiplicationOfAssociatedTiffAlpha()
    {
        var tiffData = CreateRgbaTiff([64, 128, 32, 128], extraSample: 1);

        var image = Image.Load(tiffData);

        Assert.Equal([new Argb(128, 128, 255, 64)], image.Pixels.ToArray());
    }

    private static byte[] CreateRgbaTiff(byte[] samples, ushort? extraSample)
    {
        return ImageTestData.CreateTiff(
            width: 1,
            height: 1,
            samplesPerPixel: 4,
            photometricInterpretation: PhotometricRgb,
            compression: CompressionNone,
            samples,
            extraSample);
    }

    private static Argb[] CreateGrayscalePixels(string values)
    {
        var pixels = new Argb[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            var value = (byte)values[i];
            pixels[i] = new Argb(0xFF, value, value, value);
        }

        return pixels;
    }
}
