using System.IO.Compression;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>The documented defaults (limits, options and encoders) must match the code.</summary>
public sealed class DefaultsTests
{
    [Fact]
    public void ResourceLimitDefaults()
    {
        var limits = ImageResourceLimits.Default;
        Assert.Equal(32_768, limits.MaxWidth);
        Assert.Equal(32_768, limits.MaxHeight);
        Assert.Equal(100_000_000, limits.MaxFramePixels);
        Assert.Equal(1_000, limits.MaxFrames);
        Assert.Equal(1_000_000_000, limits.MaxTotalPixels);
        Assert.Equal(256L * 1024 * 1024, limits.MaxEncodedBytes);
        Assert.Equal(16L * 1024 * 1024, limits.MaxMetadataBytes);
        Assert.Equal(512L * 1024 * 1024, limits.MaxLiveAllocationBytes);
        Assert.Same(limits, ImageConfiguration.Default.Limits);
        Assert.Equal(1, ImageConfiguration.Default.MaxDegreeOfParallelism);
    }

    [Fact]
    public void ResourceLimitsMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxWidth = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxHeight = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxFramePixels = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxFrames = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxTotalPixels = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxEncodedBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxMetadataBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxLiveAllocationBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageConfiguration { MaxDegreeOfParallelism = 0 });
        Assert.Throws<ArgumentNullException>(() => new ImageConfiguration { Limits = null! });
    }

    [Fact]
    public void EncoderDefaults()
    {
        var png = new PngEncoder();
        Assert.Equal(ImageFormat.Png, png.Format);
        Assert.Equal(PngAnimationMode.Auto, png.AnimationMode);
        Assert.Equal(CompressionLevel.Optimal, png.CompressionLevel);
        Assert.Equal(PngFilter.Adaptive, png.Filter);
        Assert.False(png.Interlaced);
        Assert.Equal(FrameDurationRounding.RequireExact, png.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, png.MetadataHandling);

        var gif = new GifEncoder();
        Assert.Equal(GifAlphaMode.Threshold, gif.AlphaMode);
        Assert.Equal(128, gif.AlphaThreshold);
        Assert.Null(gif.BackgroundColor);
        Assert.Equal(GifDithering.None, gif.Dithering);
        Assert.Equal(256, gif.MaxColors);
        Assert.Equal(FrameDurationRounding.RoundToNearest, gif.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, gif.MetadataHandling);

        var jpeg = new JpegEncoder();
        Assert.Equal(90, jpeg.Quality);
        Assert.Equal(JpegChromaSubsampling.Auto, jpeg.ChromaSubsampling);
        Assert.Null(jpeg.BackgroundColor);
        Assert.False(jpeg.AllowBitDepthReduction);
        Assert.Equal(MetadataHandling.Strict, jpeg.MetadataHandling);
    }

    [Fact]
    public void EncoderSettingsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegEncoder { Quality = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegEncoder { Quality = 101 });
        Assert.Throws<ArgumentException>(() => new JpegEncoder { BackgroundColor = new Rgba32(1, 2, 3, 254) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder { MaxColors = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder { MaxColors = 257 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngEncoder { Filter = (PngFilter)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngEncoder { MetadataHandling = (MetadataHandling)42 });
        Assert.Equal(new Rgba64(65535, 0, 0), new JpegEncoder { BackgroundColor = new Rgba32(255, 0, 0) }.BackgroundColor);
    }

    [Fact]
    public void EncoderSelectionFromExtension()
    {
        Assert.IsType<PngEncoder>(ImageEncoder.FromPath("a.PNG"));
        Assert.Equal(PngAnimationMode.Animated, Assert.IsType<PngEncoder>(ImageEncoder.FromPath("a.apng")).AnimationMode);
        Assert.IsType<GifEncoder>(ImageEncoder.FromPath("dir/a.gif"));
        Assert.IsType<JpegEncoder>(ImageEncoder.FromPath("a.jpeg"));
        Assert.IsType<JpegEncoder>(ImageEncoder.FromPath("a.jpg"));
        Assert.IsType<WebPEncoder>(ImageEncoder.FromPath("a.webp"));
        Assert.IsType<QoiEncoder>(ImageEncoder.FromPath("a.qoi"));
        Assert.IsType<BmpEncoder>(ImageEncoder.FromPath("a.BMP"));
        Assert.IsType<BmpEncoder>(ImageEncoder.FromPath("a.dib"));
        Assert.IsType<TgaEncoder>(ImageEncoder.FromPath("a.tga"));
        Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.pnm"));
        Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.pam"));
        Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.ppm"));
        Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.pgm"));
        Assert.IsType<TiffEncoder>(ImageEncoder.FromPath("a.tif"));
        Assert.IsType<TiffEncoder>(ImageEncoder.FromPath("a.TIFF"));
        Assert.IsType<IcoEncoder>(ImageEncoder.FromPath("a.ico"));
        Assert.Equal(IconKind.Cursor, Assert.IsType<IcoEncoder>(ImageEncoder.FromPath("a.cur")).Kind);
        Assert.IsType<AniEncoder>(ImageEncoder.FromPath("a.ANI"));
        Assert.Throws<ArgumentException>(() => ImageEncoder.FromPath("a.heic"));
        Assert.Throws<ArgumentException>(() => ImageEncoder.FromPath("noextension"));
    }

    [Fact]
    public void ResizeDefaults()
    {
        var options = new ResizeOptions(10, 20);
        Assert.Equal(new Size(10, 20), options.Size);
        Assert.Equal(ResizeMode.Contain, options.Mode);
        Assert.Equal(ResizeAnchor.Center, options.Anchor);
        Assert.True(options.AllowUpscaling);
        Assert.Equal(ResamplingFilter.Bicubic, options.Filter);
        Assert.Equal(ResizeWorkingSpace.Encoded, options.WorkingSpace);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeOptions(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeOptions(Size.Empty));
    }

    [Fact]
    public void ConvolutionDefaults()
    {
        var kernel = new ConvolutionKernel(3, 1, [1, 2, 3]);
        var options = new ConvolutionOptions(kernel);
        Assert.Same(kernel, options.Kernel);
        Assert.Equal(ConvolutionEdgeMode.Clamp, options.EdgeMode);
        Assert.False(options.PreserveAlpha);
        Assert.Equal(ConvolutionWorkingSpace.Encoded, options.WorkingSpace);
        Assert.Equal(default, options.EdgeMode);
        Assert.Equal(default, options.WorkingSpace);
    }

    [Fact]
    public void IoOptionDefaults()
    {
        Assert.Null(ImageDecodeOptions.Default.FrameLimit);
        Assert.Same(PixelConversionOptions.Default, ImageDecodeOptions.Default.Conversion);
        Assert.Null(PixelConversionOptions.Default.BackgroundColor);
        Assert.False(PixelConversionOptions.Default.DiscardIncompatibleColorProfile);
        Assert.Equal(ImageIdentifyMode.Header, ImageIdentifyOptions.Default.Mode);
        Assert.True(ImageReaderOptions.Default.LeaveOpen);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageDecodeOptions { FrameLimit = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageReaderOptions { FrameLimit = -1 });

        var writer = new ImageWriterOptions(new Size(4, 3));
        Assert.True(writer.LeaveOpen);
        Assert.Null(writer.Encoder);
        Assert.Null(writer.ExpectedFrameCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageWriterOptions(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageWriterOptions(new Size(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageWriterOptions(1, 1) { ExpectedFrameCount = 0 });
    }

    [Fact]
    public void AnimationTotalPlays()
    {
        var animation = new AnimationMetadata();
        Assert.Null(animation.TotalPlays); // infinite
        animation.TotalPlays = 1;
        Assert.Equal(1, animation.Clone().TotalPlays);
        Assert.Throws<ArgumentOutOfRangeException>(() => animation.TotalPlays = 0);
    }
}
