using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Error classification: content exceptions derive from ImageException and carry structured data; BCL exceptions are used for arguments and states.</summary>
public sealed class ExceptionTests
{
    [Theory]
    [InlineData(typeof(UnknownImageFormatException))]
    [InlineData(typeof(InvalidImageContentException))]
    [InlineData(typeof(UnsupportedImageFeatureException))]
    [InlineData(typeof(ImageResourceLimitException))]
    public void ContentExceptionsDeriveFromImageException(Type type)
    {
        Assert.True(typeof(ImageException).IsAssignableFrom(type));
        Assert.False(type.IsSealed); // follows the standard exception pattern
        var instance = (Exception)Activator.CreateInstance(type)!;
        Assert.False(string.IsNullOrEmpty(instance.Message));
        var inner = new IOException("inner");
        var withInner = (Exception)Activator.CreateInstance(type, "message", inner)!;
        Assert.Same(inner, withInner.InnerException);
        Assert.Equal("message", withInner.Message);
    }

    [Fact]
    public void ResourceLimitExceptionCarriesKindLimitAndRequested()
    {
        var exception = new ImageResourceLimitException(ImageResourceLimitKind.Frames, 1000, 1001);
        Assert.Equal((ImageResourceLimitKind.Frames, 1000L, (long?)1001), (exception.Kind, exception.Limit, exception.Requested));
        Assert.Equal("The resource limit 'Frames' (1000) was exceeded: 1001 requested.", exception.Message);

        var cumulative = new ImageResourceLimitException(ImageResourceLimitKind.TotalPixels, 10, null);
        Assert.Null(cumulative.Requested);
        Assert.Equal("The resource limit 'TotalPixels' (10) was exceeded.", cumulative.Message);

        var unspecified = new ImageResourceLimitException("custom");
        Assert.Equal((ImageResourceLimitKind.Unknown, 0L, (long?)null), (unspecified.Kind, unspecified.Limit, unspecified.Requested));
    }

    [Fact]
    public void LimitKindsMapOneToOneToLimitProperties()
    {
        var kinds = Enum.GetNames<ImageResourceLimitKind>().Where(name => name != nameof(ImageResourceLimitKind.Unknown)).Select(name => "Max" + name).Order(StringComparer.Ordinal);
        var properties = typeof(ImageResourceLimits).GetProperties().Where(property => property.Name.StartsWith("Max", StringComparison.Ordinal)).Select(property => property.Name).Order(StringComparer.Ordinal);
        Assert.Equal(properties, kinds);
    }

    [Fact]
    public void FormatAndFeatureAreStructured()
    {
        var invalid = new InvalidImageContentException("bad CRC", ImageFormat.Png);
        Assert.Equal(ImageFormat.Png, invalid.Format);
        Assert.Equal(ImageFormat.Unknown, new InvalidImageContentException("x").Format);

        var unsupported = new UnsupportedImageFeatureException("arithmetic", ImageFormat.Jpeg, "JPEG arithmetic coding");
        Assert.Equal((ImageFormat.Jpeg, "JPEG arithmetic coding"), (unsupported.Format, unsupported.Feature));
        Assert.Equal((ImageFormat.Unknown, (string?)null), (new UnsupportedImageFeatureException().Format, new UnsupportedImageFeatureException().Feature));
    }

    [Fact]
    public void OperationsUseTheDocumentedClassification()
    {
        // Unrepresentable timing or play count for the output format: UnsupportedImageFeatureException
        Assert.IsType<UnsupportedImageFeatureException>(Record.Exception(() => AnimationTiming.ToGifDelay(new FrameDuration(1, 3), FrameDurationRounding.RequireExact)));
        Assert.IsType<UnsupportedImageFeatureException>(Record.Exception(() => AnimationTiming.ToGifLoopCount(100_000)));

        // Metadata loss without explicit policy: UnsupportedImageFeatureException; malformed payload: InvalidImageContentException
        Assert.IsType<UnsupportedImageFeatureException>(Record.Exception(() => MetadataWritePlan.Create(new ImageMetadata { Orientation = ExifOrientation.RightTop }, ImageFormat.Gif, MetadataHandling.Strict, new Size(1, 1))));
        Assert.IsType<InvalidImageContentException>(Record.Exception(() => ExifTiff.Rewrite([1, 2, 3], null, null, removeThumbnail: false)));
        Assert.IsType<InvalidImageContentException>(Record.Exception(() => AnimationTiming.FromApngNumPlays(uint.MaxValue)));

        // Configured limits: ImageResourceLimitException
        Assert.IsType<ImageResourceLimitException>(Record.Exception(() => new ImageResourceLimits { MaxHeight = 1 }.EnsureCanvasWithinLimits(1, 2)));

        // Invalid arguments: ArgumentException family
        Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => new FrameDuration(-1, 1)));
        Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => new ImageResolution(0, 1)));
        Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => new AnimationMetadata { TotalPlays = 0 }));
        Assert.IsType<ArgumentOutOfRangeException>(Record.Exception(() => new ImageMetadata { Orientation = (ExifOrientation)9 }));
        Assert.IsType<ArgumentNullException>(Record.Exception(() => new ExifProfile(null!)));
        Assert.IsType<ArgumentException>(Record.Exception(() => new PixelConversionOptions { BackgroundColor = new Rgba64(0, 0, 0, 1) }));

        // Overflowing conversions: OverflowException
        Assert.IsType<OverflowException>(Record.Exception(() => new FrameDuration(long.MaxValue, 1).ToTimeSpan()));

        // Unsupported pixel types: NotSupportedException before anything else
        Assert.IsType<NotSupportedException>(Record.Exception(() => new Image<int>(0, 0)));
    }
}
