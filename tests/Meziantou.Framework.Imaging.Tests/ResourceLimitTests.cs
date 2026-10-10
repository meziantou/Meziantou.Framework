using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Limit validation and incremental enforcement: boundaries are inclusive, one over fails.</summary>
public sealed class ResourceLimitTests
{
    [Fact]
    public void CanvasLimitsAreInclusive()
    {
        var limits = new ImageResourceLimits { MaxWidth = 100, MaxHeight = 50, MaxFramePixels = 4000 };
        limits.EnsureCanvasWithinLimits(100, 40);
        limits.EnsureCanvasWithinLimits(80, 50);
        AssertLimit(ImageResourceLimitKind.Width, 100, 101, () => limits.EnsureCanvasWithinLimits(101, 1));
        AssertLimit(ImageResourceLimitKind.Height, 50, 51, () => limits.EnsureCanvasWithinLimits(1, 51));
        limits.EnsureCanvasWithinLimits(80, 50); // exactly 4000 pixels
        AssertLimit(ImageResourceLimitKind.FramePixels, 4000, 4050, () => limits.EnsureCanvasWithinLimits(81, 50));
    }

    [Fact]
    public void DefaultCanvasLimits()
    {
        var limits = ImageResourceLimits.Default;
        limits.EnsureCanvasWithinLimits(32_768, 3051); // 99,975,168 pixels
        AssertLimit(ImageResourceLimitKind.Width, 32_768, 32_769, () => limits.EnsureCanvasWithinLimits(32_769, 1));
        AssertLimit(ImageResourceLimitKind.FramePixels, 100_000_000, 32_768L * 3052, () => limits.EnsureCanvasWithinLimits(32_768, 3052));
        AssertLimit(ImageResourceLimitKind.LiveAllocationBytes, 512L * 1024 * 1024, (512L * 1024 * 1024) + 1, () => limits.EnsureLiveAllocationWithinLimit((512L * 1024 * 1024) + 1));
        limits.EnsureLiveAllocationWithinLimit(512L * 1024 * 1024);
    }

    [Fact]
    public void ImageConsumersRejectInvalidDimensionsBeforeLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Image<Rgba32>(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Image<Rgba32>(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Image.ImportPixelData<Rgba32>([], 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Image.ImportPixelBytes<Gray8>([], 1, 0));

        // Over-limit canvases fail with the structured limit exception, never by clamping
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 8 } };
        AssertLimit(ImageResourceLimitKind.Width, 8, 9, () => _ = new Image<Rgba32>(9, 1, configuration));
        AssertLimit(ImageResourceLimitKind.Width, 8, 9, () => _ = new Image<Gray8>(9, 1, new Gray8(1), configuration));
        AssertLimit(ImageResourceLimitKind.Width, 8, 9, () => Image.ImportPixelData<Rgba32>(new Rgba32[9], 9, 1, configuration: configuration));
        AssertLimit(ImageResourceLimitKind.Width, 8, 9, () => Image.ImportPixelBytes<Rgba32>(new byte[36], 9, 1, configuration: configuration));
        AssertLimit(ImageResourceLimitKind.Height, 32_768, 40_000, () => _ = new Image<Rgba32>(1, 40_000));
    }

    [Fact]
    public void FramesIncludeThePosterButTotalPixelsCountDisplayedFramesOnly()
    {
        var tracker = new InputResourceTracker(new ImageResourceLimits { MaxFrames = 3, MaxTotalPixels = 20 });
        var canvas = new Size(5, 2);
        tracker.ChargeFrame(canvas, isPoster: true);
        tracker.ChargeFrame(canvas);
        tracker.ChargeFrame(canvas);
        Assert.Equal((3, 20L), (tracker.Frames, tracker.TotalPixels));
        AssertLimit(ImageResourceLimitKind.Frames, 3, 4, () => tracker.ChargeFrame(canvas));
        Assert.Equal((3, 20L), (tracker.Frames, tracker.TotalPixels)); // unchanged after a failure

        var pixels = new InputResourceTracker(new ImageResourceLimits { MaxTotalPixels = 25 });
        pixels.ChargeFrame(canvas);
        pixels.ChargeFrame(canvas);
        AssertLimit(ImageResourceLimitKind.TotalPixels, 25, 30, () => pixels.ChargeFrame(canvas));
        Assert.Equal((2, 20L), (pixels.Frames, pixels.TotalPixels));
    }

    [Fact]
    public void ByteCountersAreBoundedAndSaturating()
    {
        var tracker = new InputResourceTracker(new ImageResourceLimits { MaxEncodedBytes = 100, MaxMetadataBytes = 10 });
        tracker.ChargeEncodedBytes(60);
        tracker.ChargeEncodedBytes(40);
        AssertLimit(ImageResourceLimitKind.EncodedBytes, 100, 101, () => tracker.ChargeEncodedBytes(1));
        AssertLimit(ImageResourceLimitKind.EncodedBytes, 100, long.MaxValue, () => tracker.ChargeEncodedBytes(long.MaxValue));
        Assert.Equal(100, tracker.EncodedBytes);

        tracker.ChargeMetadataBytes(10);
        AssertLimit(ImageResourceLimitKind.MetadataBytes, 10, 11, () => tracker.ChargeMetadataBytes(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.ChargeMetadataBytes(-1));
        Assert.Throws<ArgumentNullException>(() => new InputResourceTracker(null!));
    }

    [Fact]
    public void AllLimitsAcceptTheirMaximumAndRejectNonPositiveValues()
    {
        var limits = new ImageResourceLimits
        {
            MaxWidth = int.MaxValue,
            MaxHeight = int.MaxValue,
            MaxFramePixels = long.MaxValue,
            MaxFrames = int.MaxValue,
            MaxTotalPixels = long.MaxValue,
            MaxEncodedBytes = long.MaxValue,
            MaxMetadataBytes = long.MaxValue,
            MaxLiveAllocationBytes = long.MaxValue,
        };
        limits.EnsureCanvasWithinLimits(int.MaxValue, int.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxWidth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxFrames = int.MinValue });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxEncodedBytes = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageConfiguration { MaxDegreeOfParallelism = -1 });
        Assert.Equal(Environment.ProcessorCount, new ImageConfiguration { MaxDegreeOfParallelism = Environment.ProcessorCount }.MaxDegreeOfParallelism);
    }

    private static void AssertLimit(ImageResourceLimitKind kind, long limit, long requested, Action action)
    {
        var exception = Assert.Throws<ImageResourceLimitException>(action);
        Assert.Equal(kind, exception.Kind);
        Assert.Equal(limit, exception.Limit);
        Assert.Equal(requested, exception.Requested);
        Assert.Contains(kind.ToString(), exception.Message, StringComparison.Ordinal);
    }
}
