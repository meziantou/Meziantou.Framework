using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

public sealed class MetadataTests
{
    [Fact]
    public void MetadataBlobCopiesItsInput()
    {
        byte[] data = [1, 2, 3];
        var blob = new MetadataBlob(data);
        data[0] = 42;
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.ToArray());

        var copy = blob.ToArray();
        copy[1] = 42;
        Assert.Equal(new byte[] { 1, 2, 3 }, blob.Span.ToArray());
        Assert.Equal(blob, new MetadataBlob([1, 2, 3]));
    }

    [Fact]
    public void ImageMetadataCloneDoesNotAliasMutableState()
    {
        var metadata = new ImageMetadata { Orientation = ExifOrientation.RightTop };
        metadata.TextEntries.Add(new ImageTextEntry("Title", "a"));

        var clone = metadata.Clone();
        clone.TextEntries.Add(new ImageTextEntry("Author", "b"));
        clone.Orientation = ExifOrientation.TopLeft;

        Assert.Single(metadata.TextEntries);
        Assert.Equal(ExifOrientation.RightTop, metadata.Orientation);
        Assert.HasCount(2, clone.TextEntries);
        Assert.Throws<ArgumentNullException>(() => metadata.TextEntries.Add(null!));
    }

    [Fact]
    public void InvalidMetadataValuesAreRejected()
    {
        var metadata = new ImageMetadata();
        Assert.Equal(ExifOrientation.TopLeft, metadata.Orientation);
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Orientation = (ExifOrientation)0);
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Orientation = (ExifOrientation)9);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResolution(0, 72));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResolution(72, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResolution(double.PositiveInfinity, 72));
        Assert.Throws<ArgumentException>(() => new ImageTextEntry(string.Empty, "value"));
    }

    [Fact]
    public void IccProfileExposesDeclaredColorSpace()
    {
        var header = new byte[128];
        "GRAY"u8.CopyTo(header.AsSpan(16));
        Assert.Equal(IccProfileColorSpace.Gray, new IccProfile(new MetadataBlob(header)).ColorSpace);

        "RGB "u8.CopyTo(header.AsSpan(16));
        Assert.Equal(IccProfileColorSpace.Rgb, new IccProfile(new MetadataBlob(header)).ColorSpace);

        Assert.Equal(IccProfileColorSpace.Unknown, new IccProfile(new MetadataBlob([1, 2, 3])).ColorSpace);
    }

    [Fact]
    public void MetadataBlobIsImmutableAndComparedByValue()
    {
        var source = new byte[] { 1, 2, 3, 4 };
        var blob = new MetadataBlob(source.AsSpan(1, 2));
        source[1] = 99;
        Assert.Equal(new byte[] { 2, 3 }, blob.ToArray());
        Assert.Equal(2, blob.Length);
        Assert.Equal(new byte[] { 2, 3 }, blob.Memory.ToArray());
        Assert.NotSame(blob.ToArray(), blob.ToArray());
        Assert.Equal(new MetadataBlob([2, 3]).GetHashCode(), blob.GetHashCode());
        Assert.NotEqual(new MetadataBlob([2, 4]), blob);
        Assert.Equal(0, new MetadataBlob([]).Length);

        // Library-owned arrays are adopted without copying (internal decoders), and compared by value
        var owned = new byte[] { 2, 3 };
        Assert.Equal(blob, MetadataBlob.FromOwnedArray(owned));
    }

    [Fact]
    public void ProfilesShareImmutableBlobs()
    {
        var blob = new MetadataBlob([1, 2, 3]);
        Assert.Same(blob, new ExifProfile(blob).Data);
        Assert.Same(blob, new XmpProfile(blob).Data);
        Assert.Same(blob, new IccProfile(blob).Data);
        Assert.Throws<ArgumentNullException>(() => new XmpProfile(null!));
        Assert.Throws<ArgumentNullException>(() => new IccProfile(null!));
    }

    [Fact]
    public void MetadataClonesAreIndependent()
    {
        var icc = new IccProfile(new MetadataBlob(new byte[128]));
        var exif = new ExifProfile(new MetadataBlob([1]));
        var xmp = new XmpProfile(new MetadataBlob([2]));
        var resolution = new ImageResolution(72, 96);
        var metadata = new ImageMetadata
        {
            SourceFormat = ImageFormat.Jpeg,
            Orientation = ExifOrientation.BottomLeft,
            Resolution = resolution,
            IccProfile = icc,
            ExifProfile = exif,
            XmpProfile = xmp,
        };
        metadata.TextEntries.Add(new ImageTextEntry("Title", "a"));

        var clone = metadata.Clone();
        Assert.NotSame(metadata.TextEntries, clone.TextEntries);
        Assert.Equal(metadata.TextEntries, clone.TextEntries);
        Assert.Equal((ImageFormat.Jpeg, ExifOrientation.BottomLeft), (clone.SourceFormat, clone.Orientation));
        Assert.Same(resolution, clone.Resolution); // immutable values are shared
        Assert.Same(icc, clone.IccProfile);
        Assert.Same(exif, clone.ExifProfile);
        Assert.Same(xmp, clone.XmpProfile);

        // Mutating either side never affects the other
        clone.SourceFormat = ImageFormat.Png;
        clone.Orientation = ExifOrientation.TopLeft;
        clone.Resolution = null;
        clone.IccProfile = null;
        clone.TextEntries[0] = new ImageTextEntry("Title", "b");
        metadata.TextEntries.Add(new ImageTextEntry("Author", "c"));
        Assert.Equal((ImageFormat.Jpeg, ExifOrientation.BottomLeft), (metadata.SourceFormat, metadata.Orientation));
        Assert.Same(resolution, metadata.Resolution);
        Assert.Same(icc, metadata.IccProfile);
        Assert.Equal("a", metadata.TextEntries[0].Value);
        Assert.Single(clone.TextEntries);
        Assert.Throws<ArgumentNullException>(() => clone.TextEntries[0] = null!);
    }

    [Fact]
    public void FrameAndAnimationClonesAreIndependent()
    {
        var frame = new FrameMetadata();
        Assert.Equal(FrameDuration.Zero, frame.Duration); // default duration is a valid zero
        frame.Duration = new FrameDuration(1, 3);
        var frameClone = frame.Clone();
        frameClone.Duration = FrameDuration.Zero;
        Assert.Equal(new FrameDuration(1, 3), frame.Duration);

        Assert.Null(frame.Hotspot); // a frame is not a cursor image by default
        frame.Hotspot = new Point(3, 4);
        var cursorClone = frame.Clone();
        Assert.Equal(new Point(3, 4), cursorClone.Hotspot);
        cursorClone.Hotspot = null;
        Assert.Equal(new Point(3, 4), frame.Hotspot);

        // Settings that belong to no frame have no size to check: only negative coordinates are rejected
        frame.Hotspot = new Point(int.MaxValue, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Hotspot = new Point(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => frame.Hotspot = new Point(0, -1));
        Assert.Equal(new Point(int.MaxValue, 0), frame.Hotspot);

        var animation = new AnimationMetadata { TotalPlays = 2 };
        var animationClone = animation.Clone();
        animationClone.TotalPlays = null;
        Assert.Equal(2, animation.TotalPlays);
        Assert.Null(animationClone.TotalPlays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void TotalPlaysMustBePositiveOrInfinite(int value)
    {
        var animation = new AnimationMetadata { TotalPlays = 5 };
        Assert.Throws<ArgumentOutOfRangeException>(() => animation.TotalPlays = value);
        Assert.Equal(5, animation.TotalPlays); // unchanged after a rejected value
        animation.TotalPlays = null;
        animation.TotalPlays = int.MaxValue;
        Assert.Equal(int.MaxValue, animation.TotalPlays);
    }

    [Theory]
    [InlineData(0.0, 72.0)]
    [InlineData(-1.0, 72.0)]
    [InlineData(72.0, -0.0)]
    [InlineData(double.NaN, 72.0)]
    [InlineData(72.0, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 72.0)]
    public void ResolutionMustBePositiveAndFinite(double horizontal, double vertical)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResolution(horizontal, vertical));

    [Fact]
    public void ResolutionValues()
    {
        var resolution = new ImageResolution(double.Epsilon, double.MaxValue);
        Assert.Equal((double.Epsilon, double.MaxValue), (resolution.HorizontalDpi, resolution.VerticalDpi));
        Assert.Equal(new ImageResolution(72, 96), new ImageResolution(72, 96));
        Assert.NotEqual(new ImageResolution(72, 96), new ImageResolution(96, 72));
        Assert.Equal(new ImageResolution(72, 96).GetHashCode(), new ImageResolution(72, 96).GetHashCode());
        Assert.Equal("72x96 dpi", new ImageResolution(72, 96).ToString());
    }

    [Fact]
    public void OrientationAcceptsExactlyTheEightExifValues()
    {
        var metadata = new ImageMetadata();
        for (var value = -1; value <= 10; value++)
        {
            if (value is >= 1 and <= 8)
            {
                metadata.Orientation = (ExifOrientation)value;
                Assert.Equal(value, (int)metadata.Orientation);
            }
            else
            {
                var current = metadata.Orientation;
                var v = value;
                Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Orientation = (ExifOrientation)v);
                Assert.Equal(current, metadata.Orientation);
            }
        }
    }

    [Fact]
    public void TextEntryValidationAndEquality()
    {
        Assert.Throws<ArgumentNullException>(() => new ImageTextEntry(null!, "v"));
        Assert.Throws<ArgumentNullException>(() => new ImageTextEntry("k", null!));
        var entry = new ImageTextEntry("Title", string.Empty, "fr", "Titre");
        Assert.Equal((string.Empty, "fr", "Titre"), (entry.Value, entry.LanguageTag, entry.TranslatedKeyword));
        Assert.Equal(entry, new ImageTextEntry("Title", string.Empty, "fr", "Titre"));
        Assert.NotEqual(entry, new ImageTextEntry("Title", string.Empty, "FR", "Titre"));
        Assert.Equal(entry.GetHashCode(), new ImageTextEntry("Title", string.Empty, "fr", "Titre").GetHashCode());
        Assert.Equal("Comment", ImageTextEntry.CommentKeyword);
    }

    [Fact]
    public void WriterOptionsSnapshotDoesNotAliasMutableState()
    {
        var metadata = new ImageMetadata { Orientation = ExifOrientation.RightTop };
        var animation = new AnimationMetadata { TotalPlays = 4 };
        var encoder = new Formats.GifEncoder();
        var configuration = new ImageConfiguration { MaxDegreeOfParallelism = 2 };
        var options = new ImageWriterOptions(3, 2) { Metadata = metadata, Animation = animation, Encoder = encoder, ExpectedFrameCount = 5, Configuration = configuration, LeaveOpen = false };

        var snapshot = options.CreateSnapshot();
        metadata.Orientation = ExifOrientation.TopLeft;
        metadata.TextEntries.Add(new ImageTextEntry("Title", "late"));
        animation.TotalPlays = null;

        Assert.Equal(new Size(3, 2), snapshot.CanvasSize);
        Assert.Equal(ExifOrientation.RightTop, snapshot.Metadata!.Orientation);
        Assert.Empty(snapshot.Metadata.TextEntries);
        Assert.Equal(4, snapshot.Animation!.TotalPlays);
        Assert.Same(encoder, snapshot.Encoder); // immutable settings are shared
        Assert.Same(configuration, snapshot.Configuration);
        Assert.Equal((5, false), (snapshot.ExpectedFrameCount, snapshot.LeaveOpen));
        Assert.Null(new ImageWriterOptions(1, 1).CreateSnapshot().Metadata);
    }
}
