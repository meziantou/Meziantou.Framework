using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Owned images, borrowed frames, frame collection edits, posters and animation settings.</summary>
public sealed class ImageModelTests
{
    private static readonly Rgba32 Red = new(255, 0, 0);
    private static readonly Rgba32 Green = new(0, 255, 0);
    private static readonly Rgba32 Blue = new(0, 0, 255);

    [Fact]
    public void BlankImageIsAZeroedStillImage()
    {
        using var image = new Image<Rgba32>(3, 2);
        Assert.Equal(3, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(new Size(3, 2), image.Size);
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
        Assert.Same(ImageConfiguration.Default, image.Configuration);
        Assert.NotNull(image.Metadata);
        Assert.Single(image.Frames);
        Assert.Null(image.PosterFrame);
        Assert.Null(image.Animation);
        Assert.False(image.IsAnimated);

        var frame = image.Frames[0];
        Assert.Equal(new Size(3, 2), frame.Size);
        Assert.Equal(PixelFormat.Rgba32, frame.PixelFormat);
        Assert.Equal(FrameDuration.Zero, frame.Metadata.Duration);
        Assert.Equal(default, frame[2, 1]);
    }

    [Fact]
    public void FillConstructorSetsEveryPixel()
    {
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = 2 };
        using var image = new Image<Rgb24>(5, 3, new Rgb24(1, 2, 3), configuration);
        Assert.Same(configuration, image.Configuration);
        var pixels = new Rgb24[15];
        image.Frames[0].CopyPixelDataTo(pixels);
        Assert.All(pixels, pixel => Assert.Equal(new Rgb24(1, 2, 3), pixel));
    }

    [Fact]
    public void TypedAndUntypedViewsShareTheSameFrameObjects()
    {
        using var image = new Image<Gray16>(2, 2);
        image.AppendFrame();
        image.SetPosterFrame(image.Frames[0]);
        Image untyped = image;

        Assert.Same(image.Frames, untyped.Frames);
        Assert.Same(image.PosterFrame, untyped.PosterFrame);
        Assert.Equal(2, untyped.Frames.Count);
        for (var i = 0; i < image.Frames.Count; i++)
        {
            Assert.Same(image.Frames[i], untyped.Frames[i]);
            Assert.Same(image.Frames[i], image.Frames[i]);
        }

        var typed = new List<ImageFrame<Gray16>>();
        foreach (var frame in image.Frames)
        {
            typed.Add(frame);
        }

        var viaUntyped = new List<ImageFrame>();
        foreach (var frame in untyped.Frames)
        {
            viaUntyped.Add(frame);
        }

        Assert.Equal(typed.Cast<ImageFrame>(), viaUntyped, ReferenceEqualityComparer.Instance);
        Assert.Equal(viaUntyped, ((IEnumerable<ImageFrame>)untyped.Frames).ToList(), ReferenceEqualityComparer.Instance);
        Assert.Equal(1, untyped.Frames.IndexOf(image.Frames[1]));
        Assert.Equal(-1, untyped.Frames.IndexOf(image.PosterFrame!));
        Assert.False(untyped.Frames.Contains(image.PosterFrame!));
    }

    [Fact]
    public void AppendBlankFrameCreatesAnimationSettings()
    {
        using var image = new Image<Rgba32>(2, 2, Red);
        var frame = image.AppendFrame();
        Assert.Same(frame, image.Frames[1]);
        Assert.Equal(default, frame[1, 1]);
        Assert.Equal(FrameDuration.Zero, frame.Metadata.Duration);
        Assert.NotNull(image.Animation);
        Assert.Null(image.Animation.TotalPlays);
        Assert.True(image.IsAnimated);
    }

    [Fact]
    public void AppendedAndInsertedFramesAreIndependentCopies()
    {
        using var source = new Image<Rgba32>(2, 2, Green);
        source.Frames[0].Metadata.Duration = new FrameDuration(1, 30);
        using var image = new Image<Rgba32>(2, 2, Red);

        var appended = image.AppendFrame(source.Frames[0]);
        Assert.NotSame(source.Frames[0], appended);
        Assert.NotSame(source.Frames[0].Metadata, appended.Metadata);
        Assert.Equal(new FrameDuration(1, 30), appended.Metadata.Duration);
        Assert.Equal(Green, appended[1, 1]);

        // Mutating the source after the copy never affects the image (and vice versa)
        source.Frames[0][1, 1] = Blue;
        source.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(5);
        Assert.Equal(Green, appended[1, 1]);
        Assert.Equal(new FrameDuration(1, 30), appended.Metadata.Duration);
        appended[0, 0] = Blue;
        Assert.Equal(Green, source.Frames[0][0, 0]);

        var inserted = image.InsertFrame(0, source.Frames[0]);
        Assert.Same(inserted, image.Frames[0]);
        Assert.Equal(Blue, inserted[1, 1]);
        Assert.Equal(3, image.Frames.Count);

        // The source may belong to the same image
        var self = image.InsertFrame(3, image.Frames[1]);
        Assert.Same(self, image.Frames[3]);
        Assert.Equal(Red, self[0, 0]);
        self[0, 0] = Green;
        Assert.Equal(Red, image.Frames[1][0, 0]);
    }

    [Fact]
    public void FramesAreNeverResizedOrConvertedImplicitly()
    {
        using var image = new Image<Rgba32>(2, 2);
        using var other = new Image<Rgba32>(3, 2);
        using var bgra = new Image<Bgra32>(2, 2);

        Assert.Throws<ArgumentNullException>(() => image.AppendFrame(null!));
        Assert.Throws<ArgumentNullException>(() => image.InsertFrame(0, null!));
        Assert.Throws<ArgumentNullException>(() => image.SetPosterFrame(null!));
        Assert.Contains("resized", Assert.Throws<ArgumentException>(() => image.AppendFrame(other.Frames[0])).Message, StringComparison.Ordinal);
        Assert.Contains("CloneAs", Assert.Throws<ArgumentException>(() => image.AppendFrame(bgra.Frames[0])).Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => image.InsertFrame(0, bgra.Frames[0]));
        Assert.Throws<ArgumentException>(() => image.SetPosterFrame(other.Frames[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.InsertFrame(2, image.Frames[0]));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.InsertFrame(-1, image.Frames[0]));
        Assert.Single(image.Frames);
        Assert.Null(image.PosterFrame);
    }

    [Fact]
    public void RemovingFramesKeepsAnimationSettingsAndInvalidatesRemovedReferences()
    {
        using var image = new Image<Rgba32>(2, 2, Red);
        var second = image.AppendFrame();
        var first = image.Frames[0];
        image.Animation!.TotalPlays = 3;

        Assert.Throws<ArgumentOutOfRangeException>(() => image.RemoveFrame(2));
        image.RemoveFrame(1);
        Assert.Single(image.Frames);
        Assert.Same(first, image.Frames[0]);

        // A single-frame image with animation settings remains animated
        Assert.Equal(3, image.Animation!.TotalPlays);
        Assert.True(image.IsAnimated);

        Assert.Throws<ObjectDisposedException>(() => second.Metadata);
        Assert.Throws<ObjectDisposedException>(() => second.Size);
        Assert.Throws<ObjectDisposedException>(() => second[0, 0]);
        Assert.Throws<ObjectDisposedException>(() => second.ProcessPixelRows(static _ => { }));
        Assert.Throws<ObjectDisposedException>(() => second.ProcessPixelBytes(static _ => { }));
        Assert.Throws<ObjectDisposedException>(() => second.CopyPixelBytesTo(new byte[16]));
        Assert.Throws<ObjectDisposedException>(() => image.AppendFrame(second));
        Assert.Equal(-1, image.Frames.IndexOf(second));

        Assert.Contains("last frame", Assert.Throws<InvalidOperationException>(() => image.RemoveFrame(0)).Message, StringComparison.Ordinal);
        Assert.Equal(Red, first[1, 1]);

        image.Animation = null;
        Assert.False(image.IsAnimated);
    }

    [Fact]
    public void MoveUsesTheFinalDestinationIndexAndKeepsIdentity()
    {
        using var image = new Image<Gray8>(1, 1);
        image.AppendFrame();
        image.AppendFrame();
        var a = image.Frames[0];
        var b = image.Frames[1];
        var c = image.Frames[2];
        a[0, 0] = new Gray8(1);
        b[0, 0] = new Gray8(2);
        c[0, 0] = new Gray8(3);

        image.MoveFrame(0, 2);
        AssertFrames(image.Frames, b, c, a);
        image.MoveFrame(2, 1);
        AssertFrames(image.Frames, b, a, c);
        image.MoveFrame(1, 1);
        AssertFrames(image.Frames, b, a, c);
        Assert.Equal(new Gray8(1), a[0, 0]);
        Assert.Equal(new Gray8(2), image.Frames[0][0, 0]);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.MoveFrame(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.MoveFrame(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.MoveFrame(-1, 0));
    }

    [Fact]
    public void StructuralEditsInvalidateEnumerators()
    {
        using var image = new Image<Rgba32>(1, 1);
        image.AppendFrame();
        image.AppendFrame();

        AssertInvalidated(image, () => image.AppendFrame());
        AssertInvalidated(image, () => image.InsertFrame(0, image.Frames[0]));
        AssertInvalidated(image, () => image.RemoveFrame(0));
        AssertInvalidated(image, () => image.MoveFrame(0, 1));

        // Pixel and metadata edits are not structural
        var count = 0;
        foreach (var frame in image.Frames)
        {
            frame[0, 0] = Red;
            frame.Metadata.Duration = FrameDuration.FromMilliseconds(10);
            count++;
        }

        Assert.Equal(image.Frames.Count, count);

        // The untyped enumerator observes the same version
        using var untyped = ((Image)image).Frames.GetEnumerator();
        Assert.True(untyped.MoveNext());
        image.RemoveFrame(0);
        Assert.Throws<InvalidOperationException>(() => untyped.MoveNext());

        static void AssertInvalidated(Image<Rgba32> image, Action edit)
        {
            var enumerator = image.Frames.GetEnumerator();
            Assert.True(enumerator.MoveNext());
            edit();
            Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());
        }
    }

    [Fact]
    public void PosterFrameIsACopyThatCreatesAnimationSettings()
    {
        using var image = new Image<Rgba32>(2, 1, Red);
        image.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(40);
        Assert.False(image.RemovePosterFrame());

        var poster = image.SetPosterFrame(image.Frames[0]);
        Assert.Same(poster, image.PosterFrame);
        Assert.NotSame(image.Frames[0], poster);
        Assert.Single(image.Frames);
        Assert.Equal(-1, image.Frames.IndexOf(poster));
        Assert.NotNull(image.Animation);
        Assert.True(image.IsAnimated);
        Assert.Equal(Red, poster[1, 0]);
        Assert.Equal(FrameDuration.FromMilliseconds(40), poster.Metadata.Duration);
        poster[0, 0] = Blue;
        Assert.Equal(Red, image.Frames[0][0, 0]);

        // With a poster frame, the animation settings cannot be removed
        Assert.Throws<InvalidOperationException>(() => image.Animation = null);

        // Replacing the poster frame (here with a copy of itself) invalidates the previous one
        var replacement = image.SetPosterFrame(poster);
        Assert.Same(replacement, image.PosterFrame);
        Assert.Equal(Blue, replacement[0, 0]);
        Assert.Throws<ObjectDisposedException>(() => poster[0, 0]);

        Assert.True(image.RemovePosterFrame());
        Assert.Null(image.PosterFrame);
        Assert.Throws<ObjectDisposedException>(() => replacement.Metadata);
        Assert.True(image.IsAnimated); // the settings stay
        image.Animation = null;
        Assert.False(image.IsAnimated);
    }

    [Fact]
    public void AnimationSetterStoresACopyAndValidatesState()
    {
        using var image = new Image<Rgba32>(1, 1);
        var settings = new AnimationMetadata { TotalPlays = 2 };
        image.Animation = settings;
        Assert.NotSame(settings, image.Animation);
        settings.TotalPlays = 5;
        Assert.Equal(2, image.Animation!.TotalPlays);
        Assert.True(image.IsAnimated);

        image.AppendFrame();
        Assert.Equal(2, image.Animation!.TotalPlays); // existing settings are kept
        Assert.Throws<InvalidOperationException>(() => image.Animation = null);
        image.Animation = new AnimationMetadata();
        Assert.Null(image.Animation.TotalPlays);
    }

    [Fact]
    public void CloneIsADeepIndependentCopyThatOutlivesTheSource()
    {
        var source = new Image<Rgba64>(2, 2, new Rgba64(1, 2, 3, 4));
        source.AppendFrame().Metadata.Duration = new FrameDuration(1, 3);
        source.Frames[1][1, 0] = new Rgba64(0x1234, 0x5678, 0x9ABC, 0xDEF0);
        source.SetPosterFrame(source.Frames[1]);
        source.Animation!.TotalPlays = 7;
        source.Metadata.Orientation = ExifOrientation.RightTop;
        source.Metadata.TextEntries.Add(new ImageTextEntry("Title", "a"));

        var clone = source.Clone();
        Assert.NotSame(source.Owner.Scope, clone.Owner.Scope);
        Assert.NotSame(source.Metadata, clone.Metadata);
        Assert.NotSame(source.Animation, clone.Animation);
        Assert.Equal(7, clone.Animation!.TotalPlays);
        Assert.Equal(ExifOrientation.RightTop, clone.Metadata.Orientation);
        Assert.Equal(2, clone.Frames.Count);
        Assert.Equal(new FrameDuration(1, 3), clone.Frames[1].Metadata.Duration);
        Assert.Equal(new Rgba64(0x1234, 0x5678, 0x9ABC, 0xDEF0), clone.Frames[1][1, 0]);
        Assert.Equal(new Rgba64(0x1234, 0x5678, 0x9ABC, 0xDEF0), clone.PosterFrame![1, 0]);
        Assert.Equal(new Rgba64(1, 2, 3, 4), clone.Frames[0][0, 1]);

        // No aliasing of pixels or mutable metadata
        clone.Frames[0][0, 0] = default;
        clone.Metadata.TextEntries.Clear();
        clone.Frames[1].Metadata.Duration = FrameDuration.Zero;
        Assert.Equal(new Rgba64(1, 2, 3, 4), source.Frames[0][0, 0]);
        Assert.Single(source.Metadata.TextEntries);
        Assert.Equal(new FrameDuration(1, 3), source.Frames[1].Metadata.Duration);

        source.Dispose();
        Assert.Equal(new Rgba64(0x1234, 0x5678, 0x9ABC, 0xDEF0), clone.Frames[1][1, 0]);
        clone.Dispose();
        Assert.Equal(0, clone.Owner.Scope.LiveBytes);
    }

    [Fact]
    public void CloneFrameExtractsAStillImage()
    {
        using var image = new Image<Rgba32>(2, 1, Red);
        image.AppendFrame()[1, 0] = Green;
        image.Frames[1].Metadata.Duration = FrameDuration.FromMilliseconds(70);
        image.SetPosterFrame(image.Frames[0]);
        image.Metadata.Orientation = ExifOrientation.BottomRight;

        using var still = image.CloneFrame(1);
        Assert.Single(still.Frames);
        Assert.Null(still.Animation);
        Assert.Null(still.PosterFrame);
        Assert.False(still.IsAnimated);
        Assert.Equal(FrameDuration.FromMilliseconds(70), still.Frames[0].Metadata.Duration);
        Assert.Equal(ExifOrientation.BottomRight, still.Metadata.Orientation);
        Assert.NotSame(image.Metadata, still.Metadata);
        Assert.Equal(Green, still.Frames[0][1, 0]);
        Assert.Equal(default, still.Frames[0][0, 0]);

        Assert.Throws<ArgumentOutOfRangeException>(() => image.CloneFrame(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => image.CloneFrame(-1));

        using var poster = image.ClonePosterFrame();
        Assert.Equal(Red, poster.Frames[0][1, 0]);
        Assert.Null(poster.Animation);
        Assert.False(poster.IsAnimated);

        image.RemovePosterFrame();
        Assert.Throws<InvalidOperationException>(() => image.ClonePosterFrame());
    }

    [Fact]
    public void CloneAsConvertsEveryFrameAndThePoster()
    {
        using var image = new Image<Rgba32>(2, 1, new Rgba32(0x12, 0x34, 0x56));
        image.AppendFrame(image.Frames[0]).Metadata.Duration = new FrameDuration(2, 7);
        image.SetPosterFrame(image.Frames[0]);
        image.Animation!.TotalPlays = 4;
        image.Metadata.Orientation = ExifOrientation.LeftBottom;

        using var converted = image.CloneAs<Rgba64>();
        Assert.Equal(PixelFormat.Rgba64, converted.PixelFormat);
        Assert.Equal(2, converted.Frames.Count);
        Assert.Equal(new Rgba64(0x1212, 0x3434, 0x5656, 0xFFFF), converted.Frames[1][1, 0]);
        Assert.Equal(new Rgba64(0x1212, 0x3434, 0x5656, 0xFFFF), converted.PosterFrame![0, 0]);
        Assert.Equal(new FrameDuration(2, 7), converted.Frames[1].Metadata.Duration);
        Assert.Equal(4, converted.Animation!.TotalPlays);
        Assert.Equal(ExifOrientation.LeftBottom, converted.Metadata.Orientation);

        using var gray = image.CloneAs<Gray8>();
        Assert.Equal(new Gray8((byte)PixelConverter.Luma(0x12, 0x34, 0x56, 255)), gray.Frames[0][0, 0]);

        using var same = image.CloneAs<Rgba32>();
        Assert.Equal(new Rgba32(0x12, 0x34, 0x56), same.Frames[1][1, 0]);
        same.Frames[0][0, 0] = default;
        Assert.Equal(new Rgba32(0x12, 0x34, 0x56), image.Frames[0][0, 0]);
    }

    [Fact]
    public void CloneAsRejectsAlphaLossBeforeAllocating()
    {
        using var image = new Image<Rgba32>(2, 2, Red);
        image.AppendFrame(image.Frames[0]);
        image.SetPosterFrame(image.Frames[0]);
        image.PosterFrame![1, 1] = new Rgba32(10, 20, 30, 128);

        // Every frame and the poster are scanned before the destination is allocated
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.CloneAs<Rgb24>());
        Assert.Contains("BackgroundColor", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, image.Owner.ActiveLeaseCount);

        using var flattened = image.CloneAs<Rgb24>(new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0) });
        Assert.Equal(new Rgb24(5, 10, 15), flattened.PosterFrame![1, 1]);
        Assert.Equal(new Rgb24(255, 0, 0), flattened.Frames[1][1, 1]);

        Assert.Throws<NotSupportedException>(() => image.CloneAs<int>());
    }

    [Fact]
    public void CloneAsAppliesTheColorProfilePolicy()
    {
        using var image = new Image<Rgb24>(1, 1);
        image.Metadata.IccProfile = new IccProfile(MetadataBlob.FromOwnedArray(CreateIccHeader("RGB ")));

        Assert.Throws<UnsupportedImageFeatureException>(() => image.CloneAs<Gray8>());
        using var discarded = image.CloneAs<Gray8>(new PixelConversionOptions { DiscardIncompatibleColorProfile = true });
        Assert.Null(discarded.Metadata.IccProfile);
        using var kept = image.CloneAs<Rgba32>();
        Assert.Same(image.Metadata.IccProfile, kept.Metadata.IccProfile);
    }

    [Fact]
    public void DisposeIsIdempotentAndInvalidatesTheImageAndItsFrames()
    {
        var image = new Image<Rgba32>(4, 4);
        var frame = image.Frames[0];
        var poster = image.SetPosterFrame(frame);
        var frames = image.Frames;
        var scope = image.Owner.Scope;
        Assert.True(scope.LiveBytes > 0);

        image.Dispose();
        image.Dispose();
        Assert.Equal(0, scope.LiveBytes);

        Assert.Throws<ObjectDisposedException>(() => image.Size);
        Assert.Throws<ObjectDisposedException>(() => image.Width);
        Assert.Throws<ObjectDisposedException>(() => image.Configuration);
        Assert.Throws<ObjectDisposedException>(() => image.Metadata);
        Assert.Throws<ObjectDisposedException>(() => image.Animation);
        Assert.Throws<ObjectDisposedException>(() => image.IsAnimated);
        Assert.Throws<ObjectDisposedException>(() => image.Frames);
        Assert.Throws<ObjectDisposedException>(() => image.PosterFrame);
        Assert.Throws<ObjectDisposedException>(() => frames.Count);
        Assert.Throws<ObjectDisposedException>(() => frames[0]);
        Assert.Throws<ObjectDisposedException>(() => image.Clone());
        Assert.Throws<ObjectDisposedException>(() => image.CloneAs<Rgba64>());
        Assert.Throws<ObjectDisposedException>(() => image.CloneFrame(0));
        Assert.Throws<ObjectDisposedException>(() => image.AppendFrame());
        Assert.Throws<ObjectDisposedException>(() => image.RemoveFrame(0));
        Assert.Throws<ObjectDisposedException>(() => image.MoveFrame(0, 0));
        Assert.Throws<ObjectDisposedException>(() => image.RemovePosterFrame());
        Assert.Throws<ObjectDisposedException>(() => image.Save(new MemoryStream(), new Formats.PngEncoder()));
        Assert.Throws<ObjectDisposedException>(() => frame[0, 0]);
        Assert.Throws<ObjectDisposedException>(() => frame.Metadata);
        Assert.Throws<ObjectDisposedException>(() => poster.ProcessPixelRows(static _ => { }));
        Assert.Equal(PixelFormat.Rgba32, image.PixelFormat);
    }

    [Fact]
    public void FrameCountAndAllocationLimitsApplyToEdits()
    {
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 3 } };
        using var image = new Image<Gray8>(4, 4, configuration);
        image.AppendFrame();
        image.SetPosterFrame(image.Frames[0]); // the poster counts toward MaxFrames
        var exception = Assert.Throws<ImageResourceLimitException>(() => image.AppendFrame());
        Assert.Equal((ImageResourceLimitKind.Frames, 3L, (long?)4), (exception.Kind, exception.Limit, exception.Requested));
        Assert.Throws<ImageResourceLimitException>(() => image.InsertFrame(0, image.Frames[0]));
        image.SetPosterFrame(image.Frames[1]); // replacing the poster does not add a frame
        Assert.Equal(2, image.Frames.Count);

        // Allocation failures leave the image unchanged
        using var small = new Image<Rgba32>(64, 64, new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 20_000 } });
        var live = small.Owner.Scope.LiveBytes;
        var version = small.StructureVersion;
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => small.AppendFrame(small.Frames[0])).Kind);
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, Assert.Throws<ImageResourceLimitException>(() => small.SetPosterFrame(small.Frames[0])).Kind);
        Assert.Single(small.Frames);
        Assert.Null(small.PosterFrame);
        Assert.Equal(live, small.Owner.Scope.LiveBytes);
        Assert.Equal(version, small.StructureVersion);
    }

    [Fact]
    public void EachImageHasItsOwnOwnerAndReaderImagesCanShareAScope()
    {
        using var a = new Image<Rgba32>(4, 4);
        using var b = new Image<Rgba32>(4, 4);
        Assert.NotSame(a.Owner, b.Owner);
        Assert.NotSame(a.Owner.Scope, b.Owner.Scope);

        // Sequential readers pass their scope: returned images stay charged to it until each of them is disposed
        var readerScope = AllocationScope.Create(ImageConfiguration.Default, "reader");
        var first = new Image<Rgba32>(ImageConfiguration.Default, new Size(8, 8), readerScope, layoutOptions: null);
        var second = new Image<Rgba32>(ImageConfiguration.Default, new Size(8, 8), readerScope, layoutOptions: null);
        Assert.NotSame(first.Owner, second.Owner);
        Assert.Same(readerScope, first.Owner.Scope);
        var both = readerScope.LiveBytes;
        first.Dispose();
        Assert.Equal(both / 2, readerScope.LiveBytes);
        Assert.Equal(default, second.Frames[0][7, 7]);
        second.Dispose();
        Assert.Equal(0, readerScope.LiveBytes);
    }

    [Fact]
    public void TheHotspotOfAFrameIsAlwaysInsideTheFrame()
    {
        using var image = new Image<Rgba32>(3, 2, Red);
        var metadata = image.Frames[0].Metadata;
        Assert.Null(metadata.Hotspot);
        metadata.Hotspot = new Point(2, 1); // the bottom-right pixel
        Assert.Equal(new Point(2, 1), metadata.Hotspot);
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Hotspot = new Point(3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Hotspot = new Point(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Hotspot = new Point(-1, 0));
        Assert.Equal(new Point(2, 1), metadata.Hotspot);
        metadata.Hotspot = null;
        Assert.Null(metadata.Hotspot);

        // Once its frame is removed, the settings object is checked like one that belongs to no frame
        var second = image.AppendFrame();
        var detached = second.Metadata;
        image.RemoveFrame(1);
        detached.Hotspot = new Point(100, 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => detached.Hotspot = new Point(-1, 0));
    }

    [Fact]
    public void CopiesOfAFrameKeepItsHotspot()
    {
        using var image = new Image<Rgba32>(3, 2, Red);
        image.Frames[0].Metadata.Hotspot = new Point(2, 1);
        Assert.Null(image.AppendFrame().Metadata.Hotspot);
        Assert.Equal(new Point(2, 1), image.AppendFrame(image.Frames[0]).Metadata.Hotspot);
        Assert.Equal(new Point(2, 1), image.InsertFrame(0, image.Frames[0]).Metadata.Hotspot);
        Assert.Equal(new Point(2, 1), image.SetPosterFrame(image.Frames[0]).Metadata.Hotspot);

        using var clone = image.Clone();
        Assert.Equal([new Point(2, 1), new Point(2, 1), null, new Point(2, 1)], clone.Frames.Cast<ImageFrame>().Select(frame => frame.Metadata.Hotspot));
        Assert.Equal(new Point(2, 1), clone.PosterFrame!.Metadata.Hotspot);

        using var converted = image.CloneAs<Rgba64>();
        Assert.Equal(new Point(2, 1), converted.Frames[0].Metadata.Hotspot);
        Assert.Equal(new Point(2, 1), converted.PosterFrame!.Metadata.Hotspot);

        using var still = image.CloneFrame(3);
        Assert.Equal(new Point(2, 1), still.Frames[0].Metadata.Hotspot);
        using var poster = image.ClonePosterFrame();
        Assert.Equal(new Point(2, 1), poster.Frames[0].Metadata.Hotspot);

        // A copy is independent, and the hotspot of a copy is checked against its own frame
        clone.Frames[0].Metadata.Hotspot = new Point(0, 0);
        Assert.Equal(new Point(2, 1), image.Frames[0].Metadata.Hotspot);
        Assert.Throws<ArgumentOutOfRangeException>(() => clone.Frames[0].Metadata.Hotspot = new Point(3, 0));
    }

    private static void AssertFrames(ImageFrameCollection actual, params ImageFrame[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Same(expected[i], actual[i]);
        }
    }

    private static byte[] CreateIccHeader(string colorSpace)
    {
        var data = new byte[132];
        data[3] = 132;
        Encoding.ASCII.GetBytes(colorSpace).CopyTo(data, 16);
        Encoding.ASCII.GetBytes("acsp").CopyTo(data, 36);
        return data;
    }
}
