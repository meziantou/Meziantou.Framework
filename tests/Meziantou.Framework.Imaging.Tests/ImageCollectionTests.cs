using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The collection model shared by TIFF pages and icon representations (used by the TIFF and ICO/CUR codecs): entries that may
/// differ in size, pixel format and metadata, owned versus borrowed lifetimes, editing, extraction into an independently
/// owned <see cref="Image"/>, and the separation from the animation model of <see cref="Image"/>.
/// </summary>
public sealed class ImageCollectionTests
{
    [Fact]
    public void AnEmptyCollectionIsCreatedForOneKind()
    {
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        Assert.Equal(ImageCollectionKind.Pages, pages.Kind);
        Assert.Equal(ImageFormat.Unknown, pages.Format);
        Assert.Equal(0, pages.Count);
        Assert.Empty(pages.Entries);
        Assert.Same(ImageConfiguration.Default, pages.Configuration);
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageCollection.Create(ImageCollectionKind.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageCollection.Create((ImageCollectionKind)42));
        Assert.Throws<InvalidOperationException>(() => pages.SelectBySize(new Size(1, 1)));
    }

    [Fact]
    public void AddedImagesAreCopiedAndTheCallerKeepsItsOwn()
    {
        using var source = TiffEncoderTests.CreateRgba(2, 2);
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        var entry = pages.Add(source);
        Assert.Equal(new Size(2, 2), entry.Size);
        Assert.Equal(PixelFormat.Rgba32, entry.PixelFormat);
        Assert.Equal(ImageColorModel.Rgba, entry.ColorModel);
        Assert.Equal(8, entry.BitsPerComponent);
        Assert.True(entry.MayHaveTransparency);
        Assert.Equal(ImageFormat.Unknown, entry.PayloadFormat);
        Assert.Null(entry.Hotspot);

        // The collection holds a copy: disposing the caller's image leaves the entry usable
        source.Dispose();
        using var decoded = (Image<Rgba32>)entry.Decode();
        Assert.Equal(new Size(2, 2), decoded.Size);
    }

    [Fact]
    public void DecodingAnEntryReturnsAnIndependentlyOwnedImage()
    {
        using var source = TiffEncoderTests.CreateRgba(2, 2);
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        var entry = pages.Add(source);

        using var first = (Image<Rgba32>)entry.Decode();
        using var second = (Image<Rgba32>)entry.Decode();
        Assert.NotSame(first, second);

        // Editing one extracted image changes neither the entry nor the other extraction
        first.Frames[0].ProcessPixelRows(static rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                rows.GetRowSpan(y).Fill(new Rgba32(255, 0, 0));
            }
        });

        using var third = (Image<Rgba32>)entry.Decode();
        TiffEncoderTests.AssertSamePixels(second, third);
    }

    [Fact]
    public void EntriesCanBeInsertedMovedAndRemoved()
    {
        using var a = Image.ImportPixelData<Gray8>([new Gray8(1)], 1, 1);
        using var b = Image.ImportPixelData<Gray8>([new Gray8(2)], 1, 1);

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        var first = pages.Add(a);
        var second = pages.Insert(0, b);
        Assert.Equal(2, pages.Count);
        Assert.Same(second, pages[0]);
        Assert.Same(first, pages[1]);

        pages.Move(0, 1);
        Assert.Same(first, pages[0]);
        Assert.Same(second, pages[1]);

        pages.RemoveAt(0);
        Assert.Equal(1, pages.Count);
        Assert.Same(second, pages[0]);

        // A removed entry released the image it owned
        Assert.Throws<ObjectDisposedException>(() => first.Decode());

        Assert.Throws<ArgumentOutOfRangeException>(() => pages[1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => pages.RemoveAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pages.Insert(2, a));
        Assert.Throws<ArgumentOutOfRangeException>(() => pages.Move(0, 1));
    }

    [Fact]
    public void EntriesKeepIndependentMetadata()
    {
        using var a = Image.ImportPixelData<Gray8>([new Gray8(1)], 1, 1);
        a.Metadata.Resolution = new ImageResolution(100, 100);
        using var b = Image.ImportPixelData<Gray8>([new Gray8(2)], 1, 1);

        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        var first = pages.Add(a);
        var second = pages.Add(b);
        Assert.Equal(100, first.Metadata.Resolution!.HorizontalDpi);
        Assert.Null(second.Metadata.Resolution);

        // The entry holds its own copy of the metadata of the image that was added
        a.Metadata.Resolution = new ImageResolution(300, 300);
        Assert.Equal(100, first.Metadata.Resolution!.HorizontalDpi);
    }

    [Fact]
    public void DisposingTheCollectionReleasesItsEntriesButNotTheExtractedImages()
    {
        using var source = TiffEncoderTests.CreateRgba(2, 2);
        var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        var entry = pages.Add(source);
        var extracted = (Image<Rgba32>)entry.Decode();
        pages.Dispose();

        Assert.Throws<ObjectDisposedException>(() => pages.Count);
        Assert.Throws<ObjectDisposedException>(() => pages.Entries);
        Assert.Throws<ObjectDisposedException>(() => pages[0]);
        Assert.Throws<ObjectDisposedException>(() => entry.Decode());

        // The extracted image is owned by the caller and is untouched
        Assert.Equal(new Size(2, 2), extracted.Size);
        extracted.Dispose();

        // Disposing twice is a no-op
        pages.Dispose();
    }

    [Fact]
    public void AnimatedImagesAndPosterFramesAreNotPagesOrRepresentations()
    {
        using var animated = TiffEncoderTests.CreateRgba(2, 2);
        animated.AppendFrame();
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        var exception = Assert.Throws<ArgumentException>(() => pages.Add(animated));
        Assert.Contains("still image", exception.Message, StringComparison.Ordinal);

        using var withPoster = TiffEncoderTests.CreateRgba(2, 2);
        withPoster.SetPosterFrame(withPoster.Frames[0]);
        Assert.Throws<ArgumentException>(() => pages.Add(withPoster));
    }

    [Fact]
    public void ADecodedEntryIsNeverAnAnimation()
    {
        using var collection = ImageCollection.Load(ThreePages());
        foreach (var entry in collection.Entries)
        {
            using var image = entry.Decode();
            Assert.False(image.IsAnimated);
            Assert.Null(image.Animation);
            Assert.Null(image.PosterFrame);
            Assert.Equal(1, image.Frames.Count);
            Assert.Equal(FrameDuration.Zero, image.Frames[0].Metadata.Duration);
        }
    }

    [Fact]
    public void AnEntryCanBeDecodedIntoARequestedPixelType()
    {
        using var collection = ImageCollection.Load(ThreePages());
        using var gray = collection[0].Decode<Gray8>();
        Assert.Equal(PixelFormat.Gray8, gray.PixelFormat);

        using var rgba = collection[0].Decode<Rgba32>();
        Assert.Equal(PixelFormat.Rgba32, rgba.PixelFormat);

        using var owned = ImageCollection.Create(ImageCollectionKind.Pages);
        using var source = TiffEncoderTests.CreateRgba(2, 2);
        var entry = owned.Add(source);
        using var converted = entry.Decode<Rgba64>();
        Assert.Equal(PixelFormat.Rgba64, converted.PixelFormat);
    }

    [Fact]
    public void ANonSeekableStreamIsRejectedWithAnActionableMessage()
    {
        using var inner = new MemoryStream(ThreePages());
        using var stream = new TestHarness.Streams.TestInputStream(inner.ToArray()) { Seekable = false };
        var exception = Assert.Throws<ArgumentException>(() => ImageCollection.Load(stream));
        Assert.Contains("seekable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EachEntryDecodeGetsItsOwnResourceBudget()
    {
        // Three one-pixel pages with a per-input budget of one frame: decoding them one by one must all succeed,
        // because the limits of an entry are never consumed by the entries decoded before it
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 3, MaxTotalPixels = 1 } };
        using var collection = ImageCollection.Load(ThreePages(), configuration);
        for (var i = 0; i < collection.Count; i++)
        {
            using var image = collection[i].Decode(new ImageDecodeOptions { Configuration = configuration });
            Assert.Equal(1, image.Frames.Count);
        }
    }

    [Fact]
    public void AnEntryDecodedWithoutOptionsUsesTheConfigurationOfItsCollection()
    {
        // The limits a caller sets on Load also apply to every entry it extracts afterward
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 1, MaxHeight = 1 } };
        using var collection = ImageCollection.Load(ThreePages(), configuration);
        using (var page = collection[0].Decode())
        {
            Assert.Equal(new Size(1, 1), page.Size);
        }

        var strict = new ImageConfiguration { Limits = new ImageResourceLimits { MaxTotalPixels = 1 } };
        using var bounded = ImageCollection.Load(TwoPixelPage(), strict);
        Assert.Throws<ImageResourceLimitException>(() => bounded[0].Decode());

        // Explicit options still win
        using var decoded = bounded[0].Decode(ImageDecodeOptions.Default);
        Assert.Equal(new Size(2, 1), decoded.Size);
    }

    [Fact]
    public void LoadingFromAFileKeepsItOpenUntilTheCollectionIsDisposed()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("image-collection-tests"));
        try
        {
            var path = directory / "document.tif";
            File.WriteAllBytes(path, ThreePages());
            var collection = ImageCollection.Load(path);
            try
            {
                using var page = collection[1].Decode();
                Assert.Equal(new Size(1, 1), page.Size);
            }
            finally
            {
                collection.Dispose();
            }

            // The file is released once the collection is disposed
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] TwoPixelPage() => new TiffFileBuilder()
        .AddPage(new TiffPageSpec { Width = 2, Height = 1, Samples = [1, 2] })
        .Build();

    private static byte[] ThreePages()
    {
        var builder = new TiffFileBuilder();
        for (var i = 0; i < 3; i++)
        {
            builder.AddPage(new TiffPageSpec { Width = 1, Height = 1, Samples = [(byte)(i + 1)] });
        }

        return builder.Build();
    }
}
