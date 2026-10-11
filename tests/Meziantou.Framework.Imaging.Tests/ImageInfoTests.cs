using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary><see cref="ImageInfo"/> snapshots: unknown values stay unknown, metadata is detached, inconsistent snapshots are rejected.</summary>
public sealed class ImageInfoTests
{
    [Fact]
    public void HeaderOnlyValuesRemainUnknown()
    {
        // A GIF header cannot tell the frame count, whether the file is animated, or whether frames use transparency
        var info = Create(ImageFormat.Gif, frameCount: null, isAnimated: null, hasPosterFrame: null, mayHaveTransparency: null, animation: null);
        Assert.Null(info.FrameCount);
        Assert.Null(info.IsAnimated);
        Assert.Null(info.HasPosterFrame);
        Assert.Null(info.MayHaveTransparency);
        Assert.Null(info.Animation);
        Assert.Equal(ImageIdentifyMode.Header, info.IdentifyMode);
        Assert.Equal((3, 2), (info.Width, info.Height));
        Assert.Equal(new Size(3, 2), info.Size);
    }

    [Fact]
    public void DecodedStorageIsDistinctFromEncodedSamples()
    {
        // A 4-bit palette PNG decodes to Rgba32 while its encoded samples are 4-bit indexes
        var info = new ImageInfo(ImageFormat.Png, new Size(3, 2), PixelFormat.Rgba32, ImageColorModel.Indexed, 4, 1, false, false, true, null, new ImageMetadata(), ImageIdentifyMode.FullScan);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
        Assert.Equal(ImageColorModel.Indexed, info.ColorModel);
        Assert.Equal(4, info.BitsPerComponent);
        Assert.Equal(1, info.FrameCount);
        Assert.False(info.IsAnimated);
        Assert.True(info.MayHaveTransparency);
    }

    [Fact]
    public void MetadataAndAnimationAreDetachedCopies()
    {
        var metadata = new ImageMetadata { Orientation = ExifOrientation.RightTop };
        metadata.TextEntries.Add(new ImageTextEntry("Title", "a"));
        var animation = new AnimationMetadata { TotalPlays = 3 };
        var info = Create(ImageFormat.Png, frameCount: 2, isAnimated: true, hasPosterFrame: false, mayHaveTransparency: true, animation: animation, metadata: metadata);

        Assert.NotSame(metadata, info.Metadata);
        Assert.NotSame(animation, info.Animation);
        metadata.Orientation = ExifOrientation.TopLeft;
        metadata.TextEntries.Clear();
        animation.TotalPlays = null;
        Assert.Equal(ExifOrientation.RightTop, info.Metadata.Orientation);
        Assert.Single(info.Metadata.TextEntries);
        Assert.Equal(3, info.Animation!.TotalPlays);

        // Editing the snapshot's containers never affects another snapshot
        var other = Create(ImageFormat.Png, frameCount: 2, isAnimated: true, hasPosterFrame: false, mayHaveTransparency: true, animation: info.Animation, metadata: info.Metadata);
        info.Metadata.TextEntries.Clear();
        Assert.Single(other.Metadata.TextEntries);
    }

    [Fact]
    public void InconsistentSnapshotsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(ImageFormat.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create((ImageFormat)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(ImageFormat.Png, size: new Size(0, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(ImageFormat.Png, frameCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(ImageFormat.Png, bitsPerComponent: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(ImageFormat.Png, pixelFormat: (PixelFormat)99));
        Assert.Throws<ArgumentException>(() => Create(ImageFormat.Png, frameCount: 2, isAnimated: false));
        Assert.Throws<ArgumentException>(() => Create(ImageFormat.Png, isAnimated: false, hasPosterFrame: true));
        Assert.Throws<ArgumentException>(() => Create(ImageFormat.Png, isAnimated: false, animation: new AnimationMetadata()));
        Assert.Throws<ArgumentNullException>(() => new ImageInfo(ImageFormat.Png, new Size(1, 1), PixelFormat.Rgba32, ImageColorModel.Rgba, 8, null, null, null, null, null, null!, ImageIdentifyMode.Header));
    }

    private static ImageInfo Create(
        ImageFormat format,
        Size? size = null,
        PixelFormat pixelFormat = PixelFormat.Rgba32,
        int bitsPerComponent = 8,
        int? frameCount = null,
        bool? isAnimated = null,
        bool? hasPosterFrame = null,
        bool? mayHaveTransparency = null,
        AnimationMetadata? animation = null,
        ImageMetadata? metadata = null)
        => new(format, size ?? new Size(3, 2), pixelFormat, ImageColorModel.Unknown, bitsPerComponent, frameCount, isAnimated, hasPosterFrame, mayHaveTransparency, animation, metadata ?? new ImageMetadata(), ImageIdentifyMode.Header);
}
