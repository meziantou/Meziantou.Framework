using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class TiffImageLoaderTests
{
    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagBitsPerSample = 258;
    private const ushort TagStripOffsets = 273;
    private const ushort TagRowsPerStrip = 278;
    private const ushort TagStripByteCounts = 279;
    private const ushort TagTileOffsets = 324;
    private const ushort TagTileByteCounts = 325;

    private const ushort PhotometricBlackIsZero = 1;
    private const ushort PhotometricRgb = 2;
    private const ushort CompressionNone = 1;
    private const ushort CompressionLzw = 5;

    [Theory]
    [InlineData("tiff-rgb24-none")]
    [InlineData("tiff-rgb24-packbits")]
    [InlineData("tiff-rgb24-lzw")]
    [InlineData("tiff-rgb24-lzw-predictor")]
    [InlineData("tiff-rgb24-big-endian")]
    [InlineData("tiff-gray8-white-is-zero")]
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
    public void Image_Load_IgnoresLzwDataThatDecodesPastTheEndOfTheStrip()
    {
        // 258 ("AB") only partly fits in the 3 pixels of the strip, and 'C' does not fit at all. libtiff keeps
        // what fits and ignores the rest.
        var stripData = ImageTestData.EncodeTiffLzwCodes([256, 'A', 'B', 258, 'C', 257]);
        var tiffData = CreateGrayscaleTiff(width: 3, height: 1, stripData, CompressionLzw);

        var image = Image.Load(tiffData);

        Assert.Equal(CreateGrayscalePixels("ABA"), image.Pixels.ToArray());
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

    [Fact]
    public void Image_Load_ThrowsWhenTiffHasSeveralPages()
    {
        var tiffData = CreateTwoPageGrayscaleTiff(firstPage: 10, secondPage: 20);

        Assert.Throws<NotSupportedException>(() => Image.Load(tiffData));
    }

    [Fact]
    public void ImageComparer_DetectsMultiPageTiffsThatDifferOnlyOnALaterPage()
    {
        var expected = new SnapshotData("tiff", CreateTwoPageGrayscaleTiff(firstPage: 10, secondPage: 20));
        var actual = new SnapshotData("tiff", CreateTwoPageGrayscaleTiff(firstPage: 10, secondPage: 30));

        Assert.False(ImageComparer.Instance.Equals(expected, actual));
        Assert.True(ImageComparer.Instance.Equals(expected, new SnapshotData("tiff", CreateTwoPageGrayscaleTiff(firstPage: 10, secondPage: 20))));
    }

    [Fact]
    public void Image_Load_ReadsTheDefaultRowsPerStripAsASingleStrip()
    {
        var tiffData = CreateGrayscaleTiff(width: 2, height: 2, [1, 2, 3, 4]);
        SetEntryValue(tiffData, TagRowsPerStrip, uint.MaxValue);

        var image = Image.Load(tiffData);

        Assert.Equal(CreateGrayscalePixels(""), image.Pixels.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Image_Load_DefaultsTheMissingBitsPerSampleToOneBit(int samplesPerPixel)
    {
        var tiffData = ImageTestData.CreateTiff(
            width: 8,
            height: 1,
            samplesPerPixel,
            samplesPerPixel is 1 ? PhotometricBlackIsZero : PhotometricRgb,
            CompressionNone,
            stripData: [0b1010_1010]);
        RemoveEntry(tiffData, TagBitsPerSample);

        var exception = Assert.Throws<NotSupportedException>(() => Image.Load(tiffData));
        Assert.Contains("8-bit", exception.Message);
    }

    [Fact]
    public void Image_Load_ThrowsNotSupportedForTiledTiff()
    {
        var tiffData = CreateGrayscaleTiff(width: 1, height: 1, [0]);
        RenameEntry(tiffData, TagStripOffsets, TagTileOffsets);
        RenameEntry(tiffData, TagStripByteCounts, TagTileByteCounts);

        var exception = Assert.Throws<NotSupportedException>(() => Image.Load(tiffData));
        Assert.Contains("Tiled", exception.Message);
    }

    [Theory]
    [InlineData(CompressionNone)]
    [InlineData(CompressionLzw)]
    public void Image_Load_ValidatesTheStripsBeforeAllocatingThePixels(ushort compression)
    {
        // 16000 x 16000 is within the pixel limit, but a strip of 2 bytes cannot hold it, uncompressed or not
        var tiffData = CreateGrayscaleTiff(width: 16000, height: 16000, [0, 0], compression);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => Image.Load(tiffData));
        allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBytes;

        Assert.True(allocatedBytes < 1024 * 1024, $"Allocated {allocatedBytes} bytes");
    }

    [Fact]
    public void Image_Load_ThrowsNotSupportedWhenTiffDimensionsExceedTheLimit()
    {
        var tiffData = CreateGrayscaleTiff(width: 1, height: 1, [0]);
        SetEntryValue(tiffData, TagImageWidth, 100_000);
        SetEntryValue(tiffData, TagImageLength, 100_000);

        Assert.Throws<NotSupportedException>(() => Image.Load(tiffData));
    }

    private static byte[] CreateGrayscaleTiff(int width, int height, byte[] stripData, ushort compression = CompressionNone)
    {
        return ImageTestData.CreateTiff(width, height, samplesPerPixel: 1, PhotometricBlackIsZero, compression, stripData);
    }

    /// <summary>
    /// Appends a copy of the image file directory of a 1x1 grayscale TIFF that points to its own pixel, and links
    /// it from the first directory.
    /// </summary>
    private static byte[] CreateTwoPageGrayscaleTiff(byte firstPage, byte secondPage)
    {
        var firstPageData = CreateGrayscaleTiff(width: 1, height: 1, [firstPage]);
        const int FirstIfdOffset = 8;
        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(firstPageData.AsSpan(FirstIfdOffset));
        var ifdLength = 2 + (entryCount * 12) + 4;

        // Directories start on a word boundary
        var secondIfdOffset = (firstPageData.Length + 1) & ~1;
        var secondPixelOffset = secondIfdOffset + ifdLength;
        var data = new byte[secondPixelOffset + 1];
        firstPageData.CopyTo(data, 0);
        firstPageData.AsSpan(FirstIfdOffset, ifdLength).CopyTo(data.AsSpan(secondIfdOffset));
        data[secondPixelOffset] = secondPage;

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(FirstIfdOffset + ifdLength - 4), (uint)secondIfdOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(GetEntryOffset(data, secondIfdOffset, TagStripOffsets) + 8), (uint)secondPixelOffset);
        return data;
    }

    private static void SetEntryValue(byte[] tiffData, ushort tag, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(tiffData.AsSpan(GetEntryOffset(tiffData, 8, tag) + 8), value);
    }

    private static void RemoveEntry(byte[] tiffData, ushort tag)
    {
        // A private tag number that the decoder does not know
        RenameEntry(tiffData, tag, 65000);
    }

    private static void RenameEntry(byte[] tiffData, ushort tag, ushort newTag)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(tiffData.AsSpan(GetEntryOffset(tiffData, 8, tag)), newTag);
    }

    private static int GetEntryOffset(byte[] tiffData, int ifdOffset, ushort tag)
    {
        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(tiffData.AsSpan(ifdOffset));
        for (var i = 0; i < entryCount; i++)
        {
            var entryOffset = ifdOffset + 2 + (i * 12);
            if (BinaryPrimitives.ReadUInt16LittleEndian(tiffData.AsSpan(entryOffset)) == tag)
                return entryOffset;
        }

        throw new ArgumentException($"The TIFF has no tag {tag}.", nameof(tag));
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
