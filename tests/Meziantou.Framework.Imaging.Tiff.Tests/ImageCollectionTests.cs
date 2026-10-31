using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Streams;

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
        using var source = TestImages.CreateRgba(2, 2);
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
        using var source = TestImages.CreateRgba(2, 2);
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
        TestImages.AssertSamePixels(second, third);
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
        using var source = TestImages.CreateRgba(2, 2);
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
        using var animated = TestImages.CreateRgba(2, 2);
        animated.AppendFrame();
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        var exception = Assert.Throws<ArgumentException>(() => pages.Add(animated));
        Assert.Contains("still image", exception.Message, StringComparison.Ordinal);

        using var withPoster = TestImages.CreateRgba(2, 2);
        withPoster.SetPosterFrame(withPoster.Frames[0]);
        Assert.Throws<ArgumentException>(() => pages.Add(withPoster));
    }

    [Fact]
    public void APageCannotStoreAHotspot()
    {
        using var image = TestImages.CreateRgba(4, 4);
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(image, new Point(1, 2));
        using var stream = new MemoryStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => pages.Save(stream, new TiffEncoder()));
        Assert.Equal(ImageFormat.Tiff, exception.Format);
        Assert.Equal("Metadata: cursor hotspot", exception.Feature);

        using var discarded = new MemoryStream();
        pages.Save(discarded, new TiffEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
        discarded.Position = 0;
        using var reloaded = ImageCollection.Load(discarded);
        Assert.Null(reloaded[0].Hotspot);
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
        using var source = TestImages.CreateRgba(2, 2);
        var entry = owned.Add(source);
        using var converted = entry.Decode<Rgba64>();
        Assert.Equal(PixelFormat.Rgba64, converted.PixelFormat);
    }

    [Fact]
    public void FloatingPointEntriesAreStoredOrReducedWithThePolicyOfTheEncoder()
    {
        using var hdr = new Image<RgbaFloat>(2, 2, new RgbaFloat(0.25f, 0.5f, 2, 0.75f));
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        var entry = pages.Add(hdr);
        Assert.Equal(PixelFormat.RgbaFloat, entry.PixelFormat);
        Assert.Equal(32, entry.BitsPerComponent);

        // TIFF stores floating-point samples as they are
        using (var stored = new MemoryStream())
        {
            pages.Save(stored, new TiffEncoder());
            using var floats = Image.Load(stored.ToArray());
            Assert.Equal(PixelFormat.RgbaFloat, floats.PixelFormat);
            Assert.Equal(new RgbaFloat(0.25f, 0.5f, 2, 0.75f), ((Image<RgbaFloat>)floats).Frames[0][1, 1]);
        }

        // A palette page does not: nothing is written without the policy of the encoder, nor with a policy that rejects
        // the samples out of range
        using var opaque = new Image<RgbaFloat>(2, 2, new RgbaFloat(0.25f, 0.5f, 2));
        using var palette = ImageCollection.Create(ImageCollectionKind.Pages);
        palette.Add(opaque);
        using (var rejected = new MemoryStream())
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => palette.Save(rejected, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed }));
            Assert.Contains("entry 0", exception.Message);
            Assert.Equal(0, rejected.Length);
            Assert.Throws<UnsupportedImageFeatureException>(() => palette.Save(rejected, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed, FloatingPointConversion = new PixelConversionOptions(), MetadataHandling = MetadataHandling.DiscardUnsupported }));
        }

        using var stream = new MemoryStream();
        palette.Save(stream, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed, FloatingPointConversion = new PixelConversionOptions { OutOfRangeSamples = OutOfRangeSampleHandling.Clip }, MetadataHandling = MetadataHandling.DiscardUnsupported });
        using var decoded = Image.Load<Rgba64>(stream.ToArray());
        Assert.Equal(new Rgba64(16384, 32768, 65535), decoded.Frames[0][1, 1]);

        // Representations too
        using var icons = ImageCollection.Create(ImageCollectionKind.Representations);
        using var icon = new Image<RgbaHalf>(16, 16, new RgbaHalf(Half.One, Half.Zero, Half.Zero));
        icons.Add(icon);
        using var output = new MemoryStream();
        Assert.Throws<UnsupportedImageFeatureException>(() => icons.Save(output, new IcoEncoder()));
        icons.Save(output, new IcoEncoder { FloatingPointConversion = new PixelConversionOptions(), MetadataHandling = MetadataHandling.DiscardUnsupported });
        using var decodedIcon = Image.Load<Rgba64>(output.ToArray());
        Assert.Equal(new Rgba64(65535, 0, 0), decodedIcon.Frames[0][3, 3]);
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

    [Fact]
    public async Task EntriesOfAStreamCollectionAreDecodedConcurrentlyWithoutInterleavingReads()
    {
        using var stream = new InterleavingStream(ThreePages()) { GateTimeout = TimeSpan.FromSeconds(1) };
        using var collection = ImageCollection.Load(stream);
        var first = collection[0];
        var second = collection[1];

        // The first decode stops between its seek and its read; the second one then gets the gate timeout to slip in
        stream.Arm();
        var firstDecode = StartOnDedicatedThread(() => DecodePixels(first));
        Assert.True(stream.GateEntered.Wait(TimeSpan.FromSeconds(30)));
        var secondDecode = StartOnDedicatedThread(() => DecodePixels(second));

        Assert.Equal([new Gray8(1)], await firstDecode);
        Assert.Equal([new Gray8(2)], await secondDecode);
        Assert.False(stream.Interleaved);
    }

    [Fact]
    public async Task DisposingAStreamCollectionWaitsForTheReadInProgress()
    {
        using var stream = new InterleavingStream(ThreePages()) { GateTimeout = TimeSpan.FromSeconds(30) };
        var collection = ImageCollection.Load(stream);
        var entry = collection[0];

        stream.Arm();
        var decode = StartOnDedicatedThread(() =>
        {
            try
            {
                return DecodePixels(entry);
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        });

        Assert.True(stream.GateEntered.Wait(TimeSpan.FromSeconds(30)));
        var dispose = StartOnDedicatedThread(() =>
        {
            collection.Dispose();
            return true;
        });

        await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.False(dispose.IsCompleted);

        stream.Release();
        await dispose;
        var readsWhenDisposed = stream.ReadCount;

        // The decode either finished with the read in progress or failed on its next one, without touching the stream
        var pixels = await decode;
        if (pixels is not null)
        {
            Assert.Equal([new Gray8(1)], pixels);
        }

        Assert.Equal(readsWhenDisposed, stream.ReadCount);
        Assert.False(stream.Interleaved);
        Assert.False(stream.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => entry.Decode());
    }

    [Fact]
    public void EntriesOfAFileCollectionAreDecodedConcurrently()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("image-collection-tests"));
        try
        {
            const int PageCount = 8;
            var builder = new TiffFileBuilder();
            var expected = new Gray8[PageCount][];
            for (var page = 0; page < PageCount; page++)
            {
                var width = page + 3;
                var samples = new byte[width * 4];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = (byte)((page * 31) + i);
                }

                builder.AddPage(new TiffPageSpec { Width = width, Height = 4, RowsPerStrip = 1, Samples = samples });
                expected[page] = [.. samples.Select(value => new Gray8(value))];
            }

            var path = directory / "document.tif";
            File.WriteAllBytes(path, builder.Build());
            using var collection = ImageCollection.Load(path);
            var entries = collection.Entries.ToArray();
            var results = new Gray8[PageCount * 16][];
            Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => results[i] = DecodePixels(entries[i % PageCount]));

            for (var i = 0; i < results.Length; i++)
            {
                Assert.Equal(expected[i % PageCount], results[i]);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ACanceledDecodeReturnsNothingAndLeavesTheEntryDecodable()
    {
        using var collection = ImageCollection.Load(StripedPage());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => collection[0].Decode(options: null, canceled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => collection[0].Decode<Gray8>(cancellationToken: canceled.Token));

        // An entry that owns an image observes the token too, before it is copied
        using var source = TestImages.CreateRgba(2, 2);
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(source);
        Assert.ThrowsAny<OperationCanceledException>(() => pages[0].Decode(options: null, canceled.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => pages[0].Decode<Rgba32>(options: null, canceled.Token));

        // The pixel type is validated before the token, and the entries stay usable
        Assert.Throws<NotSupportedException>(() => collection[0].Decode<Guid>(options: null, canceled.Token));
        Assert.Equal(StripedPagePixels(), DecodePixels(collection[0]));
        using var copy = pages[0].Decode(options: null, XunitCancellationToken);
        Assert.Equal(new Size(2, 2), copy.Size);
    }

    [Fact]
    public void ADecodeObservesACancellationRequestedWhileItReads()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new ReadHookStream(StripedPage());
        using var collection = ImageCollection.Load(stream);

        // The token is canceled by the first read of the decode, after the decode checked it on entry
        var start = stream.ReadCount;
        stream.OnRead = count =>
        {
            if (count == start + 1)
            {
                cancellation.Cancel();
            }
        };

        Assert.ThrowsAny<OperationCanceledException>(() => collection[0].Decode(options: null, cancellation.Token));
        Assert.Equal(start + 1, stream.ReadCount); // stopped at the next boundary of the decoder (a strip), without reading again

        stream.OnRead = null;
        Assert.Equal(StripedPagePixels(), DecodePixels(collection[0]));
    }

    [Fact]
    public async Task SaveAsyncWritesTheBytesOfSave()
    {
        using var loaded = ImageCollection.Load(ThreePages());
        using var source = TestImages.CreateRgba(3, 2);
        loaded.Add(source);
        using var icons = ImageCollection.Create(ImageCollectionKind.Representations);
        icons.Add(source);
        using var larger = TestImages.CreateRgba(5, 5);
        icons.Add(larger);

        foreach (var (collection, encoder, extension) in new[] { (loaded, (ImageEncoder)new TiffEncoder(), ".tif"), (icons, new IcoEncoder(), ".ico"), (icons, new IcoEncoder { Kind = IconKind.Cursor }, ".cur") })
        {
            using var expected = new MemoryStream();
            collection.Save(expected, encoder);

            using var output = new TestOutputStream { Seekable = true, AllowPatching = true, ForbidSynchronousWrites = true };
            await collection.SaveAsync(output, encoder, XunitCancellationToken);
            Assert.Equal(expected.ToArray(), output.ToArray());
            Assert.False(output.IsDisposed);
            Assert.True(output.AsynchronousWriteCount > 0);
            Assert.True(output.FlushCount > 0);

            var directory = TestFiles.CreateDirectory();
            try
            {
                var path = directory / ("collection" + extension);
                await collection.SaveAsync(path, cancellationToken: XunitCancellationToken);
                Assert.Equal(expected.ToArray(), await File.ReadAllBytesAsync(path, XunitCancellationToken));
                Assert.Equal([path], Directory.GetFiles(directory));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveAsyncValidatesItsArgumentsBeforeReturning()
    {
        using var empty = ImageCollection.Create(ImageCollectionKind.Pages);
        using var source = TestImages.CreateRgba(2, 2);
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        pages.Add(source);
        var directory = TestFiles.CreateDirectory();
        try
        {
            // Thrown by the call, not by the returned task, like the synchronous Save
            Assert.Throws<InvalidOperationException>(() => { _ = empty.SaveAsync(new MemoryStream(), new TiffEncoder(), XunitCancellationToken); });
            Assert.Throws<InvalidOperationException>(() => { _ = empty.SaveAsync(directory / "a.tif", cancellationToken: XunitCancellationToken); });
            Assert.Throws<ArgumentException>("path", () => { _ = pages.SaveAsync("", cancellationToken: XunitCancellationToken); });
            Assert.Throws<ArgumentException>("path", () => { _ = pages.SaveAsync(directory / "a.xyz", cancellationToken: XunitCancellationToken); });
            Assert.Throws<ArgumentException>("encoder", () => { _ = pages.SaveAsync(directory / "a.png", cancellationToken: XunitCancellationToken); });
            Assert.Throws<ArgumentException>("encoder", () => { _ = pages.SaveAsync(directory / "a.ico", cancellationToken: XunitCancellationToken); });
            Assert.Throws<ArgumentException>("encoder", () => { _ = pages.SaveAsync(new TestOutputStream(), new TiffEncoder(), XunitCancellationToken); });
            Assert.Throws<ArgumentException>("stream", () => { _ = pages.SaveAsync(new MemoryStream([], writable: false), new TiffEncoder(), XunitCancellationToken); });
            Assert.Throws<ArgumentNullException>("stream", () => { _ = pages.SaveAsync((Stream)null!, new TiffEncoder(), XunitCancellationToken); });
            Assert.Throws<ArgumentNullException>("encoder", () => { _ = pages.SaveAsync(new MemoryStream(), null!, XunitCancellationToken); });
            Assert.Empty(Directory.GetFileSystemEntries(directory));

            pages.Dispose();
            Assert.Throws<ObjectDisposedException>(() => { _ = pages.SaveAsync(directory / "a.tif", cancellationToken: XunitCancellationToken); });
            Assert.Throws<ObjectDisposedException>(() => { _ = pages.SaveAsync(new MemoryStream(), new TiffEncoder(), XunitCancellationToken); });

            // A pre-canceled token creates no output
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            using var other = ImageCollection.Create(ImageCollectionKind.Pages);
            other.Add(source);
            Task? task = null;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task = other.SaveAsync(directory / "a.tif", cancellationToken: canceled.Token));
            Assert.True(task!.IsCanceled);
            using var output = new TestOutputStream { Seekable = true, AllowPatching = true };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => other.SaveAsync(output, new TiffEncoder(), canceled.Token));
            Assert.Equal(0, output.BytesWritten);
            Assert.Empty(Directory.GetFileSystemEntries(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AFailedOrCanceledSaveAsyncLeavesNoPartialFile(bool cancel)
    {
        var directory = TestFiles.CreateDirectory();
        try
        {
            var path = directory / "document.tif";
            await File.WriteAllTextAsync(path, "previous content", XunitCancellationToken);
            using var cancellation = new CancellationTokenSource();
            using var stream = new ReadHookStream(ThreePages());
            using var collection = ImageCollection.Load(stream);

            // The input fails or the token is canceled while the second page is read: the first one is already written
            var start = stream.ReadCount;
            var reads = 0;
            stream.OnRead = _ =>
            {
                if (++reads == 2)
                {
                    if (cancel)
                    {
                        cancellation.Cancel();
                    }
                    else
                    {
                        throw new IOException("Injected read failure.");
                    }
                }
            };

            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => collection.SaveAsync(path, cancellationToken: cancellation.Token));
            }
            else
            {
                await Assert.ThrowsAsync<IOException>(() => collection.SaveAsync(path, cancellationToken: XunitCancellationToken));
            }

            Assert.True(stream.ReadCount > start);
            Assert.Equal("previous content", await File.ReadAllTextAsync(path, XunitCancellationToken));
            Assert.Equal([path], Directory.GetFiles(directory));

            // The collection is unchanged and can be saved again
            stream.OnRead = null;
            await collection.SaveAsync(path, cancellationToken: XunitCancellationToken);
            using var saved = ImageCollection.Load(path);
            Assert.Equal(3, saved.Count);
            Assert.Equal([new Gray8(2)], DecodePixels(saved[1]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ACanceledStreamSaveAsyncStopsWriting()
    {
        using var cancellation = new CancellationTokenSource();
        using var image = new Image<Gray8>(256, 256, new Gray8(7));
        using var pages = ImageCollection.Create(ImageCollectionKind.Pages);
        for (var i = 0; i < 4; i++)
        {
            pages.Add(image);
        }

        var encoder = new TiffEncoder { Compression = TiffCompression.None, RowsPerStrip = 1 };
        using var expected = new MemoryStream();
        pages.Save(expected, encoder);

        // Each page stores 64 KB of uncompressed samples, so the output is flushed during every page: the token is canceled
        // by the first write, and the save stops before the remaining pages
        using var output = new TestOutputStream { Seekable = true, AllowPatching = true, CancellationSource = cancellation, CancelAtPosition = 1 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pages.SaveAsync(output, encoder, cancellation.Token));
        Assert.InRange(output.BytesWritten, 1, expected.Length / 2);
        Assert.False(output.IsDisposed);
    }

    private static Gray8[] DecodePixels(ImageCollectionEntry entry)
    {
        using var image = entry.Decode<Gray8>();
        var pixels = new Gray8[image.Width * image.Height];
        image.Frames[0].CopyPixelDataTo(pixels);
        return pixels;
    }

    private static Task<T> StartOnDedicatedThread<T>(Func<T> func)
        => Task.Factory.StartNew(func, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static byte[] TwoPixelPage() => new TiffFileBuilder()
        .AddPage(new TiffPageSpec { Width = 2, Height = 1, Samples = [1, 2] })
        .Build();

    private const int StripedPageWidth = 4096;

    private const int StripedPageHeight = 16;

    private static byte[] StripedPage() => new TiffFileBuilder()
        .AddPage(new TiffPageSpec { Width = StripedPageWidth, Height = StripedPageHeight, RowsPerStrip = 1, Samples = StripedPagePixels().Select(static pixel => pixel.Value).ToArray() })
        .Build();

    private static Gray8[] StripedPagePixels() => Enumerable.Range(0, StripedPageWidth * StripedPageHeight).Select(static i => new Gray8((byte)(i * 7))).ToArray();

    private static byte[] ThreePages()
    {
        var builder = new TiffFileBuilder();
        for (var i = 0; i < 3; i++)
        {
            builder.AddPage(new TiffPageSpec { Width = 1, Height = 1, Samples = [(byte)(i + 1)] });
        }

        return builder.Build();
    }

    /// <summary>
    /// A seekable stream that stops the first thread to seek once it is armed, until another thread touches the stream,
    /// <see cref="Release"/> is called, or the gate timeout elapses. Any access by another thread while that thread is
    /// between its seek and the end of its read is recorded as an interleaving.
    /// </summary>
    private sealed class InterleavingStream(byte[] data) : Stream
    {
        private readonly ManualResetEventSlim _release = new();
        private long _position;
        private int _gateThreadId = -1;
        private int _readCount;
        private volatile bool _interleaved;
        private volatile bool _disposed;

        public required TimeSpan GateTimeout { get; init; }

        public ManualResetEventSlim GateEntered { get; } = new();

        public bool Interleaved => _interleaved;

        public bool IsDisposed => _disposed;

        public int ReadCount => Volatile.Read(ref _readCount);

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set
            {
                CheckInterleaving();
                _position = value;
                if (Interlocked.CompareExchange(ref _gateThreadId, Environment.CurrentManagedThreadId, 0) is 0)
                {
                    GateEntered.Set();
                    _release.Wait(GateTimeout);
                }
            }
        }

        public void Arm() => Volatile.Write(ref _gateThreadId, 0);

        public void Release() => _release.Set();

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            CheckInterleaving();
            Interlocked.Increment(ref _readCount);
            var count = (int)Math.Min(buffer.Length, Math.Max(0, data.Length - _position));
            data.AsSpan((int)_position, count).CopyTo(buffer);
            _position += count;

            // The gate holds for one seek-then-read operation only
            Interlocked.CompareExchange(ref _gateThreadId, -1, Environment.CurrentManagedThreadId);
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                _release.Dispose();
                GateEntered.Dispose();
            }

            base.Dispose(disposing);
        }

        private void CheckInterleaving()
        {
            var gateThreadId = Volatile.Read(ref _gateThreadId);
            if (gateThreadId > 0 && gateThreadId != Environment.CurrentManagedThreadId)
            {
                _interleaved = true;
                _release.Set();
            }
        }
    }

    /// <summary>A seekable in-memory input that counts its reads and calls a hook before each one.</summary>
    private sealed class ReadHookStream(byte[] data) : Stream
    {
        private long _position;
        private int _readCount;

        public Action<int>? OnRead { get; set; }

        public int ReadCount => Volatile.Read(ref _readCount);

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => data.Length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            OnRead?.Invoke(Interlocked.Increment(ref _readCount));
            var count = (int)Math.Min(buffer.Length, Math.Max(0, data.Length - _position));
            data.AsSpan((int)_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => _position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => data.Length + offset,
        };

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
