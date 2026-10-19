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
    private static readonly Lazy<byte[]> InterlacedPng8Data = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new PngEncoder { Interlaced = true }));
    private static readonly Lazy<byte[]> PngGrayAlpha8Data = new(() => PngLayoutInput.Encode(GrayAlpha8Samples(ServiceRgba32Source), ServiceWidth, ServiceHeight, colorType: 4, bitDepth: 8));
    private static readonly Lazy<byte[]> PngRgb16Data = new(() => PngLayoutInput.Encode(Rgb16Samples(LargeRgba64Source, bigEndian: true), Large16Width, Large16Height, colorType: 2, bitDepth: 16));
    private static readonly Lazy<byte[]> LargePng16Data = new(() => EncodeRgba64(LargeRgba64Source, Large16Width, Large16Height, new PngEncoder()));
    private static readonly Lazy<byte[]> BaselineJpegData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, JpegEncoder));
    private static readonly Lazy<byte[]> ProgressiveJpegData = new(() => ProgressiveJpegInput.Encode(LargeRgb24Bytes.Value, LargeWidth, LargeHeight, JpegQuality));
    private static readonly Lazy<byte[]> InkJpegData = new(() => ProgressiveJpegInput.EncodeInks(LargeRgb24Bytes.Value, LargeWidth, LargeHeight, JpegQuality));
    private static readonly Lazy<byte[]> ServiceJpegData = new(() => EncodeRgb24(BenchmarkPixels.Rgb24(ServiceWidth, ServiceHeight, seed: 5), ServiceWidth, ServiceHeight, JpegEncoder));
    private static readonly Lazy<byte[]> GifData = new(() => EncodeAnimation(new GifEncoder()));
    private static readonly Lazy<byte[]> TransparentGifData = new(() => EncodeTransparentAnimation(new GifEncoder()));
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
    private static readonly Lazy<byte[]> BmpIndexed8Data = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new BmpEncoder { PixelLayout = BmpPixelLayout.Indexed8, AllowColorReduction = true }));
    private static readonly Lazy<byte[]> PngIndexed8Data = new(() => EncodeRgb24(IndexedRgb24Source, LargeWidth, LargeHeight, new PngEncoder { PixelLayout = PngPixelLayout.Indexed }));
    private static readonly Lazy<byte[]> TiffIndexed8Data = new(() => EncodeRgb24(IndexedRgb24Source, LargeWidth, LargeHeight, new TiffEncoder { PixelLayout = TiffPixelLayout.Indexed }));
    private static readonly Lazy<byte[]> TiffLzwData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new TiffEncoder { Compression = TiffCompression.Lzw }));
    private static readonly Lazy<byte[]> TiffDeflateData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new TiffEncoder { Compression = TiffCompression.Deflate }));
    private static readonly Lazy<byte[]> TiffDeflateRowStripsData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new TiffEncoder { Compression = TiffCompression.Deflate, RowsPerStrip = 1 }));
    private static readonly Lazy<byte[]> TiffPredictor8Data = new(() => TiffPredictorInput.Encode(LargeRgb24Bytes.Value, LargeWidth, LargeHeight, samplesPerPixel: 3, bitsPerSample: 8, bigEndian: false));
    private static readonly Lazy<byte[]> TiffPredictor16Data = new(() => TiffPredictorInput.Encode(unsafe(System.Runtime.InteropServices.MemoryMarshal.AsBytes(LargeRgba64Source.AsSpan())), Large16Width, Large16Height, samplesPerPixel: 4, bitsPerSample: 16, bigEndian: true));
    private static readonly Lazy<byte[]> PnmRgbData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new PnmEncoder()));
    private static readonly Lazy<byte[]> PnmRgbaData = new(() => EncodeRgba32(ServiceRgba32Source, ServiceWidth, ServiceHeight, new PnmEncoder()));
    private static readonly Lazy<byte[]> LargeBilevelBytes = new(() => BenchmarkPixels.Bilevel(LargeWidth, LargeHeight, seed: 8));
    private static readonly Lazy<byte[]> PnmBitmapData = new(() => EncodeGray8(LargeBilevelBytes.Value, LargeWidth, LargeHeight, new PnmEncoder { PixelLayout = PnmPixelLayout.Bitmap }));
    private static readonly Lazy<byte[]> PnmRgb16Data = new(() => [.. System.Text.Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"P6\n{Large16Width} {Large16Height}\n65535\n")), .. Rgb16Samples(LargeRgba64Source, bigEndian: true)]);
    private static readonly Lazy<byte[]> TiffUncompressedData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new TiffEncoder { Compression = TiffCompression.None }));
    private static readonly Lazy<byte[]> TiffBilevelData = new(() => EncodeGray8(LargeBilevelBytes.Value, LargeWidth, LargeHeight, new TiffEncoder { Compression = TiffCompression.None, PixelLayout = TiffPixelLayout.Bilevel }));
    private static readonly Lazy<byte[]> TiffCcittGroup4Data = new(() => EncodeGray8(LargeBilevelBytes.Value, LargeWidth, LargeHeight, new TiffEncoder { Compression = TiffCompression.CcittGroup4, PixelLayout = TiffPixelLayout.Bilevel }));
    private static readonly Lazy<byte[]> TiffRgb16Data = new(() => TiffPredictorInput.Encode(Rgb16Samples(LargeRgba64Source, bigEndian: !BitConverter.IsLittleEndian), Large16Width, Large16Height, samplesPerPixel: 3, bitsPerSample: 16, bigEndian: false, predictor: false));
    private static readonly Lazy<byte[]> TgaUncompressedData = new(() => EncodeRgb24(LargeRgb24Source, LargeWidth, LargeHeight, new TgaEncoder { Compression = TgaCompression.None }));
    private static readonly Lazy<byte[]> PngBilevelData = new(() => PngLayoutInput.Encode(PackBits(LargeBilevelBytes.Value), LargeWidth, LargeHeight, colorType: 0, bitDepth: 1));

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

    /// <summary>Gets the same pixels as an Adam7-interlaced RGB 8-bit PNG (<see cref="PngEncoder"/> with <see cref="PngEncoder.Interlaced"/>).</summary>
    public static byte[] InterlacedPng8 => InterlacedPng8Data.Value;

    /// <summary>Gets the 1024x768 RGBA source as a gray+alpha 8-bit PNG (gray <c>(r + 2g + b) / 4</c>), written by <see cref="PngLayoutInput"/>.</summary>
    public static byte[] PngGrayAlpha8 => PngGrayAlpha8Data.Value;

    /// <summary>Gets the pixels <see cref="PngGrayAlpha8"/> decodes to.</summary>
    public static Rgba32[] GrayAlpha8Pixels => [.. ServiceRgba32Source.Select(static p => new Rgba32(Luma(p), Luma(p), Luma(p), p.A))];

    /// <summary>Gets the 1024x768 RGBA 16-bit source without its alpha as an RGB 16-bit PNG, written by <see cref="PngLayoutInput"/>.</summary>
    public static byte[] PngRgb16 => PngRgb16Data.Value;

    /// <summary>Gets the pixels <see cref="PngRgb16"/> decodes to.</summary>
    public static Rgba64[] Rgb16Pixels => [.. LargeRgba64Source.Select(static p => new Rgba64(p.R, p.G, p.B))];

    /// <summary>Gets a 1024x768 RGBA 16-bit PNG (default <see cref="PngEncoder"/>).</summary>
    public static byte[] LargePng16 => LargePng16Data.Value;

    /// <summary>Gets a 2048x1536 baseline JPEG (library encoder, quality 85, 4:2:0, Annex K Huffman tables).</summary>
    public static byte[] BaselineJpeg => BaselineJpegData.Value;

    /// <summary>Gets a 2048x1536 progressive JPEG of the same pixels (quality 85, 4:2:0, 10 scans with successive approximation).</summary>
    public static byte[] ProgressiveJpeg => ProgressiveJpegData.Value;

    /// <summary>Gets a 2048x1536 baseline JPEG of four components (YCCK inks without profile, quality 85, 4:2:0, full-size black plane).</summary>
    public static byte[] InkJpeg => InkJpegData.Value;

    /// <summary>Gets the options that load <see cref="InkJpeg"/>: the conversion of its inks to sRGB from the CMYK profile the library assumes.</summary>
    public static ImageDecodeOptions InkJpegOptions { get; } = new() { CmykConversion = CmykConversionOptions.Default };

    /// <summary>Gets a 1024x768 baseline JPEG (quality 85, 4:2:0): the request payload of the service-style workload.</summary>
    public static byte[] ServiceJpeg => ServiceJpegData.Value;

    /// <summary>Gets a 320x240 GIF of 48 frames (default <see cref="GifEncoder"/>: 256 colors, no dithering), 40 ms per frame.</summary>
    public static byte[] Gif => GifData.Value;

    /// <summary>
    /// Gets a 320x240 GIF of 48 frames (default <see cref="GifEncoder"/>), 40 ms per frame, with a transparent index:
    /// every frame is composited over the canvas.
    /// </summary>
    public static byte[] TransparentGif => TransparentGifData.Value;

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

    /// <summary>Gets the 2048x1536 RGB source reduced to 256 colors and encoded as an 8-bit indexed BMP.</summary>
    public static byte[] BmpIndexed8 => BmpIndexed8Data.Value;

    /// <summary>Gets the 256 colors of <see cref="BmpIndexed8"/> as 2048x1536 RGB 8-bit pixels: the source of the other indexed inputs.</summary>
    public static Rgb24[] IndexedRgb24Source
    {
        get
        {
            using var image = Image.Load<Rgb24>(BmpIndexed8);
            var pixels = new Rgb24[image.Width * image.Height];
            image.Frames[0].CopyPixelDataTo(pixels);
            return pixels;
        }
    }

    /// <summary>Gets <see cref="IndexedRgb24Source"/> encoded as an 8-bit indexed PNG (default compression, adaptive filters).</summary>
    public static byte[] PngIndexed8 => PngIndexed8Data.Value;

    /// <summary>Gets <see cref="IndexedRgb24Source"/> encoded as an uncompressed 8-bit indexed TIFF.</summary>
    public static byte[] TiffIndexed8 => TiffIndexed8Data.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as an LZW TIFF (<see cref="TiffEncoder"/>, strips of 64 KB, no predictor).</summary>
    public static byte[] TiffLzw => TiffLzwData.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as a Deflate TIFF (<see cref="TiffEncoder"/>, strips of 64 KB, no predictor).</summary>
    public static byte[] TiffDeflate => TiffDeflateData.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as a Deflate TIFF of one row (6 KB) per strip, the size libtiff writes by default.</summary>
    public static byte[] TiffDeflateRowStrips => TiffDeflateRowStripsData.Value;

    /// <summary>Gets the 2048x1536 RGB source as an uncompressed little-endian TIFF with the horizontal differencing predictor.</summary>
    public static byte[] TiffPredictor8 => TiffPredictor8Data.Value;

    /// <summary>Gets the 1024x768 RGBA 16-bit source as an uncompressed big-endian TIFF with the horizontal differencing predictor.</summary>
    public static byte[] TiffPredictor16 => TiffPredictor16Data.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as a binary PPM.</summary>
    public static byte[] PnmRgb => PnmRgbData.Value;

    /// <summary>Gets the 1024x768 RGBA source encoded as a binary PAM (<c>RGB_ALPHA</c>).</summary>
    public static byte[] PnmRgba => PnmRgbaData.Value;

    /// <summary>Gets the 2048x1536 black-and-white text page as 8-bit gray samples (0 or 255): the source of the bilevel inputs.</summary>
    public static Gray8[] LargeBilevelSource => [.. LargeBilevelBytes.Value.Select(static value => new Gray8(value))];

    /// <summary>Gets the 2048x1536 black-and-white text page encoded as a binary PBM.</summary>
    public static byte[] PnmBitmap => PnmBitmapData.Value;

    /// <summary>Gets the RGB samples of the 1024x768 16-bit source as a binary PPM with <c>MAXVAL</c> 65535 (48 bits per pixel).</summary>
    public static byte[] PnmRgb16 => PnmRgb16Data.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as an uncompressed TIFF (<see cref="TiffEncoder"/>, strips of 64 KB).</summary>
    public static byte[] TiffUncompressed => TiffUncompressedData.Value;

    /// <summary>Gets the 2048x1536 black-and-white text page encoded as an uncompressed bilevel TIFF (1 bit per pixel, <c>WhiteIsZero</c>).</summary>
    public static byte[] TiffBilevel => TiffBilevelData.Value;

    /// <summary>Gets the 2048x1536 black-and-white text page encoded as a bilevel TIFF compressed with CCITT Group 4.</summary>
    public static byte[] TiffCcittGroup4 => TiffCcittGroup4Data.Value;

    /// <summary>Gets the RGB samples of the 1024x768 16-bit source as an uncompressed little-endian TIFF (48 bits per pixel, no predictor).</summary>
    public static byte[] TiffRgb16 => TiffRgb16Data.Value;

    /// <summary>Gets the 2048x1536 RGB source encoded as an uncompressed TGA.</summary>
    public static byte[] TgaUncompressed => TgaUncompressedData.Value;

    /// <summary>Gets the 2048x1536 black-and-white text page encoded as a 1-bit grayscale PNG.</summary>
    public static byte[] PngBilevel => PngBilevelData.Value;

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

    private static byte[] EncodeTransparentAnimation(ImageEncoder encoder)
    {
        // The animation of CreateAnimation with transparent 16x16 cells in a diagonal pattern
        using var image = CreateAnimation();
        foreach (var frame in image.Frames)
        {
            frame.ProcessPixelBytes(static pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < pixels.Width; x++)
                    {
                        if (((x / 16) + (y / 16)) % 3 == 0)
                        {
                            row[(x * 4) + 3] = 0;
                        }
                    }
                }
            });
        }

        return Save(image, encoder);
    }

    private static byte Luma(Rgba32 pixel) => (byte)((pixel.R + (2 * pixel.G) + pixel.B) / 4);

    private static byte[] GrayAlpha8Samples(Rgba32[] pixels)
    {
        var samples = new byte[pixels.Length * 2];
        for (var i = 0; i < pixels.Length; i++)
        {
            samples[i * 2] = Luma(pixels[i]);
            samples[(i * 2) + 1] = pixels[i].A;
        }

        return samples;
    }

    private static byte[] Rgb16Samples(Rgba64[] pixels, bool bigEndian)
    {
        var samples = new byte[pixels.Length * 6];
        for (var i = 0; i < pixels.Length; i++)
        {
            Write(samples.AsSpan(i * 6), pixels[i].R);
            Write(samples.AsSpan((i * 6) + 2), pixels[i].G);
            Write(samples.AsSpan((i * 6) + 4), pixels[i].B);
        }

        return samples;

        void Write(Span<byte> destination, ushort value)
        {
            if (bigEndian)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(destination, value);
            }
            else
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
            }
        }
    }

    /// <summary>Packs 8-bit black-and-white samples to one bit per pixel, a white pixel as a set bit and the first pixel in the most significant bit.</summary>
    private static byte[] PackBits(byte[] samples)
    {
        var rowLength = (LargeWidth + 7) / 8;
        var packed = new byte[rowLength * LargeHeight];
        for (var y = 0; y < LargeHeight; y++)
        {
            for (var x = 0; x < LargeWidth; x++)
            {
                if (samples[(y * LargeWidth) + x] != 0)
                {
                    packed[(y * rowLength) + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
        }

        return packed;
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

    private static byte[] EncodeGray8(byte[] pixels, int width, int height, ImageEncoder encoder)
    {
        using var image = Image.ImportPixelBytes<Gray8>(pixels, width, height);
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
