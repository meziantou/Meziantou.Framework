using System.IO.Compression;
using Meziantou.Framework.Imaging.Drawing;
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
        Assert.Equal(256L * 1024 * 1024, limits.MaxFontBytes);
        Assert.Equal(1_000_000, limits.MaxGlyphComplexity);
        Assert.Equal(1_000_000, limits.MaxTextLength);
        Assert.Equal(1_000_000, limits.MaxTextGlyphs);
        Assert.Equal(100_000, limits.MaxTextLines);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxFontBytes = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxGlyphComplexity = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxTextLength = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxTextGlyphs = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageResourceLimits { MaxTextLines = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageConfiguration { MaxDegreeOfParallelism = 0 });
        Assert.Throws<ArgumentNullException>(() => new ImageConfiguration { Limits = null! });
    }

    [Fact]
    public void EncoderDefaults()
    {
        var png = new PngEncoder();
        Assert.Equal(ImageFormat.Png, png.Format);
        Assert.Equal(PngAnimationMode.Auto, png.AnimationMode);
        Assert.Equal(PngPixelLayout.Auto, png.PixelLayout);
        Assert.Equal(PngOptimization.None, png.Optimization);
        Assert.Equal(CompressionLevel.Optimal, png.CompressionLevel);
        Assert.Equal(PngFilter.Adaptive, png.Filter);
        Assert.False(png.Interlaced);
        Assert.Equal(FrameDurationRounding.RequireExact, png.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, png.MetadataHandling);

        var gif = new GifEncoder();
        Assert.Equal(GifAlphaMode.Threshold, gif.AlphaMode);
        Assert.Equal(128, gif.AlphaThreshold);
        Assert.Null(gif.BackgroundColor);
        Assert.Equal(DitheringMode.None, gif.Quantization.Dithering);
        Assert.Equal(256, gif.Quantization.MaxColors);
        Assert.Equal(FrameDurationRounding.RoundToNearest, gif.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, gif.MetadataHandling);

        var jpeg = new JpegEncoder();
        Assert.Equal(90, jpeg.Quality);
        Assert.Equal(JpegChromaSubsampling.Auto, jpeg.ChromaSubsampling);
        Assert.Equal(JpegEncodingProcess.Baseline, jpeg.Process);
        Assert.Null(jpeg.BackgroundColor);
        Assert.False(jpeg.AllowBitDepthReduction);
        Assert.Equal(MetadataHandling.Strict, jpeg.MetadataHandling);

        var avif = new AvifEncoder();
        Assert.Equal(ImageFormat.Avif, avif.Format);
        Assert.Equal(AvifCompression.Lossless, avif.Compression);
        Assert.Equal(75, avif.Quality);
        Assert.Equal(5, avif.Effort);
        Assert.Equal(AvifChromaSubsampling.Auto, avif.ChromaSubsampling);
        Assert.Null(avif.BitDepth);
        Assert.False(avif.AllowBitDepthReduction);
        Assert.False(avif.ClearTransparentColors);
        Assert.Null(avif.CleanAperture);
        Assert.False(avif.PremultiplyAlpha);
        Assert.Null(avif.GridTileSize);
        Assert.False(avif.AutoTiling);
        Assert.Equal(0, avif.KeyFrameInterval);
        Assert.Equal(FrameDurationRounding.RequireExact, avif.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, avif.MetadataHandling);

        var heic = new HeicEncoder();
        Assert.Equal(ImageFormat.Heic, heic.Format);
        Assert.Equal(HeicCompression.Lossless, heic.Compression);
        Assert.Equal(75, heic.Quality);
        Assert.Equal(5, heic.Effort);
        Assert.Equal(HeicChromaSubsampling.Auto, heic.ChromaSubsampling);
        Assert.Null(heic.BitDepth);
        Assert.False(heic.AllowBitDepthReduction);
        Assert.False(heic.ClearTransparentColors);
        Assert.Null(heic.GridTileSize);
        Assert.Equal(0, heic.KeyFrameInterval);
        Assert.Equal(FrameDurationRounding.RequireExact, heic.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, heic.MetadataHandling);

        var jpeg2000 = new Jpeg2000Encoder();
        Assert.Equal(ImageFormat.Jpeg2000, jpeg2000.Format);
        Assert.Equal(Jpeg2000Compression.Lossless, jpeg2000.Compression);
        Assert.Equal(90, jpeg2000.Quality);
        Assert.Equal(Jpeg2000Container.Jp2, jpeg2000.Container);
        Assert.Equal(MetadataHandling.Strict, jpeg2000.MetadataHandling);

        var exr = new OpenExrEncoder();
        Assert.Equal(ImageFormat.OpenExr, exr.Format);
        Assert.Equal(OpenExrCompression.Zip, exr.Compression);
        Assert.Null(exr.PixelType);
        Assert.False(exr.AllowBitDepthReduction);
        Assert.Equal(MetadataHandling.Strict, exr.MetadataHandling);
    }

    [Fact]
    public void EncoderSettingsAreValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegEncoder { Quality = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new JpegEncoder { Quality = 101 });
        Assert.Throws<ArgumentException>(() => new JpegEncoder { BackgroundColor = new Rgba32(1, 2, 3, 254) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder { Quantization = new QuantizeOptions { MaxColors = 1 } });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GifEncoder { Quantization = new QuantizeOptions { MaxColors = 257 } });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngEncoder { Filter = (PngFilter)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngEncoder { PixelLayout = (PngPixelLayout)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngEncoder { Optimization = (PngOptimization)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new PngEncoder { MetadataHandling = (MetadataHandling)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { Effort = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { BitDepth = 9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { DurationRounding = (FrameDurationRounding)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { KeyFrameInterval = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { CleanAperture = new Rectangle(0, 0, 0, 4) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { CleanAperture = new Rectangle(-1, 0, 4, 4) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { GridTileSize = new Size(63, 64) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AvifEncoder { GridTileSize = new Size(64, 65537) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Compression = (HeicCompression)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Quality = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { Effort = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { ChromaSubsampling = (HeicChromaSubsampling)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { BitDepth = 9 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { BitDepth = 16 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { DurationRounding = (FrameDurationRounding)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { KeyFrameInterval = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { GridTileSize = new Size(63, 64) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HeicEncoder { GridTileSize = new Size(64, 65529) });
        Assert.Equal(new Rgba64(65535, 0, 0), new JpegEncoder { BackgroundColor = new Rgba32(255, 0, 0) }.BackgroundColor);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Quality = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Compression = (Jpeg2000Compression)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new Jpeg2000Encoder { Container = (Jpeg2000Container)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenExrEncoder { Compression = (OpenExrCompression)5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenExrEncoder { PixelType = (OpenExrPixelType)2 });
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
        Assert.Equal(PnmPixelLayout.Auto, Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.pnm")).PixelLayout);
        Assert.Equal(PnmPixelLayout.Auto, Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.pam")).PixelLayout);
        Assert.Equal(PnmPixelLayout.Auto, Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.ppm")).PixelLayout);
        Assert.Equal(PnmPixelLayout.Auto, Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.pgm")).PixelLayout);
        Assert.Equal(PnmPixelLayout.Bitmap, Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("a.pbm")).PixelLayout);
        Assert.Equal(PnmPixelLayout.Bitmap, Assert.IsType<PnmEncoder>(ImageEncoder.FromPath("dir/a.PBM")).PixelLayout);
        Assert.IsType<TiffEncoder>(ImageEncoder.FromPath("a.tif"));
        Assert.IsType<TiffEncoder>(ImageEncoder.FromPath("a.TIFF"));
        Assert.IsType<IcoEncoder>(ImageEncoder.FromPath("a.ico"));
        Assert.Equal(IconKind.Cursor, Assert.IsType<IcoEncoder>(ImageEncoder.FromPath("a.cur")).Kind);
        Assert.IsType<AniEncoder>(ImageEncoder.FromPath("a.ANI"));
        Assert.IsType<JpegXlEncoder>(ImageEncoder.FromPath("a.jxl"));
        Assert.IsType<JpegXlEncoder>(ImageEncoder.FromPath("dir/a.JXL"));
        Assert.Equal(JpegXlCompression.Lossless, Assert.IsType<JpegXlEncoder>(ImageEncoder.FromPath("a.jxl")).Compression);
        Assert.Equal(OpenExrCompression.Zip, Assert.IsType<OpenExrEncoder>(ImageEncoder.FromPath("a.exr")).Compression);
        Assert.IsType<OpenExrEncoder>(ImageEncoder.FromPath("dir/a.EXR"));
        Assert.Equal(Jpeg2000Container.Jp2, Assert.IsType<Jpeg2000Encoder>(ImageEncoder.FromPath("a.jp2")).Container);
        Assert.Equal(Jpeg2000Container.Jp2, Assert.IsType<Jpeg2000Encoder>(ImageEncoder.FromPath("dir/a.JP2")).Container);
        foreach (var extension in new[] { ".j2k", ".j2c", ".jpc", ".J2K" })
        {
            Assert.Equal(Jpeg2000Container.Codestream, Assert.IsType<Jpeg2000Encoder>(ImageEncoder.FromPath("a" + extension)).Container);
        }

        // The other members of the family are read, never written
        foreach (var extension in new[] { ".jpx", ".jpf", ".jph", ".jhc" })
        {
            Assert.Contains("can be read but not written", Assert.Throws<ArgumentException>(() => ImageEncoder.FromPath("a" + extension)).Message, StringComparison.Ordinal);
        }

        Assert.Throws<ArgumentException>(() => ImageEncoder.FromPath("a.xyz"));
        Assert.Equal(HeicCompression.Lossless, Assert.IsType<HeicEncoder>(ImageEncoder.FromPath("a.heic")).Compression);
        Assert.IsType<HeicEncoder>(ImageEncoder.FromPath("a.heif"));
        Assert.IsType<HeicEncoder>(ImageEncoder.FromPath("dir/a.HEICS"));
        Assert.IsType<HeicEncoder>(ImageEncoder.FromPath("a.heifs"));

        Assert.Equal(AvifCompression.Lossless, Assert.IsType<AvifEncoder>(ImageEncoder.FromPath("a.avif")).Compression);
        Assert.IsType<AvifEncoder>(ImageEncoder.FromPath("dir/a.AVIFS"));
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
        Assert.True(options.AllowDownscaling);
        Assert.Null(options.BackgroundColor);
        Assert.Equal(ResamplingFilter.Bicubic, options.Filter);
        Assert.Equal(ResizeWorkingSpace.Encoded, options.WorkingSpace);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeOptions(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResizeOptions(Size.Empty));
    }

    [Fact]
    public void TransformDefaults()
    {
        foreach (var options in new[] { new TransformOptions(), TransformOptions.Default })
        {
            Assert.Equal(ResamplingFilter.Bicubic, options.Filter);
            Assert.Equal(default, options.BackgroundColor);
            Assert.Equal(TransformCanvasMode.Expand, options.CanvasMode);
            Assert.Equal(default, options.CanvasMode);
            Assert.Equal(Size.Empty, options.CanvasSize);
            Assert.Equal(ResizeWorkingSpace.Encoded, options.WorkingSpace);
        }
    }

    [Fact]
    public void HistogramDefaults()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistogramEqualizationOptions { Mode = (HistogramEqualizationMode)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistogramEqualizationOptions { ColorMode = (HistogramColorMode)2 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistogramEqualizationOptions { TileCountX = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistogramEqualizationOptions { TileCountY = HistogramEqualizationOptions.MaxTileCount + 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistogramEqualizationOptions { ClipLimit = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistogramEqualizationOptions { ClipLimit = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoLevelsOptions { LowPercentile = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoLevelsOptions { LowPercentile = 100 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoLevelsOptions { HighPercentile = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoLevelsOptions { HighPercentile = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoLevelsOptions { ColorMode = (HistogramColorMode)(-1) });

        Assert.Equal(HistogramEqualizationOptions.MaxTileCount, new HistogramEqualizationOptions { TileCountX = HistogramEqualizationOptions.MaxTileCount }.TileCountX);
        Assert.Equal(double.PositiveInfinity, new HistogramEqualizationOptions { ClipLimit = double.PositiveInfinity }.ClipLimit);
        Assert.Equal(0d, new AutoLevelsOptions { LowPercentile = 0 }.LowPercentile);
        Assert.Equal(100d, new AutoLevelsOptions { HighPercentile = 100 }.HighPercentile);

        var defaults = HistogramEqualizationOptions.Default;
        Assert.Equal(HistogramEqualizationMode.Global, defaults.Mode);
        Assert.Equal(HistogramColorMode.Luminance, defaults.ColorMode);
        Assert.Equal(8, defaults.TileCountX);
        Assert.Equal(8, defaults.TileCountY);
        Assert.Equal(2d, defaults.ClipLimit);
        Assert.False(defaults.ShareHistogramAcrossFrames);
        Assert.Equal(0d, AutoLevelsOptions.Default.LowPercentile);
        Assert.Equal(100d, AutoLevelsOptions.Default.HighPercentile);
        Assert.Equal(HistogramColorMode.Luminance, AutoLevelsOptions.Default.ColorMode);
        Assert.False(AutoLevelsOptions.Default.ShareHistogramAcrossFrames);
    }

    [Fact]
    public void ThresholdDefaults()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { Method = (ThresholdMethod)4 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { Level = -0.1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { Level = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { Level = 1.5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { WindowSize = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { WindowSize = 16 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { WindowSize = 20003 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { Offset = 1.1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { Offset = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { SauvolaK = -0.1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { SauvolaR = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { SauvolaR = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ThresholdOptions { EdgeMode = (ConvolutionEdgeMode)4 });

        Assert.Equal(20001, new ThresholdOptions { WindowSize = 20001 }.WindowSize);
        Assert.Equal(-1d, new ThresholdOptions { Offset = -1 }.Offset);
        Assert.Equal(1d, new ThresholdOptions { SauvolaR = 1 }.SauvolaR);

        var defaults = ThresholdOptions.Default;
        Assert.Equal(ThresholdMethod.Otsu, defaults.Method);
        Assert.Equal(0.5, defaults.Level);
        Assert.Equal(15, defaults.WindowSize);
        Assert.Equal(0.0, defaults.Offset);
        Assert.Equal(0.2, defaults.SauvolaK);
        Assert.Equal(0.5, defaults.SauvolaR);
        Assert.Equal(ConvolutionEdgeMode.Mirror, defaults.EdgeMode);
        Assert.Equal(new Rgba64(65535, 65535, 65535), defaults.UpperColor);
        Assert.Equal(new Rgba64(0, 0, 0), defaults.LowerColor);
        Assert.False(defaults.ShareHistogramAcrossFrames);
    }

    [Fact]
    public void AutoCropDefaults()
    {
        var options = new AutoCropOptions();
        Assert.Equal(0, options.PaddingX);
        Assert.Equal(0, options.PaddingY);
        Assert.Equal(35, options.ColorThreshold);
        Assert.Null(options.BucketThreshold);
        Assert.Equal(AutoCropPaddingMode.Expand, options.PaddingMode);
        Assert.Equal(default, options.PaddingMode);
        Assert.False(options.AnalyzeWeights);

        var defaults = AutoCropOptions.Default;
        Assert.Equal(35, defaults.ColorThreshold);
        Assert.Null(defaults.BucketThreshold);
        Assert.Equal(AutoCropPaddingMode.Expand, defaults.PaddingMode);

        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoCropOptions { PaddingX = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoCropOptions { PaddingY = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoCropOptions { ColorThreshold = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoCropOptions { ColorThreshold = 256 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoCropOptions { BucketThreshold = -0.5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoCropOptions { BucketThreshold = 1.5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoCropOptions { BucketThreshold = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AutoCropOptions { PaddingMode = (AutoCropPaddingMode)2 });
        Assert.Equal(255, new AutoCropOptions { ColorThreshold = 255 }.ColorThreshold);
        Assert.Equal(1d, new AutoCropOptions { BucketThreshold = 1 }.BucketThreshold);
        Assert.Equal(0d, new AutoCropOptions { BucketThreshold = 0 }.BucketThreshold);
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
        Assert.Equal(0d, options.Bias);
        Assert.Equal(default, options.EdgeMode);
        Assert.Equal(default, options.WorkingSpace);
    }

    [Fact]
    public void DrawingDefaults()
    {
        var brush = new SolidBrush(new Rgba32(1, 2, 3));
        var pen = new Pen(brush, 2.5);
        Assert.Same(brush, pen.Brush);
        Assert.Equal(2.5, pen.Width);
        Assert.Equal(LineJoin.Miter, pen.LineJoin);
        Assert.Equal(LineCap.Butt, pen.LineCap);
        Assert.Equal(4, pen.MiterLimit);
        Assert.Equal(default, pen.LineJoin);
        Assert.Equal(default, pen.LineCap);

        GradientStop[] stops = [new(0, new Rgba32(0, 0, 0)), new(1, new Rgba32(255, 255, 255))];
        Assert.Equal(GradientSpreadMode.Pad, new LinearGradientBrush(default, new PointD(1, 0), stops).SpreadMode);
        Assert.Equal(GradientSpreadMode.Pad, new RadialGradientBrush(default, 1, stops).SpreadMode);
        Assert.Equal(default, new RadialGradientBrush(default, 1, stops).SpreadMode);
        Assert.Equal(default, FillRule.NonZero);

        // The fill rule of a path is non-zero unless told otherwise: the inner square, drawn in the same direction, is
        // not a hole
        var path = new VectorPathBuilder().AddRectangle(new Rectangle(0, 0, 3, 3)).AddRectangle(new Rectangle(1, 1, 1, 1)).Build();
        using var image = new Image<Gray8>(3, 3, new Gray8(0));
        image.Draw(canvas => canvas.Fill(new SolidBrush(new Rgba32(255, 255, 255)), path), TestContext.Current.CancellationToken);
        Assert.Equal(new Gray8(255), image.Frames[0][1, 1]);
    }

    [Fact]
    public void ColorConversionDefaults()
    {
        var options = new IccColorTransformOptions();
        Assert.Equal(IccRenderingIntent.RelativeColorimetric, options.Intent);
        Assert.True(options.BlackPointCompensation);
        Assert.Equal(IccRenderingIntent.RelativeColorimetric, IccColorTransformOptions.Default.Intent);
        Assert.True(IccColorTransformOptions.Default.BlackPointCompensation);
        Assert.Same(IccColorTransformOptions.Default, IccColorTransformOptions.Default);
    }

    [Fact]
    public void CmykConversionDefaults()
    {
        // Inks are never converted implicitly: the conversion is absent by default
        Assert.Null(ImageDecodeOptions.Default.CmykConversion);
        Assert.Null(ImageReaderOptions.Default.CmykConversion);

        var options = new CmykConversionOptions();
        Assert.Same(IccProfile.Srgb, options.DestinationProfile);
        Assert.Same(IccProfile.Cgats001CompatibleCmyk, options.AssumedSourceProfile);
        Assert.Same(IccColorTransformOptions.Default, options.Transform);
        Assert.Same(CmykConversionOptions.Default, CmykConversionOptions.Default);
        Assert.Same(IccProfile.Srgb, CmykConversionOptions.Default.DestinationProfile);
        Assert.Same(IccProfile.Cgats001CompatibleCmyk, CmykConversionOptions.Default.AssumedSourceProfile);

        // The destination is an RGB profile, the assumed source a CMYK profile or none
        Assert.Null(new CmykConversionOptions { AssumedSourceProfile = null }.AssumedSourceProfile);
        Assert.Throws<ArgumentNullException>(() => new CmykConversionOptions { DestinationProfile = null! });
        Assert.Throws<ArgumentException>(() => new CmykConversionOptions { DestinationProfile = IccProfile.SrgbGray });
        Assert.Throws<ArgumentException>(() => new CmykConversionOptions { DestinationProfile = IccProfile.Cgats001CompatibleCmyk });
        Assert.Throws<ArgumentException>(() => new CmykConversionOptions { AssumedSourceProfile = IccProfile.Srgb });
        Assert.Throws<ArgumentNullException>(() => new CmykConversionOptions { Transform = null! });
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
