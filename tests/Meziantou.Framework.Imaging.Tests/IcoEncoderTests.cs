using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// ICO and CUR encoding: a directory of alternative representations, each written as a still PNG or a 32-bit DIB,
/// with the hotspots of a cursor, the 256-pixel limit, and the rules that keep representations from being mistaken for
/// animation frames. Every output is read back by this library's decoder, whose own tests validate it byte for byte
/// against independently assembled files.
/// </summary>
public sealed class IcoEncoderTests
{
    [Theory]
    [InlineData(IconPayloadFormat.Auto)]
    [InlineData(IconPayloadFormat.Png)]
    [InlineData(IconPayloadFormat.Dib)]
    public void ASingleRepresentationRoundTrips(IconPayloadFormat payloadFormat)
    {
        using var source = TiffEncoderTests.CreateRgba(8, 6);
        using var stream = new MemoryStream();
        source.Save(stream, new IcoEncoder { PayloadFormat = payloadFormat });
        stream.Position = 0;

        using var collection = ImageCollection.Load(stream);
        Assert.Equal(ImageFormat.Ico, collection.Format);
        Assert.Equal(ImageCollectionKind.Representations, collection.Kind);
        Assert.Single(collection.Entries);
        Assert.Equal(new Size(8, 6), collection[0].Size);
        Assert.Equal(payloadFormat == IconPayloadFormat.Png ? ImageFormat.Png : ImageFormat.Bmp, collection[0].PayloadFormat);

        using var decoded = (Image<Rgba32>)collection[0].Decode();
        TiffEncoderTests.AssertSamePixels(source, decoded);
    }

    [Fact]
    public void AutoWritesADibForSmallSizesAndAPngForLargeOnes()
    {
        Assert.Equal(ImageFormat.Bmp, PayloadFormatOf(64));
        Assert.Equal(ImageFormat.Png, PayloadFormatOf(65));

        static ImageFormat PayloadFormatOf(int side)
        {
            using var image = TiffEncoderTests.CreateRgba(side, side);
            using var stream = new MemoryStream();
            image.Save(stream, new IcoEncoder());
            stream.Position = 0;
            using var collection = ImageCollection.Load(stream);
            return collection[0].PayloadFormat;
        }
    }

    [Fact]
    public void SeveralRepresentationsKeepTheirOwnSizeAndPixels()
    {
        using var small = TiffEncoderTests.CreateRgba(16, 16);
        using var medium = TiffEncoderTests.CreateRgba(32, 32);
        using var large = TiffEncoderTests.CreateRgba(128, 128);

        using var icon = ImageCollection.Create(ImageCollectionKind.Representations);
        icon.Add(small);
        icon.Add(medium);
        icon.Add(large);

        using var stream = new MemoryStream();
        icon.Save(stream, new IcoEncoder());
        stream.Position = 0;

        using var reloaded = ImageCollection.Load(stream);
        Assert.Equal(3, reloaded.Count);
        Assert.Equal(new Size(16, 16), reloaded[0].Size);
        Assert.Equal(new Size(32, 32), reloaded[1].Size);
        Assert.Equal(new Size(128, 128), reloaded[2].Size);
        Assert.Equal(ImageFormat.Bmp, reloaded[0].PayloadFormat);
        Assert.Equal(ImageFormat.Png, reloaded[2].PayloadFormat);

        using var decoded = (Image<Rgba32>)reloaded.SelectBySize(new Size(32, 32)).Decode();
        TiffEncoderTests.AssertSamePixels(medium, decoded);

        // The directory never becomes an animation
        using var loaded = Image.Load(stream.ToArray());
        Assert.False(loaded.IsAnimated);
        Assert.Equal(1, loaded.Frames.Count);
        Assert.Equal(new Size(128, 128), loaded.Size);
    }

    [Fact]
    public void ACursorKeepsTheHotspotOfEveryRepresentation()
    {
        using var small = TiffEncoderTests.CreateRgba(16, 16);
        using var large = TiffEncoderTests.CreateRgba(32, 32);

        using var cursor = ImageCollection.Create(ImageCollectionKind.Representations);
        cursor.Add(small, new Point(3, 4));
        cursor.Add(large, new Point(7, 8));

        using var stream = new MemoryStream();
        cursor.Save(stream, new IcoEncoder { Kind = IconKind.Cursor });
        stream.Position = 0;

        Assert.Equal(ImageFormat.Cur, Image.DetectFormat(stream.ToArray()));
        using var reloaded = ImageCollection.Load(stream);
        Assert.Equal(ImageFormat.Cur, reloaded.Format);
        Assert.Equal(new Point(3, 4), reloaded[0].Hotspot);
        Assert.Equal(new Point(7, 8), reloaded[1].Hotspot);
    }

    [Fact]
    public void AHotspotOutsideTheImageIsRejected()
    {
        using var image = TiffEncoderTests.CreateRgba(8, 8);
        using var cursor = ImageCollection.Create(ImageCollectionKind.Representations);
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Add(image, new Point(8, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Add(image, new Point(0, -1)));
    }

    [Fact]
    public void AnIconCannotStoreAHotspot()
    {
        using var image = TiffEncoderTests.CreateRgba(8, 8);
        using var icon = ImageCollection.Create(ImageCollectionKind.Representations);
        icon.Add(image, new Point(1, 1));
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => icon.Save(stream, new IcoEncoder()));
        Assert.Contains("hotspot", exception.Message, StringComparison.OrdinalIgnoreCase);

        using var discarded = new MemoryStream();
        icon.Save(discarded, new IcoEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        Assert.True(discarded.Length > 0);
    }

    [Fact]
    public void ARepresentationLargerThan256PixelsIsRejected()
    {
        using var image = TiffEncoderTests.CreateRgba(257, 16);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new IcoEncoder()));
        Assert.Contains("256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIconNeedsNoSeekableDestination()
    {
        using var image = TiffEncoderTests.CreateRgba(8, 8);
        using var stream = new TestHarness.Streams.TestOutputStream();
        image.Save(stream, new IcoEncoder());
        Assert.True(stream.BytesWritten > 0);
    }

    [Fact]
    public void TheExtensionSelectsTheIconAndCursorEncoders()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("ico-encoder-tests"));
        try
        {
            using var image = TiffEncoderTests.CreateRgba(16, 16);
            var iconPath = directory / "app.ico";
            image.Save(iconPath);
            Assert.Equal(ImageFormat.Ico, Image.DetectFormat(File.ReadAllBytes(iconPath)));

            var cursorPath = directory / "pointer.cur";
            using (var cursor = ImageCollection.Create(ImageCollectionKind.Representations))
            {
                cursor.Add(image, new Point(1, 2));
                cursor.Save(cursorPath);
            }

            Assert.Equal(ImageFormat.Cur, Image.DetectFormat(File.ReadAllBytes(cursorPath)));
            using var reloaded = ImageCollection.Load(cursorPath);
            Assert.Equal(new Point(1, 2), reloaded[0].Hotspot);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PagesCannotBeSavedAsAnIcon()
    {
        using var image = TiffEncoderTests.CreateRgba(8, 8);
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(image);
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentException>(() => pages.Save(stream, new IcoEncoder()));
    }

    [Fact]
    public void EveryPixelFormatIsWrittenThroughTheRgbaPayload()
    {
        using var gray = Image.ImportPixelData<Gray8>([new Gray8(10), new Gray8(200), new Gray8(0), new Gray8(255)], 2, 2);
        using var stream = new MemoryStream();
        gray.Save(stream, new IcoEncoder { PayloadFormat = IconPayloadFormat.Dib });
        stream.Position = 0;

        using var collection = ImageCollection.Load(stream);
        using var decoded = collection[0].Decode<Rgba32>();
        using var expected = gray.CloneAs<Rgba32>();
        TiffEncoderTests.AssertSamePixels(expected, decoded);
    }
}
