using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// The encoded inputs and decoded sources of the benchmark workloads, generated once per process from
/// <see cref="BenchmarkPixels"/> (never committed). Dimensions, frame counts, pixel types and codec settings are part of
/// each description and are printed in the benchmark reports.
/// </summary>
internal static class BenchmarkInputs
{
    public const int AnimationWidth = 320;
    public const int AnimationHeight = 240;
    public const int AnimationFrames = 48;

    private static readonly Lazy<byte[]> LargeRgb24Bytes = new(() => BenchmarkPixels.Rgb24(LargeWidth, LargeHeight, seed: 1));
    private static readonly Lazy<byte[]> SmallPngData = new(() => EncodeRgba32(SmallPngSource, 64, 64, new PngEncoder()));
    private static readonly Lazy<byte[]> LargePng8Data = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new PngEncoder()));
    private static readonly Lazy<byte[]> LargePng16Data = new(() => EncodeRgba64(LargeRgba64Source, Large16Width, Large16Height, new PngEncoder()));
    private static readonly Lazy<byte[]> BaselineJpegData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, JpegEncoder));
    private static readonly Lazy<byte[]> ProgressiveJpegData = new(() => ProgressiveJpegInput.Encode(LargeRgb24Bytes.Value, LargeWidth, LargeHeight, JpegQuality));
    private static readonly Lazy<byte[]> ServiceJpegData = new(() => EncodeRgb24(BenchmarkPixels.Rgb24(ServiceWidth, ServiceHeight, seed: 5), ServiceWidth, ServiceHeight, JpegEncoder));
    private static readonly Lazy<byte[]> GifData = new(() => EncodeAnimation(new GifEncoder()));
    private static readonly Lazy<byte[]> ApngData = new(() => EncodeAnimation(new PngEncoder { AnimationMode = PngAnimationMode.Animated }));
    private static readonly Lazy<byte[]> LossyWebPData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, WebPLossyEncoder));
    private static readonly Lazy<byte[]> LosslessWebPData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new WebPEncoder()));
    private static readonly Lazy<byte[]> TransparentWebPData = new(() => EncodeRgba32(ServiceRgba32Source, ServiceWidth, ServiceHeight, WebPLossyEncoder));
    private static readonly Lazy<byte[]> WebPAnimationData = new(() => EncodeAnimation(new WebPEncoder { Effort = 0 }));
    private static readonly Lazy<byte[]> QoiRgbData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new QoiEncoder()));
    private static readonly Lazy<byte[]> QoiRgbaData = new(() => EncodeRgba32(ServiceRgba32Source, ServiceWidth, ServiceHeight, new QoiEncoder()));
    private static readonly Lazy<byte[]> BmpRgbData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new BmpEncoder()));
    private static readonly Lazy<byte[]> BmpRgbaData = new(() => EncodeRgba32(ServiceRgba32Source, ServiceWidth, ServiceHeight, new BmpEncoder()));
    private static readonly Lazy<byte[]> TgaRgbData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new TgaEncoder { Compression = TgaCompression.RunLength }));
    private static readonly Lazy<byte[]> TgaRgbaData = new(() => EncodeRgba32(ServiceRgba32Source, ServiceWidth, ServiceHeight, new TgaEncoder { Compression = TgaCompression.RunLength }));
    private static readonly Lazy<byte[]> PnmRgbData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new PnmEncoder()));
    private static readonly Lazy<byte[]> PnmRgbaData = new(() => EncodeRgba32(ServiceRgba32Source, ServiceWidth, ServiceHeight, new PnmEncoder()));

    public static int LargeWidth => 2048;

    public static int LargeHeight => 1536;

    public static int Large16Width => 1024;

    public static int Large16Height => 768;

    public static int ResizeWidth => 1920;

    public static int ResizeHeight => 1080;

    public static int ServiceWidth => 1024;

    public static int ServiceHeight => 768;

    public static int JpegQuality => 85;

    /// <summary>Gets the JPEG encoder settings of every JPEG workload: quality 85, 4:2:0.</summary>
    public static JpegEncoder JpegEncoder => new() { Quality = JpegQuality, ChromaSubsampling = JpegChromaSubsampling.Ratio420 };

    /// <summary>Gets 64x64 RGBA 8-bit pixels with transparency.</summary>
    public static Rgba32[] SmallPngSource => BenchmarkPixels.Rgba32(64, 64, seed: 2, alpha: true);

    /// <summary>Gets 2048x1536 RGB 8-bit pixels.</summary>
    public static Rgb24[] LargeRgb24Source => unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Rgb24>(LargeRgb24Bytes.Value)).ToArray();

    /// <summary>Gets 1024x768 RGBA 16-bit pixels with transparency.</summary>
    public static Rgba64[] LargeRgba64Source => BenchmarkPixels.Rgba64(Large16Width, Large16Height, seed: 3, alpha: true);

    /// <summary>Gets 1920x1080 RGBA 8-bit pixels with transparency (resize source).</summary>
    public static Rgba32[] ResizeRgba32Source => BenchmarkPixels.Rgba32(ResizeWidth, ResizeHeight, seed: 4, alpha: true);

    /// <summary>Gets 1920x1080 RGBA 16-bit pixels with transparency (resize source).</summary>
    public static Rgba64[] ResizeRgba64Source => BenchmarkPixels.Rgba64(ResizeWidth, ResizeHeight, seed: 4, alpha: true);

    /// <summary>Gets a 64x64 RGBA 8-bit PNG (default <see cref="PngEncoder"/>: adaptive filters, optimal compression).</summary>
    public static byte[] SmallPng => SmallPngData.Value;

    /// <summary>Gets a 2048x1536 RGB 8-bit PNG (default <see cref="PngEncoder"/>).</summary>
    public static byte[] LargePng8 => LargePng8Data.Value;

    /// <summary>Gets a 1024x768 RGBA 16-bit PNG (default <see cref="PngEncoder"/>).</summary>
    public static byte[] LargePng16 => LargePng16Data.Value;

    /// <summary>Gets a 2048x1536 baseline JPEG (library encoder, quality 85, 4:2:0, Annex K Huffman tables).</summary>
    public static byte[] BaselineJpeg => BaselineJpegData.Value;

    /// <summary>Gets a 2048x1536 progressive JPEG of the same pixels (quality 85, 4:2:0, 10 scans with successive approximation).</summary>
    public static byte[] ProgressiveJpeg => ProgressiveJpegData.Value;

    /// <summary>Gets a 1024x768 baseline JPEG (quality 85, 4:2:0): the request payload of the service-style workload.</summary>
    public static byte[] ServiceJpeg => ServiceJpegData.Value;

    /// <summary>Gets a 320x240 GIF of 48 frames (default <see cref="GifEncoder"/>: 256 colors, no dithering), 40 ms per frame.</summary>
    public static byte[] Gif => GifData.Value;

    /// <summary>Gets a 320x240 APNG of 48 frames (default <see cref="PngEncoder"/>), 40 ms per frame.</summary>
    public static byte[] Apng => ApngData.Value;

    /// <summary>Gets the WebP lossy settings of the WebP workloads: quality 75, effort 5 (the defaults).</summary>
    public static WebPEncoder WebPLossyEncoder => new() { Compression = WebPCompression.Lossy, Quality = 75 };

    /// <summary>Gets the 1024x768 RGBA source with opaque, translucent and transparent areas of the transparent WebP workloads.</summary>
    public static Rgba32[] ServiceRgba32Source => BenchmarkPixels.Rgba32(ServiceWidth, ServiceHeight, seed: 7, alpha: true);

    /// <summary>Gets the 2048x1536 RGB source encoded as lossy WebP (quality 75, effort 5).</summary>
    public static byte[] LossyWebP => LossyWebPData.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as lossless WebP (effort 5).</summary>
    public static byte[] LosslessWebP => LosslessWebPData.Value;

    /// <summary>Gets the 1024x768 RGBA source encoded as lossy WebP with a lossless alpha plane (quality 75).</summary>
    public static byte[] TransparentWebP => TransparentWebPData.Value;

    /// <summary>Gets the animation of <see cref="CreateAnimation"/> encoded as lossless WebP (effort 0, full-canvas frames).</summary>
    public static byte[] WebPAnimation => WebPAnimationData.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as QOI (3 channels).</summary>
    public static byte[] QoiRgb => QoiRgbData.Value;

    /// <summary>Gets the 1024x768 RGBA source with transparency encoded as QOI (4 channels).</summary>
    public static byte[] QoiRgba => QoiRgbaData.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as a 24-bit BMP.</summary>
    public static byte[] BmpRgb => BmpRgbData.Value;

    /// <summary>Gets the 1024x768 RGBA source encoded as a 32-bit BMP with an explicit alpha mask.</summary>
    public static byte[] BmpRgba => BmpRgbaData.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as a run-length TGA.</summary>
    public static byte[] TgaRgb => TgaRgbData.Value;

    /// <summary>Gets the 1024x768 RGBA source encoded as a run-length TGA with eight alpha bits.</summary>
    public static byte[] TgaRgba => TgaRgbaData.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as a binary PPM.</summary>
    public static byte[] PnmRgb => PnmRgbData.Value;

    /// <summary>Gets the 1024x768 RGBA source encoded as a binary PAM (<c>RGB_ALPHA</c>).</summary>
    public static byte[] PnmRgba => PnmRgbaData.Value;

    /// <summary>Creates the source animation of <see cref="Gif"/> and <see cref="Apng"/>.</summary>
    public static Image<Rgba32> CreateAnimation()
    {
        var frames = BenchmarkPixels.AnimationFrames(AnimationWidth, AnimationHeight, AnimationFrames, seed: 6);
        var image = Image.ImportPixelData<Rgba32>(frames[0], AnimationWidth, AnimationHeight);
        try
        {
            image.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(40);
            for (var i = 1; i < frames.Length; i++)
            {
                var frame = image.AppendFrame();
                frame.Metadata.Duration = FrameDuration.FromMilliseconds(40);
                frame.ProcessPixelRows(frames[i], static (pixels, source) =>
                {
                    for (var y = 0; y < pixels.Height; y++)
                    {
                        source.AsSpan(y * pixels.Width, pixels.Width).CopyTo(pixels.GetRowSpan(y));
                    }
                });
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static byte[] EncodeAnimation(ImageEncoder encoder)
    {
        using var image = CreateAnimation();
        return Save(image, encoder);
    }

    private static byte[] EncodeRgba32(Rgba32[] pixels, int width, int height, ImageEncoder encoder)
    {
        using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
        return Save(image, encoder);
    }

    private static byte[] EncodeRgb24(Rgb24[] pixels, int width, int height, ImageEncoder encoder)
    {
        using var image = Image.ImportPixelData<Rgb24>(pixels, width, height);
        return Save(image, encoder);
    }

    private static byte[] EncodeRgb24(byte[] pixels, int width, int height, ImageEncoder encoder)
    {
        using var image = Image.ImportPixelBytes<Rgb24>(pixels, width, height);
        return Save(image, encoder);
    }

    private static byte[] EncodeRgba64(Rgba64[] pixels, int width, int height, ImageEncoder encoder)
    {
        using var image = Image.ImportPixelData<Rgba64>(pixels, width, height);
        return Save(image, encoder);
    }

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }
}
