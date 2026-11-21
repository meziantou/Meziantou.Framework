using System.Runtime.InteropServices;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

/// <summary>
/// The Radiance HDR, PSD, DNG, DDS, DPX, Cineon and KTX 2.0 codecs of <c>Meziantou.Framework.Imaging</c> (and the
/// decoders of the BC1 to BC7 blocks, and the encoders of the BCn, ETC2, EAC and ASTC blocks), the OpenEXR encoder (its
/// tiles, levels and compressions), the image data of PNG files (inflation and
/// reconstruction), the lossy compression of its GIF encoder, the separate planes of its TIFF encoder, the structure of the outputs of its JPEG 2000 encoder, and its processing
/// operations that read pixels or points
/// they do not control: custom processors, seam carving, inpainting, color selections and their masks, line and corner detection, skew estimation,
/// homography estimation and resizing; the conversions between the pixel formats (and from CMYK inks with the device
/// formulas, with the clones and vertical flips of the inks); the merging of the pieces of strokes;
/// the optimal line breaking of text layouts; the texts drawn from glyph masks; the text watermarks; the ICC profiles that the color conversions read (named colors, the
/// processing elements of iccMAX profiles); the options of the WebP encoder that change its pixels or its output
/// (bit-depth dithering, sharp YUV, non-seekable animations); and the layers of JPEG XL images and the regions of its
/// optimized animations. The inputs of the processing targets are small images whose pixel format, size, frames and
/// bytes come from the fuzzer, followed by the options of the operation.
/// </summary>
public static partial class ImagingFuzzTargets
{
    /// <summary>The bytes that select the formats, the sizes and the options, and the extreme samples.</summary>
    private static readonly string[] Tokens =
    [
        "\0", "\u0001", "\u0002", "\u0003", "\u0004", "\u0007", "\u0008", "\u000F", "\u0010", "\u001F", "\u007F",
        "\0\0\0\0", "\u007F\u007F\u007F\u007F", "\0\u007F\0\u007F",
    ];

    /// <summary>The pixel formats of the images of the inputs.</summary>
    private static readonly PixelFormat[] Formats =
    [
        PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.Gray8, PixelFormat.Gray16, PixelFormat.Rgba64, PixelFormat.La16,
        PixelFormat.RgbaFloat, PixelFormat.RgbaHalf, PixelFormat.Bgr565, PixelFormat.A8, PixelFormat.Bgra32, PixelFormat.Rgb48,
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("imaging-radiance", RadianceTarget) { Seeds = GetRadianceSeeds, Dictionary = RadianceTokens },
        new("imaging-psd", PsdTarget) { Seeds = GetPsdSeeds, Dictionary = PsdTokens },
        new("imaging-dng", DngTarget) { Seeds = GetDngSeeds, Dictionary = DngTokens },
        new("imaging-dds", DdsTarget) { Seeds = GetDdsSeeds, Dictionary = DdsTokens },
        new("imaging-dds-blocks", DdsBlockTarget) { Seeds = GetDdsBlockSeeds, Dictionary = Tokens },
        new("imaging-dpx", DpxTarget) { Seeds = GetDpxSeeds, Dictionary = DpxTokens },
        new("imaging-cineon", CineonTarget) { Seeds = GetCineonSeeds, Dictionary = DpxTokens },
        new("imaging-ktx2", Ktx2Target) { Seeds = GetKtx2Seeds, Dictionary = Ktx2Tokens },
        new("imaging-psd-encoder", PsdEncoderTarget) { Seeds = GetPsdEncoderSeeds, Dictionary = Tokens },
        new("imaging-dng-encoder", DngEncoderTarget) { Seeds = GetDngEncoderSeeds, Dictionary = Tokens },
        new("imaging-cineon-encoder", CineonEncoderTarget) { Seeds = GetCineonEncoderSeeds, Dictionary = Tokens },
        new("imaging-texture-blocks", TextureBlockTarget) { Seeds = GetTextureBlockSeeds, Dictionary = Tokens },
        new("imaging-texture-encoders", TextureEncoderTarget) { Seeds = GetTextureEncoderSeeds, Dictionary = Tokens },
        new("imaging-openexr-encoder", OpenExrEncoderTarget) { Seeds = GetOpenExrEncoderSeeds, Dictionary = Tokens },
        new("imaging-png-image-data", PngImageDataTarget) { Seeds = GetPngImageDataSeeds, Dictionary = PngImageDataTokens },
        new("imaging-png-zlib", PngZlibTarget) { Seeds = GetPngZlibSeeds, Dictionary = PngImageDataTokens },
        new("imaging-gif-lossy", GifLossyTarget) { Seeds = GetGifLossySeeds, Dictionary = Tokens },
        new("imaging-processor", ProcessorTarget) { Seeds = GetProcessorSeeds, Dictionary = Tokens },
        new("imaging-seam-carving", SeamCarvingTarget) { Seeds = GetSeamCarvingSeeds, Dictionary = Tokens },
        new("imaging-inpainting", InpaintingTarget) { Seeds = GetInpaintingSeeds, Dictionary = Tokens },
        new("imaging-color-selection", ColorSelectionTarget) { Seeds = GetColorSelectionSeeds, Dictionary = Tokens },
        new("imaging-hough-lines", LinesTarget) { Seeds = GetLinesSeeds, Dictionary = Tokens },
        new("imaging-corners", CornersTarget) { Seeds = GetCornersSeeds, Dictionary = Tokens },
        new("imaging-homography", HomographyTarget) { Seeds = GetHomographySeeds, Dictionary = Tokens },
        new("imaging-skew", SkewTarget) { Seeds = GetSkewSeeds, Dictionary = Tokens },
        new("imaging-stroke", StrokeTarget) { Seeds = GetStrokeSeeds, Dictionary = StrokeTokens },
        new("imaging-sweep-pieces", SweepPiecesTarget) { Seeds = GetSweepPiecesSeeds, Dictionary = SweepPiecesTokens },
        new("imaging-resize", ResizeTarget) { Seeds = GetResizeSeeds, Dictionary = Tokens },
        new("imaging-icc-named-colors", IccNamedColorsTarget) { Seeds = GetIccSeeds, Dictionary = IccTokens },
        new("imaging-icc-conversion", IccProfileConversionTarget) { Seeds = GetIccSeeds, Dictionary = IccTokens },
        new("imaging-pixel-conversion", PixelConversionTarget) { Seeds = GetPixelConversionSeeds, Dictionary = Tokens },
        new("imaging-device-cmyk", DeviceCmykTarget) { Seeds = GetDeviceCmykSeeds, Dictionary = Tokens },
        new("imaging-line-breaking", LineBreakingTarget) { Seeds = GetLineBreakingSeeds, Dictionary = LineBreakingTokens },
        new("imaging-glyph-masks", GlyphMasksTarget) { Seeds = GetGlyphMaskSeeds, Dictionary = GlyphMaskTokens },
        new("imaging-watermark", WatermarkTarget) { Seeds = GetWatermarkSeeds, Dictionary = WatermarkTokens },
        new("imaging-color-matrix", ColorMatrixTarget) { Seeds = GetColorMatrixSeeds, Dictionary = Tokens },
        new("imaging-resize-float", ResizeFloatTarget) { Seeds = GetResizeFloatSeeds, Dictionary = Tokens },
        new("imaging-tiff-planes", TiffPlanesTarget) { Seeds = GetTiffPlanesSeeds, Dictionary = Tokens },
        new("imaging-jpeg2000-encoder", Jpeg2000EncoderTarget) { Seeds = GetJpeg2000EncoderSeeds, Dictionary = Tokens },
        new("imaging-webp-encoder", WebPEncoderTarget) { Seeds = GetWebPEncoderSeeds, Dictionary = Tokens },
        new("imaging-jxl-layers", JpegXlLayersTarget) { Seeds = GetJpegXlLayersSeeds, Dictionary = JpegXlLayerTokens },
        new("imaging-jxl-layers-load", JpegXlLayersLoadTarget) { Seeds = GetJpegXlLayersLoadSeeds, Dictionary = JpegXlLayerTokens },
        new("imaging-jxl-optimized-animation", JpegXlOptimizedAnimationTarget) { Seeds = GetJpegXlOptimizedAnimationSeeds, Dictionary = Tokens },
    ];

    /// <summary>
    /// Reads an image from the input: a byte of format, a byte of width and one of height (1 to <paramref name="maxSide"/>),
    /// a byte of frame count (1 to 3, and a poster frame when its high bit is set), then the bytes of the pixels, repeated
    /// when the input is shorter.
    /// </summary>
    private static Image CreateImage(ref ReadOnlySpan<byte> data, int maxSide, ReadOnlySpan<byte> pixels = default)
    {
        var format = Formats[FuzzInput.ReadByte(ref data) % Formats.Length];
        var width = 1 + (FuzzInput.ReadByte(ref data) % maxSide);
        var height = 1 + (FuzzInput.ReadByte(ref data) % maxSide);
        var frameByte = FuzzInput.ReadByte(ref data);
        var frames = 1 + (frameByte & 0x7F) % 3;
        var source = pixels.IsEmpty ? data : pixels;
        Image image = format switch
        {
            PixelFormat.Rgba32 => Create<Rgba32>(source, width, height),
            PixelFormat.Rgb24 => Create<Rgb24>(source, width, height),
            PixelFormat.Gray8 => Create<Gray8>(source, width, height),
            PixelFormat.Gray16 => Create<Gray16>(source, width, height),
            PixelFormat.Rgba64 => Create<Rgba64>(source, width, height),
            PixelFormat.La16 => Create<La16>(source, width, height),
            PixelFormat.RgbaFloat => Create<RgbaFloat>(source, width, height),
            PixelFormat.RgbaHalf => Create<RgbaHalf>(source, width, height),
            PixelFormat.Bgr565 => Create<Bgr565>(source, width, height),
            PixelFormat.A8 => Create<A8>(source, width, height),
            PixelFormat.Bgra32 => Create<Bgra32>(source, width, height),
            PixelFormat.Rgb48 => Create<Rgb48>(source, width, height),
            _ => throw new InvalidOperationException("The format is not listed."),
        };

        try
        {
            for (var i = 1; i < frames; i++)
            {
                // The next frames are the first one with its rows rotated, so that they differ
                var frame = image.InsertFrame(i, image.Frames[0]);
                RotateRows(frame, i);
            }

            if ((frameByte & 0x80) != 0)
            {
                RotateRows(image.SetPosterFrame(image.Frames[0]), frames);
            }
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    private static Image<TPixel> Create<TPixel>(ReadOnlySpan<byte> source, int width, int height)
        where TPixel : unmanaged
    {
        var length = width * height * Marshal.SizeOf<TPixel>();
        var bytes = new byte[length];
        for (var i = 0; i < length && !source.IsEmpty; i++)
        {
            bytes[i] = source[i % source.Length];
        }

        return Image.ImportPixelBytes<TPixel>(bytes, width, height);
    }

    private static void RotateRows(ImageFrame frame, int shift)
    {
        var rowLength = frame.Width * PixelFormats.GetBytesPerPixel(frame.PixelFormat);
        var bytes = new byte[rowLength * frame.Height];
        frame.CopyPixelBytesTo(bytes);
        var rotated = new byte[bytes.Length];
        for (var y = 0; y < frame.Height; y++)
        {
            bytes.AsSpan(((y + shift) % frame.Height) * rowLength, rowLength).CopyTo(rotated.AsSpan(y * rowLength));
        }

        frame.ProcessPixelBytes(rotated, static (pixels, rotated) =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                rotated.AsSpan(y * pixels.RowLength, pixels.RowLength).CopyTo(pixels.GetRowSpan(y));
            }
        });
    }

    /// <summary>Gets the size, the frame count and the bytes of every frame and of the poster: what a failed transactional operation must leave unchanged.</summary>
    private static string Snapshot(Image image)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(image.Width).Append('x').Append(image.Height).Append(' ').Append(image.Frames.Count).Append(image.PosterFrame is null ? "" : "+poster");
        var frames = image.PosterFrame is null ? [.. image.Frames] : (ImageFrame[])[.. image.Frames, image.PosterFrame];
        foreach (var frame in frames)
        {
            var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
            frame.CopyPixelBytesTo(bytes);
            builder.Append(' ').Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
            builder.Append(' ').Append(frame.Metadata.Hotspot?.ToString() ?? "-");
        }

        return builder.ToString();
    }
}
