using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// TIFF and BigTIFF encoding: the documented subset is written losslessly for every working pixel format, both
/// byte orders, both offset sizes and both compressions, multi-page documents keep the size, pixel format and metadata
/// of every page, and the container constraints (seekable destination, single page per <c>Image.Save</c>, no animation)
/// are enforced before anything is written. Every output is read back by this library's own decoder, which the
/// byte-for-byte decoder tests validate independently.
/// </summary>
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
        using var source = CreateRgba(5, 4);
        var encoder = new TiffEncoder { Compression = compression, BigEndian = bigEndian, BigTiff = bigTiff };
        using var image = RoundTrip(source, encoder);
        Assert.Equal(ImageFormat.Tiff, image.Metadata.SourceFormat);
        Assert.Equal(source.Size, image.Size);
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        AssertSamePixels(source, (Image<Rgba32>)image);
    }

    [Fact]
    public void EveryWorkingPixelFormatIsWrittenLosslessly()
    {
        using (var gray = Image.ImportPixelData<Gray8>([new Gray8(1), new Gray8(200), new Gray8(0), new Gray8(255)], 2, 2))
        using (var decoded = RoundTrip(gray, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Gray8, decoded.PixelFormat);
            AssertSamePixels(gray, (Image<Gray8>)decoded);
        }

        using (var gray16 = Image.ImportPixelData<Gray16>([new Gray16(1), new Gray16(60000), new Gray16(0), new Gray16(65535)], 2, 2))
        using (var decoded = RoundTrip(gray16, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Gray16, decoded.PixelFormat);
            AssertSamePixels(gray16, (Image<Gray16>)decoded);
        }

        using (var rgb = Image.ImportPixelData<Rgb24>([new Rgb24(1, 2, 3), new Rgb24(250, 251, 252)], 2, 1))
        using (var decoded = RoundTrip(rgb, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
            AssertSamePixels(rgb, (Image<Rgb24>)decoded);
        }

        using (var rgba64 = Image.ImportPixelData<Rgba64>([new Rgba64(1, 2, 3, 4), new Rgba64(65535, 60000, 100, 7)], 2, 1))
        using (var decoded = RoundTrip(rgba64, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Rgba64, decoded.PixelFormat);
            AssertSamePixels(rgba64, (Image<Rgba64>)decoded);
        }

        // Bgra32 stores the same information as Rgba32 and is written as RGBA, not reordered away
        using (var bgra = Image.ImportPixelData<Bgra32>([new Bgra32(1, 2, 3, 4), new Bgra32(9, 8, 7, 6)], 2, 1))
        using (var decoded = RoundTrip(bgra, new TiffEncoder()))
        {
            Assert.Equal(PixelFormat.Rgba32, decoded.PixelFormat);
            using var expected = bgra.CloneAs<Rgba32>();
            AssertSamePixels(expected, (Image<Rgba32>)decoded);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void TheStripHeightDoesNotChangeThePixels(int rowsPerStrip)
    {
        using var source = CreateRgba(7, 11);
        using var decoded = RoundTrip(source, new TiffEncoder { RowsPerStrip = rowsPerStrip });
        AssertSamePixels(source, (Image<Rgba32>)decoded);
    }

    [Fact]
    public void AMultiPageDocumentKeepsTheSizeAndPixelFormatOfEveryPage()
    {
        using var first = CreateRgba(4, 3);
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
        AssertSamePixels(first, page0);
        using var page1 = reloaded[1].Decode<Gray8>();
        AssertSamePixels(second, page1);
    }

    [Fact]
    public void ALoadedDocumentCanBeReSavedWithoutMaterializingEveryPage()
    {
        using var first = CreateRgba(4, 3);
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
        AssertSamePixels(first, page0);
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
        using var source = CreateRgba(2, 2);
        source.Metadata.Resolution = new ImageResolution(300, 150);
        source.Metadata.Orientation = ExifOrientation.BottomRight;

        using var decoded = RoundTrip(source, new TiffEncoder());
        Assert.Equal(300, decoded.Metadata.Resolution!.HorizontalDpi);
        Assert.Equal(150, decoded.Metadata.Resolution.VerticalDpi);
        Assert.Equal(ExifOrientation.BottomRight, decoded.Metadata.Orientation);

        // A TIFF stores no EXIF block in this version: the strict policy rejects it instead of dropping it
        using var withExif = CreateRgba(2, 2);
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
        using var source = CreateRgba(2, 2);
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
        using var source = CreateRgba(2, 2);
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
        using var animated = CreateRgba(2, 2);
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
            using var source = CreateRgba(3, 2);
            source.Save(path);
            Assert.Equal(ImageFormat.Tiff, Image.DetectFormat(File.ReadAllBytes(path)));
            using var reloaded = (Image<Rgba32>)Image.Load(path);
            AssertSamePixels(source, reloaded);

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

    internal static Image<Rgba32> CreateRgba(int width, int height)
    {
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)(i * 7), (byte)(i * 13), (byte)(i * 29), (byte)(200 - (i % 200)));
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    internal static Image RoundTrip(Image source, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        source.Save(stream, encoder);
        stream.Position = 0;
        return Image.Load(stream);
    }

    internal static void AssertSamePixels<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged
    {
        Assert.Equal(expected.Size, actual.Size);
        var expectedPixels = new TPixel[expected.Width * expected.Height];
        var actualPixels = new TPixel[expected.Width * expected.Height];
        expected.Frames[0].CopyPixelDataTo(expectedPixels);
        actual.Frames[0].CopyPixelDataTo(actualPixels);
        Assert.Equal(expectedPixels, actualPixels);
    }
}
