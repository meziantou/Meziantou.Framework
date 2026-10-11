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

    [Theory]
    [InlineData(IconKind.Icon)]
    [InlineData(IconKind.Cursor)]
    public void SixteenBitPixelsAreWrittenAsAPngWhateverTheirSize(IconKind kind)
    {
        // A DIB stores 8-bit samples: Auto never narrows, even below the size from which it otherwise writes a PNG
        Rgba64[] pixels = [new Rgba64(1, 2, 3, 65535), new Rgba64(65535, 32768, 257, 4660), new Rgba64(0, 0, 0, 0), new Rgba64(513, 1027, 2056, 1)];
        using var image = Image.ImportPixelData<Rgba64>(pixels, 2, 2);
        using var stream = new MemoryStream();
        image.Save(stream, new IcoEncoder { Kind = kind });

        // The payload of the only entry starts right after the 22-byte directory
        Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], stream.ToArray()[22..30]);
        stream.Position = 0;
        using var decoded = Image.Load(stream);
        var typed = Assert.IsType<Image<Rgba64>>(decoded);
        var actual = new Rgba64[4];
        typed.Frames[0].CopyPixelDataTo(actual);
        Assert.Equal(pixels, actual);

        // The same pixels with 8-bit samples keep the DIB payload of small representations
        using var narrow = image.CloneAs<Rgba32>();
        using var dib = new MemoryStream();
        narrow.Save(dib, new IcoEncoder { Kind = kind });
        Assert.Equal([40, 0, 0, 0], dib.ToArray()[22..26]);
    }

    [Fact]
    public void SixteenBitGrayPixelsKeepTheirPrecisionToo()
    {
        Gray16[] pixels = [new Gray16(0), new Gray16(1), new Gray16(32768), new Gray16(65535)];
        using var image = Image.ImportPixelData<Gray16>(pixels, 4, 1);
        using var stream = new MemoryStream();
        image.Save(stream, new IcoEncoder());
        stream.Position = 0;
        using var decoded = Image.Load(stream);
        var typed = Assert.IsType<Image<Gray16>>(decoded);
        var actual = new Gray16[4];
        typed.Frames[0].CopyPixelDataTo(actual);
        Assert.Equal(pixels, actual);
    }

    [Theory]
    [InlineData(PixelFormat.Rgba64)]
    [InlineData(PixelFormat.Gray16)]
    public void ADibPayloadIsRejectedForSixteenBitPixelsBeforeAnyOutput(PixelFormat pixelFormat)
    {
        using Image image = pixelFormat == PixelFormat.Rgba64 ? new Image<Rgba64>(4, 4) : new Image<Gray16>(4, 4);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new IcoEncoder { PayloadFormat = IconPayloadFormat.Dib }));
        Assert.Equal(ImageFormat.Ico, exception.Format);
        Assert.Equal("Bit depth reduction", exception.Feature);
        Assert.Contains(pixelFormat.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, stream.Length);

        // A sequential writer reports it for the frame, before writing anything of it
        using (var writer = pixelFormat == PixelFormat.Rgba64
            ? (IDisposable)Image.CreateWriter<Rgba64>(stream, new ImageWriterOptions(4, 4) { Encoder = new IcoEncoder { PayloadFormat = IconPayloadFormat.Dib }, LeaveOpen = true })
            : Image.CreateWriter<Gray16>(stream, new ImageWriterOptions(4, 4) { Encoder = new IcoEncoder { PayloadFormat = IconPayloadFormat.Dib }, LeaveOpen = true }))
        {
            Assert.Equal("Bit depth reduction", Assert.Throws<UnsupportedImageFeatureException>(() => WriteFirstFrame(writer, image)).Feature);
        }

        Assert.Equal(0, stream.Length);

        // The representations of a collection follow the same rule, and nothing is written when a later one fails
        using var small = TiffEncoderTests.CreateRgba(8, 8);
        using var cursor = ImageCollection.Create(ImageCollectionKind.Representations);
        cursor.Add(small);
        cursor.Add(image);
        var collection = Assert.Throws<UnsupportedImageFeatureException>(() => cursor.Save(stream, new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = IconPayloadFormat.Dib }));
        Assert.Equal(ImageFormat.Cur, collection.Format);
        Assert.Equal("Bit depth reduction", collection.Feature);
        Assert.Equal(0, stream.Length);

        // Auto and Png store both representations, each with its own payload
        cursor.Save(stream, new IcoEncoder { Kind = IconKind.Cursor });
        stream.Position = 0;
        using var reloaded = ImageCollection.Load(stream);
        Assert.Equal([ImageFormat.Bmp, ImageFormat.Png], reloaded.Entries.Select(entry => entry.PayloadFormat));
        Assert.Equal(pixelFormat, reloaded[1].PixelFormat);

        static void WriteFirstFrame(IDisposable writer, Image image)
        {
            if (writer is ImageWriter<Rgba64> color)
            {
                color.WriteFrame(((Image<Rgba64>)image).Frames[0]);
            }
            else
            {
                ((ImageWriter<Gray16>)writer).WriteFrame(((Image<Gray16>)image).Frames[0]);
            }
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

    [Theory]
    [InlineData(16, IconPayloadFormat.Dib)]
    [InlineData(16, IconPayloadFormat.Png)]
    [InlineData(128, IconPayloadFormat.Auto)] // a PNG payload, written by a nested PNG encode that must not see the hotspot
    public void SavingAnImageAsACursorWritesTheHotspotOfItsFrame(int side, IconPayloadFormat payloadFormat)
    {
        using var image = TiffEncoderTests.CreateRgba(side, side);
        image.Frames[0].Metadata.Hotspot = new Point(3, 4);
        using var stream = new MemoryStream();
        image.Save(stream, new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = payloadFormat });

        // ICONDIRENTRY of a cursor: the hotspot X and Y are the two 16-bit fields after the four size and color bytes
        Assert.Equal([3, 0, 4, 0], stream.ToArray()[10..14]);
        stream.Position = 0;
        using var reloaded = Image.Load<Rgba32>(stream);
        Assert.Equal(new Point(3, 4), reloaded.Frames[0].Metadata.Hotspot);
        TiffEncoderTests.AssertSamePixels(image, reloaded);

        // A cursor always has a hotspot: the top-left corner when the frame has none, and whatever the metadata policy is
        image.Frames[0].Metadata.Hotspot = null;
        using var none = new MemoryStream();
        image.Save(none, new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = payloadFormat });
        Assert.Equal([0, 0, 0, 0], none.ToArray()[10..14]);

        image.Frames[0].Metadata.Hotspot = new Point(side - 1, side - 1);
        using var stripped = new MemoryStream();
        image.Save(stripped, new IcoEncoder { Kind = IconKind.Cursor, PayloadFormat = payloadFormat, MetadataHandling = MetadataHandling.Strip });
        Assert.Equal([(byte)(side - 1), 0, (byte)(side - 1), 0], stripped.ToArray()[10..14]);
    }

    [Theory]
    [InlineData(IconKind.Icon)]
    [InlineData(IconKind.None)]
    public void AnIconRejectsTheHotspotOfAFrameUnlessThePolicyDiscardsIt(IconKind kind)
    {
        using var image = TiffEncoderTests.CreateRgba(8, 8);
        image.Frames[0].Metadata.Hotspot = new Point(1, 1);
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new IcoEncoder { Kind = kind }));
        Assert.Equal(ImageFormat.Ico, exception.Format);
        Assert.Equal("Metadata: cursor hotspot", exception.Feature);
        Assert.Equal(0, stream.Length);

        // The same rule applies to the entries of a collection
        using var icon = ImageCollection.Create(ImageCollectionKind.Representations);
        icon.Add(image);
        Assert.Equal("Metadata: cursor hotspot", Assert.Throws<UnsupportedImageFeatureException>(() => icon.Save(stream, new IcoEncoder { Kind = kind })).Feature);
        Assert.Equal(0, stream.Length);

        foreach (var handling in (MetadataHandling[])[MetadataHandling.DiscardUnsupported, MetadataHandling.Strip])
        {
            using var discarded = new MemoryStream();
            image.Save(discarded, new IcoEncoder { Kind = kind, MetadataHandling = handling });

            // An icon entry stores one color plane and the bit count where a cursor stores its hotspot
            Assert.Equal([1, 0, 32, 0], discarded.ToArray()[10..14]);
            discarded.Position = 0;
            using var reloaded = Image.Load(discarded);
            Assert.Null(reloaded.Frames[0].Metadata.Hotspot);
        }
    }

    [Fact]
    public void AnEntryTakesTheHotspotOfItsImageUnlessOneIsGiven()
    {
        using var image = TiffEncoderTests.CreateRgba(8, 8);
        image.Frames[0].Metadata.Hotspot = new Point(2, 3);
        using var cursor = ImageCollection.Create(ImageCollectionKind.Representations);
        Assert.Equal(new Point(2, 3), cursor.Add(image).Hotspot);
        Assert.Equal(new Point(5, 6), cursor.Add(image, new Point(5, 6)).Hotspot);
        Assert.Equal(new Point(7, 7), cursor.Insert(0, image, new Point(7, 7)).Hotspot);

        // The image of an entry and the entry agree, and the caller's image is not touched
        using (var decoded = cursor[2].Decode())
        {
            Assert.Equal(new Point(5, 6), decoded.Frames[0].Metadata.Hotspot);
        }

        Assert.Equal(new Point(2, 3), image.Frames[0].Metadata.Hotspot);

        using var stream = new MemoryStream();
        cursor.Save(stream, new IcoEncoder { Kind = IconKind.Cursor });
        stream.Position = 0;
        using var reloaded = ImageCollection.Load(stream);
        Assert.Equal([new Point(7, 7), new Point(2, 3), new Point(5, 6)], reloaded.Entries.Select(entry => entry.Hotspot));
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
