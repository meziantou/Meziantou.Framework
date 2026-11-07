using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// TIFF and BigTIFF encoding: every working pixel format is written losslessly in both byte orders, both offset sizes
/// and every lossless compression (the floating-point ones as 32-bit floating-point samples), the bilevel and palette layouts store a page exactly or reject it, a JPEG page holds
/// what the JPEG encoder writes, multi-page documents keep the size, pixel format and metadata of every page, and the
/// container constraints (seekable destination, single page per <c>Image.Save</c>, no animation) are enforced before
/// anything is written.
/// </summary>
/// <remarks>
/// The outputs are read back by this library's own decoder, which the byte-for-byte decoder tests validate
/// independently, and the strips of the small cases are compared with the bytes the specifications give: the LZW and
/// PackBits examples, and CCITT code words spelled out from the tables of ITU-T T.4 and T.6. The interoperability tests
/// decode the same outputs with other libraries.
/// </remarks>
public sealed class TiffEncoderTests
{
    [Theory]
    [InlineData(TiffCompression.None, false, false)]
    [InlineData(TiffCompression.Deflate, false, false)]
    [InlineData(TiffCompression.None, true, false)]
    [InlineData(TiffCompression.Deflate, true, false)]
    [InlineData(TiffCompression.None, false, true)]
    [InlineData(TiffCompression.Deflate, true, true)]
    public void RoundTripsEveryVariantOfTheWrittenSubset(TiffCompression compression, bool bigEndian, bool bigTiff)
    {
        using var source = TestImages.CreateRgba(5, 4);
        var encoder = new TiffEncoder { Compression = compression, BigEndian = bigEndian, BigTiff = bigTiff };
        using var image = RoundTrip(source, encoder);
        Assert.Equal(ImageFormat.Tiff, image.Metadata.SourceFormat);
        Assert.Equal(source.Size, image.Size);
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        TestImages.AssertSamePixels(source, (Image<Rgba32>)image);
    }

    [Fact]
    public void EveryWorkingPixelFormatIsWrittenLosslessly()
    {
        using (var gray = Image.ImportPixelData<Gray8>([new Gray8(1), new Gray8(200), new Gray8(0), new Gray8(255)], 2, 2))
        using (var decoded = RoundTrip(gray, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
            TestImages.AssertSamePixels(gray, (Image<Gray8>)decoded);
        }

        using (var gray16 = Image.ImportPixelData<Gray16>([new Gray16(1), new Gray16(60000), new Gray16(0), new Gray16(65535)], 2, 2))
        using (var decoded = RoundTrip(gray16, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Gray16, decoded.PixelFormat);
            TestImages.AssertSamePixels(gray16, (Image<Gray16>)decoded);
        }

        using (var rgb = Image.ImportPixelData<Rgb24>([new Rgb24(1, 2, 3), new Rgb24(250, 251, 252)], 2, 1))
        using (var decoded = RoundTrip(rgb, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
            TestImages.AssertSamePixels(rgb, (Image<Rgb24>)decoded);
        }

        using (var rgba64 = Image.ImportPixelData<Rgba64>([new Rgba64(1, 2, 3, 4), new Rgba64(65535, 60000, 100, 7)], 2, 1))
        using (var decoded = RoundTrip(rgba64, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Rgba64, decoded.PixelFormat);
            TestImages.AssertSamePixels(rgba64, (Image<Rgba64>)decoded);
        }

        // Bgra32 stores the same information as Rgba32 and is written as RGBA, not reordered away
        using (var bgra = Image.ImportPixelData<Bgra32>([new Bgra32(1, 2, 3, 4), new Bgra32(9, 8, 7, 6)], 2, 1))
        using (var decoded = RoundTrip(bgra, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Rgba32, decoded.PixelFormat);
            using var expected = bgra.CloneAs<Rgba32>();
            TestImages.AssertSamePixels(expected, (Image<Rgba32>)decoded);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void TheStripHeightDoesNotChangeThePixels(int rowsPerStrip)
    {
        using var source = TestImages.CreateRgba(7, 11);
        using var decoded = RoundTrip(source, new TiffEncoder { RowsPerStrip = rowsPerStrip });
        TestImages.AssertSamePixels(source, (Image<Rgba32>)decoded);
    }

    [Fact]
    public void AMultiPageDocumentKeepsTheSizeAndPixelFormatOfEveryPage()
    {
        using var first = TestImages.CreateRgba(4, 3);
        using var second = Image.ImportPixelData<Gray8>([new Gray8(10), new Gray8(20)], 2, 1);
        using var third = Image.ImportPixelData<Rgb24>([new Rgb24(1, 2, 3)], 1, 1);

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(first);
        pages.Add(second);
        pages.Add(third);

        using var stream = new MemoryStream();
        pages.Save(stream, new TiffEncoder());
        stream.Position = 0;

        using var reloaded = ImageCollection.Load(stream);
        Assert.Equal(ImageCollectionKind.Pages, reloaded.Kind);
        Assert.Equal(3, reloaded.Count);
        Assert.Equal(new Size(4, 3), reloaded[0].Size);
        Assert.Equal(PixelFormat.Rgba32, reloaded[0].PixelFormat);
        Assert.Equal(new Size(2, 1), reloaded[1].Size);
        Assert.Equal(PixelFormat.Gray8, reloaded[1].PixelFormat);
        Assert.Equal(new Size(1, 1), reloaded[2].Size);
        Assert.Equal(PixelFormat.Rgb24, reloaded[2].PixelFormat);

        using var page0 = (Image<Rgba32>)reloaded[0].Decode();
        TestImages.AssertSamePixels(first, page0);
        using var page1 = reloaded[1].Decode<Gray8>();
        TestImages.AssertSamePixels(second, page1);
    }

    [Fact]
    public void ALoadedDocumentCanBeReSavedWithoutMaterializingEveryPage()
    {
        using var first = TestImages.CreateRgba(4, 3);
        using var second = Image.ImportPixelData<Gray8>([new Gray8(10), new Gray8(20)], 2, 1);
        using var original = new MemoryStream();
        using (var pages = ImageCollection.Create(ImageCollectionKind.Pages))
        {
            pages.Add(first);
            pages.Add(second);
            pages.Save(original, new TiffEncoder());
        }

        original.Position = 0;
        using var copy = new MemoryStream();
        using (var loaded = ImageCollection.Load(original))
        {
            loaded.Save(copy, new TiffEncoder { Compression = TiffCompression.None });
        }

        copy.Position = 0;
        using var reloaded = ImageCollection.Load(copy);
        Assert.Equal(2, reloaded.Count);
        using var page0 = (Image<Rgba32>)reloaded[0].Decode();
        TestImages.AssertSamePixels(first, page0);
    }

    [Fact]
    public void PagesCanBeReorderedAndRemovedBeforeSaving()
    {
        using var a = Image.ImportPixelData<Gray8>([new Gray8(1)], 1, 1);
        using var b = Image.ImportPixelData<Gray8>([new Gray8(2)], 1, 1);
        using var c = Image.ImportPixelData<Gray8>([new Gray8(3)], 1, 1);

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(a);
        pages.Add(b);
        pages.Add(c);
        pages.Move(2, 0);
        pages.RemoveAt(2);

        using var stream = new MemoryStream();
        pages.Save(stream, new TiffEncoder());
        stream.Position = 0;

        using var reloaded = ImageCollection.Load(stream);
        Assert.Equal(2, reloaded.Count);
        using var page0 = reloaded[0].Decode<Gray8>();
        using var page1 = reloaded[1].Decode<Gray8>();
        Assert.Equal(new Gray8(3), Read(page0));
        Assert.Equal(new Gray8(1), Read(page1));

        static Gray8 Read(Image<Gray8> image)
        {
            var pixels = new Gray8[1];
            image.Frames[0].CopyPixelDataTo(pixels);
            return pixels[0];
        }
    }

    [Fact]
    public void ResolutionAndOrientationRoundTripAndUnstorableMetadataIsRejected()
    {
        using var source = TestImages.CreateRgba(2, 2);
        source.Metadata.Resolution = new ImageResolution(300, 150);
        source.Metadata.Orientation = ExifOrientation.BottomRight;

        using var decoded = RoundTrip(source, new TiffEncoder());
        Assert.Equal(300, decoded.Metadata.Resolution!.HorizontalDpi);
        Assert.Equal(150, decoded.Metadata.Resolution.VerticalDpi);
        Assert.Equal(ExifOrientation.BottomRight, decoded.Metadata.Orientation);

        // A TIFF stores no EXIF block in this version: the strict policy rejects it instead of dropping it
        using var withExif = TestImages.CreateRgba(2, 2);
        withExif.Metadata.ExifProfile = new ExifProfile(new MetadataBlob(Internals.ExifTiff.CreateMinimal(ExifOrientation.TopLeft)));
        using var stream = new MemoryStream();
        Assert.Throws<UnsupportedImageFeatureException>(() => withExif.Save(stream, new TiffEncoder()));
        Assert.Equal(0, stream.Length);

        using var discarding = new MemoryStream();
        withExif.Save(discarding, new TiffEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.True(discarding.Length > 0);
    }

    [Fact]
    public void ANonSeekableDestinationIsRejectedBeforeAnyByteIsWritten()
    {
        using var source = TestImages.CreateRgba(2, 2);
        using var stream = new TestOutputStream();
        Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder()));
        Assert.Equal(0, stream.BytesWritten);

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(source);
        using var collectionStream = new TestOutputStream();
        Assert.Throws<ArgumentException>(() => pages.Save(collectionStream, new TiffEncoder()));
        Assert.Equal(0, collectionStream.BytesWritten);
    }

    [Fact]
    public void TheCollectionKindMustMatchTheOutputFormat()
    {
        using var source = TestImages.CreateRgba(2, 2);
        using var representations = ImageCollection.Create(ImageCollectionKind.Representations);
        representations.Add(source);
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentException>(() => representations.Save(stream, new TiffEncoder()));

        using var empty = ImageCollection.Create(ImageCollectionKind.Pages);
        Assert.Throws<InvalidOperationException>(() => empty.Save(stream, new TiffEncoder()));

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(source);
        Assert.Throws<ArgumentException>(() => pages.Save(stream, new PngEncoder()));
    }

    [Fact]
    public void AnAnimatedImageIsRejectedBecausePagesAreNotFrames()
    {
        using var animated = TestImages.CreateRgba(2, 2);
        animated.AppendFrame();
        using var stream = new MemoryStream();
        Assert.Throws<UnsupportedImageFeatureException>(() => animated.Save(stream, new TiffEncoder()));

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        Assert.Throws<ArgumentException>(() => pages.Add(animated));
    }

    [Fact]
    public void TheExtensionSelectsTheTiffEncoder()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("tiff-encoder-tests"));
        try
        {
            var path = directory / "page.tiff";
            using var source = TestImages.CreateRgba(3, 2);
            source.Save(path);
            Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(File.ReadAllBytes(path)));
            using var reloaded = (Image<Rgba32>)Image.Load(path);
            TestImages.AssertSamePixels(source, reloaded);

            var document = directory / "document.tif";
            using (var pages = ImageCollection.Create(ImageCollectionKind.Pages))
            {
                pages.Add(source);
                pages.Add(source);
                pages.Save(document);
            }

            using var collection = ImageCollection.Load(document);
            Assert.Equal(2, collection.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static TheoryData<TiffCompression, bool, bool> LosslessCompressionCases()
    {
        var data = new TheoryData<TiffCompression, bool, bool>();
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Deflate, TiffCompression.Lzw, TiffCompression.PackBits })
        {
            data.Add(compression, false, false);
            data.Add(compression, true, false);
            data.Add(compression, false, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LosslessCompressionCases))]
    public void EveryLosslessCompressionStoresEveryPixelFormatExactly(TiffCompression compression, bool bigEndian, bool bigTiff)
    {
        // 150 rows of 211 pixels: several strips, with runs and with noise, so that the LZW table fills up and is cleared
        var encoder = new TiffEncoder { Compression = compression, BigEndian = bigEndian, BigTiff = bigTiff };
        using var rgba = CreatePattern(211, 150);
        using (var decoded = RoundTrip(rgba, encoder))
        {
            TestImages.AssertSamePixels(rgba, (Image<Rgba32>)decoded);
        }

        var opaque = new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) };
        using (var rgb = rgba.CloneAs<Rgb24>(opaque))
        using (var decoded = RoundTrip(rgb, encoder))
        {
            TestImages.AssertSamePixels(rgb, (Image<Rgb24>)decoded);
        }

        using (var gray = rgba.CloneAs<Gray8>(opaque))
        using (var decoded = RoundTrip(gray, encoder))
        {
            TestImages.AssertSamePixels(gray, (Image<Gray8>)decoded);
        }

        using (var rgba64 = rgba.CloneAs<Rgba64>())
        using (var decoded = RoundTrip(rgba64, encoder))
        {
            TestImages.AssertSamePixels(rgba64, (Image<Rgba64>)decoded);
        }

        using (var wide = rgba.CloneAs<Rgba64>())
        using (var gray16 = wide.CloneAs<Gray16>(opaque))
        using (var decoded = RoundTrip(gray16, encoder))
        {
            TestImages.AssertSamePixels(gray16, (Image<Gray16>)decoded);
        }
    }

    [Theory]
    [InlineData(TiffCompression.None, 1)]
    [InlineData(TiffCompression.Deflate, 8)]
    [InlineData(TiffCompression.Lzw, 5)]
    [InlineData(TiffCompression.PackBits, 32773)]
    public void TheDirectoryDeclaresTheCompressionOfTheStrips(TiffCompression compression, int expected)
    {
        using var source = CreatePattern(40, 30);
        var tags = ReadTags(Save(source, new TiffEncoder { Compression = compression, RowsPerStrip = 7 }));
        Assert.Equal([(ulong)expected], tags[259]);
        Assert.Equal([2UL], tags[262]);
        Assert.Equal([8UL, 8, 8, 8], tags[258]);
        Assert.Equal([7UL], tags[278]);
        Assert.HasCount(5, tags[273]);
        Assert.DoesNotContain((ushort)317, tags.Keys);
    }

    [Fact]
    public void LzwAndPackBitsStripsAreTheExamplesOfTheirSpecifications()
    {
        // TIFF 6.0, section 13: the strip 7 7 7 8 8 7 7 6 6 is the codes 256 7 258 8 8 258 6 6 257, nine bits each
        using (var image = Image.ImportPixelData<Gray8>([.. new byte[] { 7, 7, 7, 8, 8, 7, 7, 6, 6 }.Select(static value => new Gray8(value))], 9, 1))
        {
            var expected = TiffReferenceCodecs.PackBitString(string.Concat(new[] { 256, 7, 258, 8, 8, 258, 6, 6, 257 }.Select(static code => Convert.ToString(code, 2).PadLeft(9, '0'))));
            Assert.Equal(expected, ReadStrip(Save(image, new TiffEncoder { Compression = TiffCompression.Lzw })));
        }

        // Apple Technical Note TN1023: runs of three bytes and more are repeated runs, the rest are literal runs
        byte[] unpacked = [0xAA, 0xAA, 0xAA, 0x80, 0x00, 0x2A, 0xAA, 0xAA, 0xAA, 0xAA, 0x80, 0x00, 0x2A, 0x22, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA];
        using (var image = Image.ImportPixelData<Gray8>([.. unpacked.Select(static value => new Gray8(value))], 24, 1))
        {
            Assert.Equal([0xFE, 0xAA, 0x02, 0x80, 0x00, 0x2A, 0xFD, 0xAA, 0x03, 0x80, 0x00, 0x2A, 0x22, 0xF7, 0xAA], ReadStrip(Save(image, new TiffEncoder { Compression = TiffCompression.PackBits })));
        }

        // PackBits packs each row on its own: a run does not continue on the next row, and a run is at most 128 bytes
        using (var image = Image.ImportPixelData<Gray8>([.. Enumerable.Repeat(new Gray8(9), 300 * 2)], 300, 2))
        {
            Assert.Equal([0x81, 9, 0x81, 9, 0xD5, 9, 0x81, 9, 0x81, 9, 0xD5, 9], ReadStrip(Save(image, new TiffEncoder { Compression = TiffCompression.PackBits })));
        }
    }

    [Fact]
    public void LzwStripsOfEveryLengthAroundACodeSizeChangeRoundTrip()
    {
        // One code per byte (no pair of bytes repeats): the strips end just before, at and after each change of code size
        // and the clearing of the table
        var sequence = TiffCodecTests.SequenceWithoutRepeatedPair(4200);
        foreach (var boundary in new[] { 254, 766, 1790, 3836 })
        {
            for (var length = boundary - 4; length <= boundary + 4; length++)
            {
                using var image = Image.ImportPixelData<Gray8>([.. sequence.Take(length).Select(static value => new Gray8(value))], length, 1);
                using var decoded = RoundTrip(image, new TiffEncoder { Compression = TiffCompression.Lzw });
                TestImages.AssertSamePixels(image, (Image<Gray8>)decoded);
            }
        }
    }

    [Theory]
    [InlineData(TiffCompression.None, 1)]
    [InlineData(TiffCompression.Deflate, 8)]
    [InlineData(TiffCompression.Lzw, 5)]
    [InlineData(TiffCompression.PackBits, 32773)]
    [InlineData(TiffCompression.CcittModifiedHuffman, 2)]
    [InlineData(TiffCompression.CcittGroup3, 3)]
    [InlineData(TiffCompression.CcittGroup4, 4)]
    public void TheBilevelLayoutStoresBlackAndWhitePixelsOfAnyPixelFormat(TiffCompression compression, int expectedCompression)
    {
        // 300 rows of 437 pixels with runs of every length up to the width, in several strips
        const int Width = 437;
        const int Height = 300;
        var black = new bool[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                black[(y * Width) + x] = y % 10 == 0 ? x >= y : y % 10 == 1 ? x < y : ((x * 7) + (y * 13)) % (2 + (y % 23)) < 1 + (y % 5);
            }
        }

        var encoder = new TiffEncoder { Compression = compression, PixelLayout = TiffPixelLayout.Bilevel, RowsPerStrip = 64 };
        var expected = black.Select(static value => new Gray8(value ? (byte)0 : (byte)255)).ToArray();
        using var gray = Image.ImportPixelData<Gray8>(expected, Width, Height);
        var file = Save(gray, encoder);
        var tags = ReadTags(file);
        Assert.Equal([(ulong)expectedCompression], tags[259]);
        Assert.Equal([1UL], tags[258]);
        Assert.Equal([1UL], tags[277]);
        Assert.Equal([0UL], tags[262]);

        var info = Image.Identify(file);
        Assert.Equal(1, info.BitsPerComponent);
        Assert.Equal(PixelFormat.Gray8, info.PixelFormat);
        using (var decoded = Image.Load<Gray8>(file))
        {
            TestImages.AssertSamePixels(gray, decoded);
        }

        // The same pixels in the other pixel formats give the same file
        using (var rgb = gray.CloneAs<Rgb24>())
        {
            Assert.Equal(file, Save(rgb, encoder));
        }

        using (var bgra = gray.CloneAs<Bgra32>())
        {
            Assert.Equal(file, Save(bgra, encoder));
        }

        using (var rgba64 = gray.CloneAs<Rgba64>())
        {
            Assert.Equal(file, Save(rgba64, encoder));
        }

        using (var gray16 = gray.CloneAs<Gray16>())
        {
            Assert.Equal(file, Save(gray16, encoder));
        }
    }

    [Fact]
    public void FaxStripsAreTheCodesOfTheRecommendations()
    {
        // The rows and the code words of the decoder tests, spelled out from the tables of ITU-T T.4 and T.6
        var pixels = TiffCodecTests.FaxRows.SelectMany(static row => row.Select(static pixel => new Gray8(pixel == '#' ? (byte)0 : (byte)255))).ToArray();
        using var image = Image.ImportPixelData<Gray8>(pixels, 8, TiffCodecTests.FaxRows.Length);

        // Modified Huffman: each row starts on a byte boundary
        byte[] huffman = [.. TiffCodecTests.FaxRowsInOneDimension.SelectMany(TiffReferenceCodecs.PackBitString)];
        Assert.Equal(huffman, ReadStrip(Save(image, new TiffEncoder { Compression = TiffCompression.CcittModifiedHuffman, PixelLayout = TiffPixelLayout.Bilevel })));

        // Group 3: an end-of-line code before each row, no fill bit, no return-to-control sequence
        var groupThree = TiffReferenceCodecs.PackBitString(string.Concat(TiffCodecTests.FaxRowsInOneDimension.Select(static row => TiffCodecTests.FaxEndOfLine + row)));
        Assert.Equal(groupThree, ReadStrip(Save(image, new TiffEncoder { Compression = TiffCompression.CcittGroup3, PixelLayout = TiffPixelLayout.Bilevel })));

        // Group 4: every row relative to the row above, then the end-of-facsimile-block sequence
        var groupFour = TiffReferenceCodecs.PackBitString(string.Concat(TiffCodecTests.FaxRowsInTwoDimensions) + TiffCodecTests.FaxEndOfLine + TiffCodecTests.FaxEndOfLine);
        Assert.Equal(groupFour, ReadStrip(Save(image, new TiffEncoder { Compression = TiffCompression.CcittGroup4, PixelLayout = TiffPixelLayout.Bilevel })));

        // Runs longer than the longest make-up code repeat it: 5,200 white pixels, then 5,200 black ones, then 2,623
        // white and 2,577 black
        const string Run2560 = "000000011111";
        var wide = new Gray8[5200 * 3];
        for (var x = 0; x < 5200; x++)
        {
            wide[x] = new Gray8(255);
            wide[10400 + x] = new Gray8(x < 2623 ? (byte)255 : (byte)0);
        }

        using var wideImage = Image.ImportPixelData<Gray8>(wide, 5200, 3);
        byte[] expected =
        [
            .. TiffReferenceCodecs.PackBitString(Run2560 + Run2560 + "11011" + "101010"),
            .. TiffReferenceCodecs.PackBitString("00110101" + Run2560 + Run2560 + "0000001111" + "0000010111"),
            .. TiffReferenceCodecs.PackBitString(Run2560 + "00110100" + Run2560 + "0000011000"),
        ];
        Assert.Equal(expected, ReadStrip(Save(wideImage, new TiffEncoder { Compression = TiffCompression.CcittModifiedHuffman, PixelLayout = TiffPixelLayout.Bilevel })));
    }

    [Fact]
    public void TheBilevelLayoutNeverThresholdsNorDropsAlpha()
    {
        var encoder = new TiffEncoder { Compression = TiffCompression.CcittGroup4, PixelLayout = TiffPixelLayout.Bilevel };
        using var stream = new MemoryStream();

        // A gray level, a color, a 16-bit value that is only nearly white
        using (var gray = Image.ImportPixelData<Gray8>([new Gray8(0), new Gray8(255), new Gray8(254)], 3, 1))
        {
            Assert.Equal("Bilevel reduction", Assert.Throws<UnsupportedImageFeatureException>(() => gray.Save(stream, encoder)).Feature);
        }

        using (var color = Image.ImportPixelData<Rgb24>([new Rgb24(255, 255, 255), new Rgb24(255, 0, 0)], 2, 1))
        {
            Assert.Equal("Bilevel reduction", Assert.Throws<UnsupportedImageFeatureException>(() => color.Save(stream, encoder)).Feature);
        }

        using (var deep = Image.ImportPixelData<Gray16>([new Gray16(65535), new Gray16(65534)], 2, 1))
        {
            Assert.Equal("Bilevel reduction", Assert.Throws<UnsupportedImageFeatureException>(() => deep.Save(stream, encoder)).Feature);
        }

        // A black pixel that is not opaque
        using (var transparent = Image.ImportPixelData<Rgba32>([new Rgba32(0, 0, 0, 255), new Rgba32(0, 0, 0, 254)], 2, 1))
        {
            Assert.Equal("Alpha removal", Assert.Throws<UnsupportedImageFeatureException>(() => transparent.Save(stream, encoder)).Feature);
        }

        // The page is rejected before anything is written
        Assert.Equal(0, stream.Length);

        // Opaque black and white pixels of a pixel format with alpha are stored
        using var opaque = Image.ImportPixelData<Rgba64>([new Rgba64(0, 0, 0, 65535), new Rgba64(65535, 65535, 65535, 65535)], 2, 1);
        using var decoded = RoundTrip(opaque, encoder);
        Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
        using var expected = Image.ImportPixelData<Gray8>([new Gray8(0), new Gray8(255)], 2, 1);
        TestImages.AssertSamePixels(expected, (Image<Gray8>)decoded);
    }

    [Theory]
    [InlineData(TiffCompression.None)]
    [InlineData(TiffCompression.Deflate)]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.PackBits)]
    public void TheIndexedLayoutStoresTheColorsOfThePageExactly(TiffCompression compression)
    {
        // 256 colors: the most a color map holds
        var pixels = new Rgb24[64 * 40];
        for (var i = 0; i < pixels.Length; i++)
        {
            var color = (i * 37) % 256;
            pixels[i] = new Rgb24((byte)color, (byte)(color * 3), (byte)(255 - color));
        }

        var encoder = new TiffEncoder { Compression = compression, PixelLayout = TiffPixelLayout.Indexed, RowsPerStrip = 9 };
        using var source = Image.ImportPixelData<Rgb24>(pixels, 64, 40);
        var file = Save(source, encoder);
        var tags = ReadTags(file);
        Assert.Equal([3UL], tags[262]);
        Assert.Equal([8UL], tags[258]);
        Assert.Equal([1UL], tags[277]);
        Assert.HasCount(768, tags[320]);

        // The color map is sorted, and each 8-bit sample c is stored as 257 c
        for (var i = 1; i < 256; i++)
        {
            var previous = (tags[320][i - 1], tags[320][256 + i - 1], tags[320][512 + i - 1]);
            var current = (tags[320][i], tags[320][256 + i], tags[320][512 + i]);
            Assert.True(previous.CompareTo(current) < 0);
            Assert.Equal(0UL, tags[320][i] % 257);
        }

        var info = Image.Identify(file);
        Assert.Equal(ImageColorModel.Indexed, info.ColorModel);
        using (var decoded = Image.Load(file))
        {
            Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
            TestImages.AssertSamePixels(source, (Image<Rgb24>)decoded);
        }

        // The same colors in another pixel format give the same file
        using (var rgba = source.CloneAs<Rgba32>())
        {
            Assert.Equal(file, Save(rgba, encoder));
        }
    }

    [Fact]
    public void TheIndexedLayoutNeverReducesColorsNorDropsAlpha()
    {
        var encoder = new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed };
        using var stream = new MemoryStream();

        // 257 colors
        using (var colors = Image.ImportPixelData<Rgb24>([.. Enumerable.Range(0, 257).Select(static i => new Rgb24((byte)i, (byte)(i >> 8), 0))], 257, 1))
        {
            Assert.Equal("Color reduction", Assert.Throws<UnsupportedImageFeatureException>(() => colors.Save(stream, encoder)).Feature);
        }

        using (var transparent = Image.ImportPixelData<Rgba32>([new Rgba32(1, 2, 3, 0)], 1, 1))
        {
            Assert.Equal("Alpha removal", Assert.Throws<UnsupportedImageFeatureException>(() => transparent.Save(stream, encoder)).Feature);
        }

        Assert.Equal(0, stream.Length);

        // Gray pixels are colors whose three samples are equal
        using (var gray = Image.ImportPixelData<Gray8>([new Gray8(3), new Gray8(200), new Gray8(3)], 3, 1))
        using (var expected = Image.ImportPixelData<Rgb24>([new Rgb24(3, 3, 3), new Rgb24(200, 200, 200), new Rgb24(3, 3, 3)], 3, 1))
        using (var decoded = RoundTrip(gray, encoder))
        {
            TestImages.AssertSamePixels(expected, (Image<Rgb24>)decoded);
        }

        // The colors of a 16-bit pixel format keep their 16 bits: the page is read back with them
        using (var deep = Image.ImportPixelData<Rgba64>([new Rgba64(1000, 2000, 3000, 65535), new Rgba64(65535, 0, 1, 65535), new Rgba64(1000, 2000, 3000, 65535)], 3, 1))
        using (var decoded = RoundTrip(deep, encoder))
        using (var wide = decoded.CloneAs<Rgba64>())
        {
            Assert.Equal(PixelFormat.Rgb48, decoded.PixelFormat);
            TestImages.AssertSamePixels(deep, wide);
        }

        // 16-bit colors that 8 bits store exactly are an 8-bit color map
        using (var exact = Image.ImportPixelData<Rgba64>([new Rgba64(257, 514, 65535, 65535)], 1, 1))
        using (var expected = Image.ImportPixelData<Rgb24>([new Rgb24(1, 2, 255)], 1, 1))
        using (var decoded = RoundTrip(exact, encoder))
        {
            TestImages.AssertSamePixels(expected, (Image<Rgb24>)decoded);
        }

        // Readers take a color map whose entries are all multiples of 256 for 8-bit colors written by the software that
        // stores a color c as 256 c: 16-bit colors that look like that would not be read back as they are
        using (var ambiguous = Image.ImportPixelData<Gray16>([new Gray16(256), new Gray16(512)], 2, 1))
        {
            Assert.Equal("Color map precision", Assert.Throws<UnsupportedImageFeatureException>(() => ambiguous.Save(stream, encoder)).Feature);
        }

        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(JpegChromaSubsampling.Ratio444, 1, 1, 8)]
    [InlineData(JpegChromaSubsampling.Ratio422, 2, 1, 8)]
    [InlineData(JpegChromaSubsampling.Ratio420, 2, 2, 16)]
    [InlineData(JpegChromaSubsampling.Auto, 2, 2, 16)]
    public void AJpegPageIsOneStripThatHoldsAJpegFile(JpegChromaSubsampling subsampling, int horizontal, int vertical, int unitRows)
    {
        using var rgba = CreatePattern(45, 37);
        using var source = rgba.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        var file = Save(source, new TiffEncoder { Compression = TiffCompression.Jpeg, JpegQuality = 80, JpegChromaSubsampling = subsampling, RowsPerStrip = 4 });
        var tags = ReadTags(file);
        Assert.Equal([7UL], tags[259]);
        Assert.Equal([6UL], tags[262]);
        Assert.Equal([8UL, 8, 8], tags[258]);
        Assert.Equal([(ulong)horizontal, (ulong)vertical], tags[530]);
        Assert.Equal([0UL, 1, 255, 1, 128, 1, 255, 1, 128, 1, 255, 1], tags[532]);
        Assert.HasCount(1, tags[273]);

        // One strip of whole minimum coded units: the smallest multiple of their height that holds the page
        Assert.Equal([(ulong)((37 + unitRows - 1) / unitRows * unitRows)], tags[278]);

        // The strip is what the JPEG encoder writes with the same settings and no metadata
        var jpeg = Save(source, new JpegEncoder { Quality = 80, ChromaSubsampling = subsampling, MetadataHandling = MetadataHandling.Strip });
        Assert.Equal(jpeg, ReadStrip(file));
        using var expected = Image.Load<Rgb24>(jpeg);
        using var decoded = Image.Load(file);
        Assert.Equal(ImageFormat.Tiff, decoded.Metadata.SourceFormat);
        TestImages.AssertSamePixels(expected, (Image<Rgb24>)decoded);
    }

    [Fact]
    public void JpegCompressionStoresGrayPagesAndRejectsWhatJpegCannotStore()
    {
        var encoder = new TiffEncoder { Compression = TiffCompression.Jpeg };
        using var rgba = CreatePattern(20, 18);

        // A gray pixel format is one component
        using (var gray = rgba.CloneAs<Gray8>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) }))
        {
            var file = Save(gray, encoder);
            var tags = ReadTags(file);
            Assert.Equal([1UL], tags[262]);
            Assert.Equal([8UL], tags[258]);
            Assert.DoesNotContain((ushort)530, tags.Keys);
            Assert.Equal(Save(gray, new JpegEncoder { MetadataHandling = MetadataHandling.Strip }), ReadStrip(file));
            using var expected = Image.Load<Gray8>(ReadStrip(file));
            using var decoded = Image.Load<Gray8>(file);
            TestImages.AssertSamePixels(expected, decoded);
        }

        // Opaque pixels of a pixel format with alpha are stored; a pixel that is not opaque is not, nor are 16-bit samples
        using (var rgb = rgba.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) }))
        using (var opaque = rgb.CloneAs<Bgra32>())
        using (var decoded = RoundTrip(opaque, encoder))
        {
            Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
            Assert.Equal(Save(rgb, encoder), Save(opaque, encoder));
        }

        using var stream = new MemoryStream();
        Assert.Equal("Alpha removal", Assert.Throws<UnsupportedImageFeatureException>(() => rgba.Save(stream, encoder)).Feature);
        using (var deep = rgba.CloneAs<Rgba64>())
        {
            Assert.Equal("Bit depth reduction", Assert.Throws<UnsupportedImageFeatureException>(() => deep.Save(stream, encoder)).Feature);
        }

        // A JPEG datastream stores at most 65,535 pixels per side
        var largeCanvas = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 65536 } };
        using (var wide = Image.ImportPixelData<Gray8>(new Gray8[65536], 65536, 1, configuration: largeCanvas))
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => wide.Save(stream, encoder));
            Assert.Equal("Canvas size", exception.Feature);
            Assert.Equal(ImageFormat.Tiff, exception.Format);
        }

        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void ACompressionThatCannotStoreThePixelLayoutIsAnInvalidSetting()
    {
        using var source = Image.ImportPixelData<Gray8>([new Gray8(0), new Gray8(255)], 2, 1);
        using var stream = new MemoryStream();
        foreach (var compression in new[] { TiffCompression.CcittModifiedHuffman, TiffCompression.CcittGroup3, TiffCompression.CcittGroup4 })
        {
            foreach (var layout in new[] { TiffPixelLayout.Auto, TiffPixelLayout.Indexed })
            {
                Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder { Compression = compression, PixelLayout = layout }));
            }
        }

        foreach (var layout in new[] { TiffPixelLayout.Bilevel, TiffPixelLayout.Indexed })
        {
            Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder { Compression = TiffCompression.Jpeg, PixelLayout = layout }));
        }

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(source);
        Assert.Throws<ArgumentException>(() => pages.Save(stream, new TiffEncoder { Compression = TiffCompression.CcittGroup4 }));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void ThePredictorAndTheTilesNeedSettingsThatCanStoreThem()
    {
        using var source = Image.ImportPixelData<Gray8>([new Gray8(0), new Gray8(255)], 2, 1);
        using var stream = new MemoryStream();

        // Readers apply a predictor to Deflate and LZW data, and horizontal differencing predicts samples of 8 and 16 bits
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.PackBits, TiffCompression.Jpeg })
        {
            Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder { Compression = compression, Predictor = TiffPredictor.HorizontalDifferencing }));
        }

        Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder { Compression = TiffCompression.CcittGroup4, PixelLayout = TiffPixelLayout.Bilevel, Predictor = TiffPredictor.HorizontalDifferencing }));
        foreach (var layout in new[] { TiffPixelLayout.Bilevel, TiffPixelLayout.Indexed })
        {
            Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder { PixelLayout = layout, Predictor = TiffPredictor.HorizontalDifferencing }));
        }

        // A tile has both sizes, and a JPEG page is one strip
        Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder { TileWidth = 16 }));
        Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder { TileHeight = 16 }));
        Assert.Throws<ArgumentException>(() => source.Save(stream, new TiffEncoder { Compression = TiffCompression.Jpeg, TileWidth = 16, TileHeight = 16 }));

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(source);
        Assert.Throws<ArgumentException>(() => pages.Save(stream, new TiffEncoder { Compression = TiffCompression.PackBits, Predictor = TiffPredictor.HorizontalDifferencing }));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void HorizontalDifferencingStoresTheDifferencesOfTheSpecification()
    {
        // TIFF 6.0, section 14: in each row, every sample but the first of its channel is replaced with its difference from
        // the sample of the same channel on its left, modulo 2^8 or 2^16
        using (var gray = Image.ImportPixelData<Gray8>([.. new byte[] { 10, 12, 9, 200, 100, 0 }.Select(value => new Gray8(value))], 3, 2))
        {
            var file = Save(gray, new TiffEncoder { Predictor = TiffPredictor.HorizontalDifferencing });
            Assert.Equal([2UL], ReadTags(file)[317]);
            Assert.Equal([10, 2, 253, 200, 156, 156], Inflate(ReadStrip(file)));
        }

        using (var rgb = Image.ImportPixelData<Rgb24>([new Rgb24(1, 2, 3), new Rgb24(5, 1, 250)], 2, 1))
        {
            var file = Save(rgb, new TiffEncoder { Compression = TiffCompression.Lzw, Predictor = TiffPredictor.HorizontalDifferencing });
            Assert.Equal([2UL], ReadTags(file)[317]);
            Assert.Equal(TiffReferenceCodecs.EncodeLzw([1, 2, 3, 4, 255, 247]), ReadStrip(file));
        }

        using (var deep = Image.ImportPixelData<La32>([new La32(1000, 65535), new La32(999, 0), new La32(65535, 1)], 3, 1))
        {
            var differences = new byte[12];
            ushort[] expected = [1000, 65535, 65535, 1, 64536, 1];
            for (var i = 0; i < expected.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(differences.AsSpan(i * 2), expected[i]);
            }

            Assert.Equal(differences, Inflate(ReadStrip(Save(deep, new TiffEncoder { Predictor = TiffPredictor.HorizontalDifferencing }))));
        }

        // Floating-point samples keep the floating-point predictor, and the file it always had
        using var floats = CreateFloatingPointPattern(5, 3, gray: false, opaque: false);
        var predicted = Save(floats, new TiffEncoder { Predictor = TiffPredictor.HorizontalDifferencing });
        Assert.Equal([3UL], ReadTags(predicted)[317]);
        Assert.Equal(Save(floats, new TiffEncoder()), predicted);
    }

    [Theory]
    [InlineData(TiffCompression.Deflate, false, false)]
    [InlineData(TiffCompression.Lzw, false, false)]
    [InlineData(TiffCompression.Deflate, true, false)]
    [InlineData(TiffCompression.Lzw, true, true)]
    public void HorizontalDifferencingStoresEveryIntegerPixelFormatExactly(TiffCompression compression, bool bigEndian, bool bigTiff)
    {
        // Strips of 5 rows, whose rows each start their own differences
        var encoder = new TiffEncoder { Compression = compression, Predictor = TiffPredictor.HorizontalDifferencing, BigEndian = bigEndian, BigTiff = bigTiff, RowsPerStrip = 5 };
        var opaque = new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) };
        using var rgba = CreatePattern(37, 23);
        using var rgba64 = CreateWorkerImage("rgba64", ImageConfiguration.Default);
        Func<Image>[] images =
        [
            rgba.Clone, rgba64.Clone, () => rgba.CloneAs<Rgb24>(opaque), () => rgba.CloneAs<Gray8>(opaque), () => rgba.CloneAs<La16>(), () => rgba.CloneAs<Bgra32>(),
            () => rgba64.CloneAs<Rgb48>(opaque), () => rgba64.CloneAs<Gray16>(opaque), () => rgba64.CloneAs<La32>(),
        ];

        foreach (var create in images)
        {
            using var image = create();
            using var decoded = RoundTrip(image, encoder);
            using var expected = image.PixelFormat == PixelFormat.Bgra32 ? image.CloneAs<Rgba32>() : image.Clone();
            Assert.Equal(expected.PixelFormat, decoded.PixelFormat);
            Assert.Equal(GetPixelBytes(expected), GetPixelBytes(decoded));
        }
    }

    [Fact]
    public void TiledPagesHaveTheTilesOfTheSpecification()
    {
        // 37x23 pixels in 16x16 tiles: three tiles across and two down, stored in full, the ones of the right and bottom
        // edges padded with zero samples
        using var source = CreatePattern(37, 23);
        var file = Save(source, new TiffEncoder { Compression = TiffCompression.None, TileWidth = 16, TileHeight = 16, RowsPerStrip = 5 });
        var tags = ReadTags(file);
        Assert.Equal([16UL], tags[322]);
        Assert.Equal([16UL], tags[323]);
        Assert.HasCount(6, tags[324]);
        Assert.Equal(Enumerable.Repeat(16UL * 16 * 4, 6).ToArray(), tags[325]);
        Assert.DoesNotContain((ushort)273, tags.Keys);
        Assert.DoesNotContain((ushort)278, tags.Keys);
        Assert.DoesNotContain((ushort)279, tags.Keys);
        Assert.DoesNotContain((ushort)317, tags.Keys);

        var pixels = GetPixelBytes(source);
        for (var tile = 0; tile < 6; tile++)
        {
            var expected = new byte[16 * 16 * 4];
            var left = tile % 3 * 16;
            var top = tile / 3 * 16;
            for (var y = 0; y < 16 && top + y < 23; y++)
            {
                var columns = Math.Min(16, 37 - left);
                pixels.AsSpan((((top + y) * 37) + left) * 4, columns * 4).CopyTo(expected.AsSpan(y * 16 * 4));
            }

            Assert.Equal(expected, file.AsSpan((int)tags[324][tile], (int)tags[325][tile]).ToArray());
        }

        // With horizontal differencing, each row of a tile starts its own differences, over the padded width
        var predicted = Save(source, new TiffEncoder { Predictor = TiffPredictor.HorizontalDifferencing, TileWidth = 16, TileHeight = 16 });
        var predictedTags = ReadTags(predicted);
        Assert.Equal([2UL], predictedTags[317]);
        var lastTile = Inflate(predicted.AsSpan((int)predictedTags[324][5], (int)predictedTags[325][5]).ToArray());
        var firstRow = pixels.AsSpan(((16 * 37) + 32) * 4, 5 * 4).ToArray();
        Assert.Equal(firstRow[..4], lastTile[..4]);
        Assert.Equal((byte)(firstRow[4] - firstRow[0]), lastTile[4]);
        Assert.Equal((byte)(0 - firstRow[16]), lastTile[20]);
        Assert.Equal(new byte[(16 - 6) * 4], lastTile.AsSpan(6 * 4, (16 - 6) * 4).ToArray());
    }

    public static TheoryData<string> TiledPageCases =>
    [
        "rgba32-none", "rgba32-deflate", "rgba32-lzw-predictor", "rgba32-packbits", "rgba64-deflate-predictor-big-endian",
        "rgba64-lzw-predictor-bigtiff", "float-deflate", "float-lzw-big-endian", "indexed-lzw", "indexed-packbits",
        "bilevel-group4", "bilevel-group3", "bilevel-modified-huffman", "bilevel-none", "noise-deflate-predictor", "rgba32-one-tile",
    ];

    [Theory]
    [MemberData(nameof(TiledPageCases))]
    public void TiledPagesStoreThePixelsOfStrips(string name)
    {
        // A page of 300x230 pixels in tiles of 64x48 pixels: five tiles across and five down, the last ones partial
        var pixels = name.Split('-')[0];
        using var image = CreateWorkerImage(pixels, ImageConfiguration.Default);
        var compression = name.Contains("-deflate", StringComparison.Ordinal) ? TiffCompression.Deflate
            : name.Contains("-lzw", StringComparison.Ordinal) ? TiffCompression.Lzw
            : name.Contains("-packbits", StringComparison.Ordinal) ? TiffCompression.PackBits
            : name.Contains("-group4", StringComparison.Ordinal) ? TiffCompression.CcittGroup4
            : name.Contains("-group3", StringComparison.Ordinal) ? TiffCompression.CcittGroup3
            : name.Contains("-modified-huffman", StringComparison.Ordinal) ? TiffCompression.CcittModifiedHuffman
            : name.Contains("-one-tile", StringComparison.Ordinal) ? TiffCompression.Deflate
            : TiffCompression.None;
        var strips = new TiffEncoder
        {
            Compression = compression,
            Predictor = name.Contains("-predictor", StringComparison.Ordinal) ? TiffPredictor.HorizontalDifferencing : TiffPredictor.None,
            BigEndian = name.EndsWith("-big-endian", StringComparison.Ordinal),
            BigTiff = name.EndsWith("-bigtiff", StringComparison.Ordinal),
            PixelLayout = pixels switch
            {
                "indexed" => TiffPixelLayout.Indexed,
                "bilevel" => TiffPixelLayout.Bilevel,
                _ => TiffPixelLayout.Auto,
            },
        };

        var tileSize = name.EndsWith("-one-tile", StringComparison.Ordinal) ? 304 : 0;
        var tiles = new TiffEncoder
        {
            Compression = strips.Compression,
            Predictor = strips.Predictor,
            BigEndian = strips.BigEndian,
            BigTiff = strips.BigTiff,
            PixelLayout = strips.PixelLayout,
            TileWidth = tileSize > 0 ? tileSize : 64,
            TileHeight = tileSize > 0 ? tileSize : 48,
        };

        using var expected = Image.Load(Save(image, strips));
        var file = Save(image, tiles);
        using var decoded = Image.Load(file);
        Assert.Equal(expected.PixelFormat, decoded.PixelFormat);
        Assert.Equal(GetPixelBytes(expected), GetPixelBytes(decoded));
        if (!tiles.BigEndian && !tiles.BigTiff)
        {
            Assert.HasCount(tileSize > 0 ? 1 : 25, ReadTags(file)[324]);
        }

        // The pages of a document are written the same way
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(image);
        pages.Add(image);
        using var stream = new MemoryStream();
        pages.Save(stream, tiles);
        using var reloaded = ImageCollection.Load(stream.ToArray());
        using var second = reloaded[1].Decode();
        Assert.Equal(GetPixelBytes(expected), GetPixelBytes(second));
    }

    [Theory]
    [InlineData(TiffCompression.Lzw, "rgba32", 64, 48, TiffPredictor.None)]
    [InlineData(TiffCompression.Deflate, "rgba64-big-endian", 16, 32, TiffPredictor.HorizontalDifferencing)]
    [InlineData(TiffCompression.Deflate, "rgba32", 0, 0, TiffPredictor.HorizontalDifferencing)]
    [InlineData(TiffCompression.Lzw, "float", 128, 16, TiffPredictor.None)]
    [InlineData(TiffCompression.CcittGroup4, "bilevel", 64, 64, TiffPredictor.None)]
    public void TilesAndPredictedStripsCompressedByWorkersAreTheFileOfOneWorker(TiffCompression compression, string pixels, int tileWidth, int tileHeight, TiffPredictor predictor)
    {
        var encoder = new TiffEncoder
        {
            Compression = compression,
            Predictor = predictor,
            TileWidth = tileWidth,
            TileHeight = tileHeight,
            RowsPerStrip = 7,
            BigEndian = pixels == "rgba64-big-endian",
            PixelLayout = pixels == "bilevel" ? TiffPixelLayout.Bilevel : TiffPixelLayout.Auto,
        };

        var (expected, peak) = SaveWithWorkers(pixels, encoder, workers: 1);
        foreach (var workers in new[] { 2, 3, 16 })
        {
            var (file, workerPeak) = SaveWithWorkers(pixels, encoder, workers);
            Assert.Equal(expected, file);

            // Each worker rents a block and an output: the LZW and Deflate blocks are compressed by workers, the CCITT ones by one
            if (compression is TiffCompression.Lzw or TiffCompression.Deflate)
            {
                Assert.InRange(workerPeak, peak + 1, long.MaxValue);
            }
            else
            {
                Assert.Equal(peak, workerPeak);
            }
        }
    }

    [Fact]
    public void ADocumentUsesTheLayoutAndTheCompressionForEveryPage()
    {
        // Pages of other sizes and pixel formats, each with its own colors: a color map per page
        using var first = Image.ImportPixelData<Rgb24>([new Rgb24(1, 2, 3), new Rgb24(4, 5, 6), new Rgb24(1, 2, 3)], 3, 1);
        using var second = Image.ImportPixelData<Gray8>([new Gray8(9), new Gray8(8)], 1, 2);
        using var pattern = CreatePattern(33, 21);
        using var third = pattern.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        foreach (var encoder in new[] { new TiffEncoder { Compression = TiffCompression.Lzw, PixelLayout = TiffPixelLayout.Indexed }, new TiffEncoder { Compression = TiffCompression.PackBits }, new TiffEncoder { Compression = TiffCompression.Jpeg, JpegChromaSubsampling = JpegChromaSubsampling.Ratio444, JpegQuality = 100 } })
        {
            using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
            pages.Add(first);
            pages.Add(second);
            if (encoder.PixelLayout != TiffPixelLayout.Indexed)
            {
                pages.Add(third);
            }

            using var stream = new MemoryStream();
            pages.Save(stream, encoder);
            stream.Position = 0;
            using var reloaded = ImageCollection.Load(stream);
            Assert.Equal(pages.Count, reloaded.Count);
            Assert.Equal(new Size(3, 1), reloaded[0].Size);
            Assert.Equal(new Size(1, 2), reloaded[1].Size);
            if (encoder.Compression == TiffCompression.Jpeg)
            {
                Assert.Equal(ImageColorModel.YCbCr, reloaded[0].ColorModel);
                Assert.Equal(ImageColorModel.Grayscale, reloaded[1].ColorModel);
                continue;
            }

            using var page0 = reloaded[0].Decode<Rgb24>();
            TestImages.AssertSamePixels(first, page0);
            if (encoder.PixelLayout == TiffPixelLayout.Indexed)
            {
                // A palette page is color, whatever the pixel format it was written from
                using var expected = Image.ImportPixelData<Rgb24>([new Rgb24(9, 9, 9), new Rgb24(8, 8, 8)], 1, 2);
                using var page1 = reloaded[1].Decode<Rgb24>();
                TestImages.AssertSamePixels(expected, page1);
            }
            else
            {
                using var page1 = reloaded[1].Decode<Gray8>();
                TestImages.AssertSamePixels(second, page1);
                using var page2 = reloaded[2].Decode<Rgb24>();
                TestImages.AssertSamePixels(third, page2);
            }
        }

        // A page the layout cannot store stops the document: an error, never a document that misses a page
        using var blackAndWhite = Image.ImportPixelData<Gray8>([new Gray8(0), new Gray8(255)], 2, 1);
        using var bilevel = ImageCollection.Create(ImageCollectionKind.Pages);
        bilevel.Add(blackAndWhite);
        bilevel.Add(second);
        using var output = new MemoryStream();
        Assert.Throws<UnsupportedImageFeatureException>(() => bilevel.Save(output, new TiffEncoder { Compression = TiffCompression.CcittGroup4, PixelLayout = TiffPixelLayout.Bilevel }));
    }

    [Fact]
    public void AColorProfileMustDescribeTheWrittenSamples()
    {
        // A bilevel page is grayscale and a palette page is RGB, whatever the pixel format of the image
        using var rgb = Image.ImportPixelData<Rgb24>([new Rgb24(0, 0, 0), new Rgb24(255, 255, 255)], 2, 1);
        rgb.Metadata.IccProfile = IccProfile.Srgb;
        using var gray = Image.ImportPixelData<Gray8>([new Gray8(0), new Gray8(255)], 2, 1);
        gray.Metadata.IccProfile = IccProfile.SrgbGray;
        using var stream = new MemoryStream();

        Assert.Throws<UnsupportedImageFeatureException>(() => rgb.Save(stream, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel }));
        Assert.Throws<UnsupportedImageFeatureException>(() => gray.Save(stream, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed }));
        Assert.Equal(0, stream.Length);

        using (var decoded = RoundTrip(gray, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel }))
        {
            Assert.Equal(IccProfile.SrgbGray.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
        }

        using (var decoded = RoundTrip(rgb, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed }))
        {
            Assert.Equal(IccProfile.Srgb.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
        }

        using (var decoded = RoundTrip(rgb, new TiffEncoder { Compression = TiffCompression.Jpeg }))
        {
            Assert.Equal(IccProfile.Srgb.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
        }

        using (var decoded = RoundTrip(rgb, new TiffEncoder { PixelLayout = TiffPixelLayout.Bilevel, MetadataHandling = MetadataHandling.DiscardUnsupported }))
        {
            Assert.Null(decoded.Metadata.IccProfile);
        }
    }

    /// <summary>
    /// Floating-point values a page must store exactly: signed zeros, values outside [0, 1], the largest and the smallest
    /// singles, subnormal numbers, infinities and a not-a-number value with a payload.
    /// </summary>
    private static readonly float[] SpecialSingles =
    [
        0f, -0f, 1f, 0.5f, -0.25f, float.MaxValue, 1e-40f, float.Epsilon, 65504f, -1234.5678f,
        float.PositiveInfinity, float.NegativeInfinity, BitConverter.UInt32BitsToSingle(0x7FC0_0001), 0.1f, 3.5f, float.MinValue, -1e-40f,
    ];

    public static TheoryData<TiffCompression, bool, bool, int> FloatingPointCases()
    {
        var data = new TheoryData<TiffCompression, bool, bool, int>();
        foreach (var compression in new[] { TiffCompression.None, TiffCompression.Deflate, TiffCompression.Lzw, TiffCompression.PackBits })
        {
            foreach (var (bigEndian, bigTiff) in new[] { (false, false), (true, false), (false, true) })
            {
                foreach (var samplesPerPixel in new[] { 1, 2, 3, 4 })
                {
                    data.Add(compression, bigEndian, bigTiff, samplesPerPixel);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FloatingPointCases))]
    public void FloatingPointPixelsAreWrittenExactlyInTheFewestSamplesThatHoldThem(TiffCompression compression, bool bigEndian, bool bigTiff, int samplesPerPixel)
    {
        // Several strips; the gray pages have three equal colors and the opaque ones an alpha of exactly 1
        using var image = CreateFloatingPointPattern(23, 17, gray: samplesPerPixel <= 2, opaque: samplesPerPixel % 2 == 1);
        var data = Save(image, new TiffEncoder { Compression = compression, BigEndian = bigEndian, BigTiff = bigTiff, RowsPerStrip = 5 });
        var info = Image.Identify(data);
        var model = samplesPerPixel switch
        {
            1 => ImageColorModel.Grayscale,
            2 => ImageColorModel.GrayscaleAlpha,
            3 => ImageColorModel.Rgb,
            _ => ImageColorModel.Rgba,
        };
        Assert.Equal((PixelFormat.RgbaFloat, model, 32, ColorTransferFunction.Linear), (info.PixelFormat, info.ColorModel, info.BitsPerComponent, info.Metadata.TransferFunction));

        using var decoded = Image.Load<RgbaFloat>(data);
        Assert.Equal(GetComponentBits(image), GetComponentBits(decoded));
        if (bigEndian || bigTiff)
            return;

        // 32-bit floating-point samples, with the floating-point predictor when the compression is Deflate or LZW
        var tags = ReadTags(data);
        Assert.Equal([.. Enumerable.Repeat(32UL, samplesPerPixel)], tags[258]);
        Assert.Equal([.. Enumerable.Repeat(3UL, samplesPerPixel)], tags[339]);
        Assert.Equal([samplesPerPixel <= 2 ? 1UL : 2UL], tags[262]);
        Assert.Equal(samplesPerPixel % 2 == 0, tags.TryGetValue(338, out var extra) && extra is [2]);
        if (compression is TiffCompression.Deflate or TiffCompression.Lzw)
        {
            Assert.Equal([3UL], tags[317]);
        }
        else
        {
            Assert.DoesNotContain((ushort)317, tags.Keys);
        }
    }

    [Fact]
    public void HalfPrecisionPixelsAreWrittenAsSingles()
    {
        var halves = SpecialSingles.Select(static value => (Half)value).ToArray();
        var pixels = new RgbaHalf[halves.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new RgbaHalf(halves[i], halves[(i + 3) % halves.Length], halves[(i + 7) % halves.Length], halves[(i + 1) % halves.Length]);
        }

        using var image = Image.ImportPixelData<RgbaHalf>(pixels, pixels.Length, 1);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var data = Save(image, new TiffEncoder());
        Assert.Equal([32UL, 32, 32, 32], ReadTags(data)[258]);
        using var decoded = Image.Load(data);
        Assert.Equal(PixelFormat.RgbaFloat, decoded.PixelFormat);
        using var expected = image.CloneAs<RgbaFloat>();
        Assert.Equal(GetComponentBits(expected), GetComponentBits((Image<RgbaFloat>)decoded));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void TheFloatingPointPredictorIsTheOneOfTechnicalNote3(int samplesPerPixel)
    {
        // The strip is inflated and its rows rebuilt by the reference of the tests, written from the technical note
        using var image = CreateFloatingPointPattern(9, 4, gray: samplesPerPixel == 1, opaque: samplesPerPixel == 1);
        var strip = ReadStrip(Save(image, new TiffEncoder()));
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(strip), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var pixels = new RgbaFloat[9 * 4];
        image.Frames[0].CopyPixelDataTo(pixels);
        var rowLength = 9 * samplesPerPixel * sizeof(float);
        Assert.Equal(4 * rowLength, inflated.Length);
        for (var y = 0; y < 4; y++)
        {
            var row = TiffReferenceCodecs.UndoFloatingPointPredictor(inflated.ToArray().AsSpan(y * rowLength, rowLength), samplesPerPixel, sizeof(float), bigEndian: false);
            for (var x = 0; x < 9; x++)
            {
                var pixel = pixels[(y * 9) + x];
                float[] expected = samplesPerPixel == 1 ? [pixel.R] : [pixel.R, pixel.G, pixel.B, pixel.A];
                for (var c = 0; c < samplesPerPixel; c++)
                {
                    Assert.Equal(BitConverter.SingleToUInt32Bits(expected[c]), BinaryPrimitives.ReadUInt32LittleEndian(row.AsSpan(((x * samplesPerPixel) + c) * sizeof(float))));
                }
            }
        }
    }

    [Fact]
    public void AnRgbColorProfileKeepsGrayFloatingPointPixelsInRgbSamples()
    {
        // A gray sample cannot be described by an RGB profile
        using var image = CreateFloatingPointPattern(4, 3, gray: true, opaque: true);
        image.Metadata.IccProfile = IccProfile.Srgb;
        var data = Save(image, new TiffEncoder());
        var info = Image.Identify(data);
        Assert.Equal(ImageColorModel.Rgb, info.ColorModel);
        Assert.Equal(IccProfile.Srgb.Data.ToArray(), info.Metadata.IccProfile?.Data.ToArray());
        using var decoded = Image.Load<RgbaFloat>(data);
        Assert.Equal(GetComponentBits(image), GetComponentBits(decoded));
    }

    [Fact]
    public void FloatingPointPixelsAreLinearLightUnlessAProfileDescribesThem()
    {
        // A conversion keeps the label of its source: these pixels say they are sRGB encoded, and a floating-point page
        // without profile is read as linear light
        using var rgba = CreatePattern(4, 3);
        using var encoded = rgba.CloneAs<RgbaFloat>();
        Assert.Equal(ColorTransferFunction.Srgb, encoded.Metadata.TransferFunction);
        using (var rejected = new MemoryStream())
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => encoded.Save(rejected, new TiffEncoder()));
            Assert.Contains("sRGB transfer function", exception.Message);
            Assert.Equal(0, rejected.Length);
        }

        using (var decoded = RoundTrip(encoded, new TiffEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }))
        {
            Assert.Equal(ColorTransferFunction.Linear, decoded.Metadata.TransferFunction);
        }

        // A profile describes them
        encoded.Metadata.IccProfile = IccProfile.Srgb;
        using (var decoded = RoundTrip(encoded, new TiffEncoder()))
        {
            Assert.Equal(ColorTransferFunction.Srgb, decoded.Metadata.TransferFunction);
            Assert.Equal(IccProfile.Srgb.Data.ToArray(), decoded.Metadata.IccProfile?.Data.ToArray());
        }

        // An integer page is still not labeled linear light
        using var linear = rgba.Clone();
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        Assert.Throws<UnsupportedImageFeatureException>(() => Save(linear, new TiffEncoder()));
    }

    [Fact]
    public void TheSettingsThatCannotStoreFloatingPointPixelsReduceThemWithThePolicyOfTheEncoder()
    {
        using var image = Image.ImportPixelData<RgbaFloat>([new RgbaFloat(0, 0, 0), new RgbaFloat(1, 1, 1), new RgbaFloat(1, 0, 0), new RgbaFloat(0, 0, 0)], 2, 2);
        foreach (var encoder in new[] { new TiffEncoder { Compression = TiffCompression.Jpeg }, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed } })
        {
            using var stream = new MemoryStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, encoder));
            Assert.Contains("FloatingPointConversion", exception.Message);
            Assert.Equal(0, stream.Length);
        }

        using var decoded = RoundTrip(image, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed, FloatingPointConversion = new PixelConversionOptions(), MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
        Assert.Equal(new Rgb24(255, 0, 0), ((Image<Rgb24>)decoded).Frames[0][0, 1]);
    }

    [Theory]
    [InlineData(TiffCompression.Lzw, "rgba32", 7)]
    [InlineData(TiffCompression.Lzw, "rgba32", 0)]
    [InlineData(TiffCompression.Deflate, "rgba32", 7)]
    [InlineData(TiffCompression.Deflate, "rgba64", 7)]
    [InlineData(TiffCompression.Lzw, "rgba64-big-endian", 7)]
    [InlineData(TiffCompression.Deflate, "float", 7)]
    [InlineData(TiffCompression.Lzw, "float", 0)]
    [InlineData(TiffCompression.Lzw, "indexed", 7)]
    [InlineData(TiffCompression.Deflate, "noise", 7)]
    [InlineData(TiffCompression.PackBits, "rgba32", 7)]
    [InlineData(TiffCompression.None, "rgba32", 7)]
    [InlineData(TiffCompression.CcittGroup4, "bilevel", 7)]
    public void StripsCompressedByWorkersAreTheFileOfOneWorker(TiffCompression compression, string pixels, int rowsPerStrip)
    {
        var encoder = new TiffEncoder
        {
            Compression = compression,
            RowsPerStrip = rowsPerStrip,
            BigEndian = pixels == "rgba64-big-endian",
            PixelLayout = pixels switch
            {
                "indexed" => TiffPixelLayout.Indexed,
                "bilevel" => TiffPixelLayout.Bilevel,
                _ => TiffPixelLayout.Auto,
            },
        };

        var (expected, peak) = SaveWithWorkers(pixels, encoder, workers: 1);
        foreach (var workers in new[] { 2, 3, 16 })
        {
            var (file, workerPeak) = SaveWithWorkers(pixels, encoder, workers);
            Assert.Equal(expected, file);

            // Each worker rents a strip and an output: the LZW and Deflate strips are compressed by workers, the others, which
            // cost about what serializing them costs, by one
            if (compression is TiffCompression.Lzw or TiffCompression.Deflate)
            {
                Assert.InRange(workerPeak, peak + 1, long.MaxValue);
            }
            else
            {
                Assert.Equal(peak, workerPeak);
            }
        }

        // The pages of a document are written the same way
        using var page = CreateWorkerImage(pixels, ImageConfiguration.Default);
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages, new ImageConfiguration { MaxDegreeOfParallelism = 16 });
        pages.Add(page);
        pages.Add(page);
        using var stream = new MemoryStream();
        pages.Save(stream, encoder);
        using var reloaded = ImageCollection.Load(stream.ToArray());
        using var first = reloaded[0].Decode();
        using var second = reloaded[1].Decode();
        using var single = Image.Load(expected);
        Assert.Equal(GetPixelBytes(single), GetPixelBytes(first));
        Assert.Equal(GetPixelBytes(single), GetPixelBytes(second));
    }

    [Theory]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.Deflate)]
    public void WorkersCompressWithinTheAllocationLimitOfOneWorker(TiffCompression compression)
    {
        // At the smallest limit one worker writes the document with, the workers write it too, with the same bytes; below it,
        // they fail as one worker does. The strips of 100 rows make the writer, not the copy of the page, the largest need
        using var page = CreateWorkerImage("rgba32", ImageConfiguration.Default);
        var encoder = new TiffEncoder { Compression = compression, RowsPerStrip = 100 };
        var low = 0L;
        var high = 64L << 20;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            try
            {
                SaveDocument(workers: 1, middle);
                high = middle;
            }
            catch (ImageResourceLimitException)
            {
                low = middle;
            }
        }

        var expected = SaveDocument(workers: 1, high);
        var failure = Assert.Throws<ImageResourceLimitException>(() => SaveDocument(workers: 1, low));
        foreach (var workers in new[] { 2, 16 })
        {
            Assert.Equal(expected, SaveDocument(workers, high));
            Assert.Equal(failure.Message, Assert.Throws<ImageResourceLimitException>(() => SaveDocument(workers, low)).Message);

            // A limit that leaves room for some workers only writes the same document
            Assert.Equal(expected, SaveDocument(workers, high + (512 << 10)));
        }

        byte[] SaveDocument(int workers, long limit)
        {
            using var pages = ImageCollection.Create(ImageCollectionKind.Pages, new ImageConfiguration { MaxDegreeOfParallelism = workers, Limits = new ImageResourceLimits { MaxLiveAllocationBytes = limit } });
            pages.Add(page);
            using var stream = new MemoryStream();
            pages.Save(stream, encoder);
            return stream.ToArray();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public async Task ACancellationWhileWorkersCompressTheStripsIsReported(int workers)
    {
        // The destination cancels the encoding once it receives its first bytes, between two strips
        using var image = CreateWorkerImage("rgba32", new ImageConfiguration { MaxDegreeOfParallelism = workers });
        using var cancellation = new CancellationTokenSource();
        await using var stream = new CancellingStream(cancellation);
        await Assert.ThrowsAny<OperationCanceledException>(() => image.SaveAsync(stream, new TiffEncoder { Compression = TiffCompression.Lzw, RowsPerStrip = 7 }, cancellation.Token));
        Assert.InRange(stream.Length, 1, 300 * 230 * 4);
    }

    /// <summary>Saves an image of <see cref="CreateWorkerImage"/>, and measures the most live bytes of its scopes, which must all be released.</summary>
    private static (byte[] File, long PeakLiveBytes) SaveWithWorkers(string pixels, TiffEncoder encoder, int workers)
    {
        using var audit = new PoolAudit();
        byte[] file;
        using (var image = CreateWorkerImage(pixels, new ImageConfiguration { MaxDegreeOfParallelism = workers }))
        {
            file = Save(image, encoder);
        }

        var peak = audit.Scopes.Sum(static scope => scope.GetDiagnostics().PeakLiveBytes);
        audit.AssertClean(string.Create(CultureInfo.InvariantCulture, $"an encoding by {workers} workers"));
        return (file, peak);
    }

    /// <summary>
    /// An image of 300x230 pixels: more than the 65,536 pixels from which strips are compressed by several workers, and a last
    /// strip of 6 rows with strips of 7 rows.
    /// </summary>
    private static Image CreateWorkerImage(string pixels, ImageConfiguration configuration)
    {
        const int Width = 300;
        const int Height = 230;
        using var pattern = CreatePattern(Width, Height);
        var colors = new Rgba32[Width * Height];
        pattern.Frames[0].CopyPixelDataTo(colors);
        switch (pixels)
        {
            case "rgba32":
                return Image.ImportPixelData<Rgba32>(colors, Width, Height, configuration: configuration);

            case "rgba64" or "rgba64-big-endian":
                return Image.ImportPixelData<Rgba64>([.. colors.Select(static (color, i) => new Rgba64((ushort)((color.R * 257) + (i % 251)), (ushort)(color.G * 251), (ushort)(color.B * 7), (ushort)((color.A * 257) - (i % 7))))], Width, Height, configuration: configuration);

            case "float":
                var floats = Image.ImportPixelData<RgbaFloat>([.. colors.Select(static (color, i) => new RgbaFloat(color.R / 255f, color.G / 97f, -color.B / 31f, (i % 5) / 4f))], Width, Height, configuration: configuration);
                floats.Metadata.TransferFunction = ColorTransferFunction.Linear;
                return floats;

            case "indexed":
                return Image.ImportPixelData<Rgb24>([.. colors.Select(static color => new Rgb24((byte)(color.R & 0xE0), (byte)(color.G & 0xC0), (byte)(color.B & 0xE0)))], Width, Height, configuration: configuration);

            case "noise":
                // Strips that Deflate stores rather than compresses
                var noise = new byte[Width * Height * 3];
                var state = 977u;
                for (var i = 0; i < noise.Length; i++)
                {
                    state = (state * 1664525) + 1013904223;
                    noise[i] = (byte)(state >> 24);
                }

                return Image.ImportPixelBytes<Rgb24>(noise, Width, Height, configuration: configuration);

            default:
                return Image.ImportPixelData<Gray8>([.. colors.Select(static color => new Gray8(color.R >= 128 ? (byte)255 : (byte)0))], Width, Height, configuration: configuration);
        }
    }

    private static byte[] GetPixelBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    /// <summary>A floating-point image of <see cref="SpecialSingles"/>: gray pixels have three equal colors, and opaque pixels an alpha of exactly 1.</summary>
    private static Image<RgbaFloat> CreateFloatingPointPattern(int width, int height, bool gray, bool opaque)
    {
        var values = SpecialSingles;
        var pixels = new RgbaFloat[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var red = values[i % values.Length];
            var alpha = opaque ? 1 : values[((i * 3) + 1) % values.Length];
            pixels[i] = gray ? new RgbaFloat(red, red, red, alpha) : new RgbaFloat(red, values[(i + 5) % values.Length], values[(i + 11) % values.Length], alpha);
        }

        var image = Image.ImportPixelData<RgbaFloat>(pixels, width, height);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        return image;
    }

    /// <summary>Gets the bits of the components of an image, so that signed zeros and not-a-number values are compared exactly.</summary>
    private static uint[] GetComponentBits(Image<RgbaFloat> image)
    {
        var pixels = new RgbaFloat[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return [.. pixels.SelectMany(static pixel => new[] { pixel.R, pixel.G, pixel.B, pixel.A }).Select(BitConverter.SingleToUInt32Bits)];
    }

    /// <summary>An image with flat areas, gradients and noise, and an alpha channel that takes every value.</summary>
    internal static Image<Rgba32> CreatePattern(int width, int height)
    {
        var pixels = new Rgba32[width * height];
        var state = 12345u;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                state = (state * 1664525) + 1013904223;
                var noise = (byte)(state >> 24);
                pixels[(y * width) + x] = (y / 10 % 3) switch
                {
                    0 => new Rgba32(200, 30, 90, 255),
                    1 => new Rgba32((byte)(x * 3), (byte)(y * 5), (byte)(x + y), (byte)(x * 7)),
                    _ => new Rgba32(noise, (byte)(noise * 3), (byte)(noise ^ x), (byte)(noise + y)),
                };
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>
    /// Reads the tags of the first directory of a little-endian classic TIFF, independently of the library: the values of
    /// each tag as numbers (a RATIONAL is its numerator then its denominator).
    /// </summary>
    private static Dictionary<ushort, ulong[]> ReadTags(byte[] file)
    {
        Assert.Equal("II*\0"u8.ToArray(), file[..4]);
        var directory = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(directory));
        var tags = new Dictionary<ushort, ulong[]>();
        ushort previous = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = file.AsSpan(directory + 2 + (i * 12), 12);
            var tag = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var type = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            var valueCount = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            var size = type switch
            {
                1 or 2 or 7 => 1,
                3 => 2,
                _ => 4,
            };

            // A RATIONAL is two LONG values
            var numbers = type == 5 ? valueCount * 2 : valueCount;
            var data = numbers * size <= 4 ? entry[8..] : file.AsSpan(BinaryPrimitives.ReadInt32LittleEndian(entry[8..]));
            var values = new ulong[numbers];
            for (var j = 0; j < numbers; j++)
            {
                values[j] = size switch
                {
                    1 => data[j],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(data[(j * 2)..]),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(data[(j * 4)..]),
                };
            }

            // The entries of a directory are sorted by tag
            Assert.True(tag > previous);
            previous = tag;
            tags.Add(tag, values);
        }

        return tags;
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Reads the stored bytes of one strip of the first directory of a little-endian classic TIFF.</summary>
    private static byte[] ReadStrip(byte[] file, int index = 0)
    {
        var tags = ReadTags(file);
        return file.AsSpan((int)tags[273][index], (int)tags[279][index]).ToArray();
    }

    internal static Image RoundTrip(Image source, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        source.Save(stream, encoder);
        stream.Position = 0;
        return Image.Load(stream);
    }

    /// <summary>A seekable destination that cancels a token once it receives bytes.</summary>
    private sealed class CancellingStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.Write(buffer);
            cancellation.Cancel();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            cancellation.Cancel();
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            await cancellation.CancelAsync();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
