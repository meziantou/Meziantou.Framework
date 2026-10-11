using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// TIFF and BigTIFF encoding: every working pixel format is written losslessly in both byte orders, both offset sizes
/// and every lossless compression, the bilevel and palette layouts store a page exactly or reject it, a JPEG page holds
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
        {
            Assert.Equal(PixelFormat.Rgba64, decoded.PixelFormat);
            TestImages.AssertSamePixels(deep, (Image<Rgba64>)decoded);
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
}
