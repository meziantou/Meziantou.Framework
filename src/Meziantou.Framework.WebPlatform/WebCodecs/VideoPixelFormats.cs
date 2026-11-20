using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>A plane of a pixel format (https://w3c.github.io/webcodecs/#videopixelformat): its sub-sampling factors and the size of a sample.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct VideoPlaneInfo(int SampleWidth, int SampleHeight, int SampleBytes)
{
    /// <summary>The number of bytes of a row of the plane of a frame of a width.</summary>
    public long GetRowBytes(long width) => DivideRoundUp(width, SampleWidth) * SampleBytes;

    /// <summary>The number of rows of the plane of a frame of a height.</summary>
    public long GetRows(long height) => DivideRoundUp(height, SampleHeight);

    public static long DivideRoundUp(long value, int divisor) => (value + divisor - 1) / divisor;
}

/// <summary>The planes, the bit depths and the conversions of the pixel formats of <see cref="VideoFrame"/>.</summary>
internal static class VideoPixelFormats
{
    /// <summary>The largest coded width or height of a frame, as in the browsers.</summary>
    public const int MaxDimension = 16384;

    private static readonly VideoPlaneInfo Full8 = new(1, 1, 1);
    private static readonly VideoPlaneInfo Full16 = new(1, 1, 2);
    private static readonly VideoPlaneInfo Half8 = new(2, 2, 1);
    private static readonly VideoPlaneInfo Half16 = new(2, 2, 2);
    private static readonly VideoPlaneInfo Horizontal8 = new(2, 1, 1);
    private static readonly VideoPlaneInfo Horizontal16 = new(2, 1, 2);
    private static readonly VideoPlaneInfo Rgb = new(1, 1, 4);
    private static readonly VideoPlaneInfo InterleavedUv = new(2, 2, 2);

    private static readonly VideoPlaneInfo[] I420Planes = [Full8, Half8, Half8];
    private static readonly VideoPlaneInfo[] I420HighPlanes = [Full16, Half16, Half16];
    private static readonly VideoPlaneInfo[] I420APlanes = [Full8, Half8, Half8, Full8];
    private static readonly VideoPlaneInfo[] I420AHighPlanes = [Full16, Half16, Half16, Full16];
    private static readonly VideoPlaneInfo[] I422Planes = [Full8, Horizontal8, Horizontal8];
    private static readonly VideoPlaneInfo[] I422HighPlanes = [Full16, Horizontal16, Horizontal16];
    private static readonly VideoPlaneInfo[] I422APlanes = [Full8, Horizontal8, Horizontal8, Full8];
    private static readonly VideoPlaneInfo[] I422AHighPlanes = [Full16, Horizontal16, Horizontal16, Full16];
    private static readonly VideoPlaneInfo[] I444Planes = [Full8, Full8, Full8];
    private static readonly VideoPlaneInfo[] I444HighPlanes = [Full16, Full16, Full16];
    private static readonly VideoPlaneInfo[] I444APlanes = [Full8, Full8, Full8, Full8];
    private static readonly VideoPlaneInfo[] I444AHighPlanes = [Full16, Full16, Full16, Full16];
    private static readonly VideoPlaneInfo[] Nv12Planes = [Full8, InterleavedUv];
    private static readonly VideoPlaneInfo[] RgbPlanes = [Rgb];

    /// <summary>Returns the planes of a format.</summary>
    public static VideoPlaneInfo[] GetPlanes(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.I420 => I420Planes,
        VideoPixelFormat.I420P10 or VideoPixelFormat.I420P12 => I420HighPlanes,
        VideoPixelFormat.I420A => I420APlanes,
        VideoPixelFormat.I420AP10 or VideoPixelFormat.I420AP12 => I420AHighPlanes,
        VideoPixelFormat.I422 => I422Planes,
        VideoPixelFormat.I422P10 or VideoPixelFormat.I422P12 => I422HighPlanes,
        VideoPixelFormat.I422A => I422APlanes,
        VideoPixelFormat.I422AP10 or VideoPixelFormat.I422AP12 => I422AHighPlanes,
        VideoPixelFormat.I444 => I444Planes,
        VideoPixelFormat.I444P10 or VideoPixelFormat.I444P12 => I444HighPlanes,
        VideoPixelFormat.I444A => I444APlanes,
        VideoPixelFormat.I444AP10 or VideoPixelFormat.I444AP12 => I444AHighPlanes,
        VideoPixelFormat.NV12 => Nv12Planes,
        _ => RgbPlanes,
    };

    /// <summary>Whether the format is one of the RGB formats (one plane of 4 bytes per pixel).</summary>
    public static bool IsRgb(VideoPixelFormat format) => format is VideoPixelFormat.RGBA or VideoPixelFormat.RGBX or VideoPixelFormat.BGRA or VideoPixelFormat.BGRX;

    /// <summary>Whether the samples of an RGB format are in the blue, green, red order.</summary>
    public static bool IsBgr(VideoPixelFormat format) => format is VideoPixelFormat.BGRA or VideoPixelFormat.BGRX;

    /// <summary>Whether the format has an alpha channel.</summary>
    public static bool HasAlpha(VideoPixelFormat format) => format is VideoPixelFormat.RGBA or VideoPixelFormat.BGRA
        or VideoPixelFormat.I420A or VideoPixelFormat.I420AP10 or VideoPixelFormat.I420AP12
        or VideoPixelFormat.I422A or VideoPixelFormat.I422AP10 or VideoPixelFormat.I422AP12
        or VideoPixelFormat.I444A or VideoPixelFormat.I444AP10 or VideoPixelFormat.I444AP12;

    /// <summary>The number of bits of a sample of the format.</summary>
    public static int GetBitDepth(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.I420P10 or VideoPixelFormat.I420AP10 or VideoPixelFormat.I422P10 or VideoPixelFormat.I422AP10 or VideoPixelFormat.I444P10 or VideoPixelFormat.I444AP10 => 10,
        VideoPixelFormat.I420P12 or VideoPixelFormat.I420AP12 or VideoPixelFormat.I422P12 or VideoPixelFormat.I422AP12 or VideoPixelFormat.I444P12 or VideoPixelFormat.I444AP12 => 12,
        _ => 8,
    };

    /// <summary>The equivalent opaque format of a format (https://w3c.github.io/webcodecs/#equivalent-opaque-format).</summary>
    public static VideoPixelFormat GetOpaqueFormat(VideoPixelFormat format) => format switch
    {
        VideoPixelFormat.I420A => VideoPixelFormat.I420,
        VideoPixelFormat.I420AP10 => VideoPixelFormat.I420P10,
        VideoPixelFormat.I420AP12 => VideoPixelFormat.I420P12,
        VideoPixelFormat.I422A => VideoPixelFormat.I422,
        VideoPixelFormat.I422AP10 => VideoPixelFormat.I422P10,
        VideoPixelFormat.I422AP12 => VideoPixelFormat.I422P12,
        VideoPixelFormat.I444A => VideoPixelFormat.I444,
        VideoPixelFormat.I444AP10 => VideoPixelFormat.I444P10,
        VideoPixelFormat.I444AP12 => VideoPixelFormat.I444P12,
        VideoPixelFormat.RGBA => VideoPixelFormat.RGBX,
        VideoPixelFormat.BGRA => VideoPixelFormat.BGRX,
        _ => format,
    };

    /// <summary>
    /// Returns whether the offset of a rectangle is a multiple of the sub-sampling factors of the planes of a format
    /// (https://w3c.github.io/webcodecs/#videoframe-verify-rect-offset-alignment).
    /// </summary>
    public static bool IsRectOffsetAligned(VideoPixelFormat? format, long x, long y)
    {
        if (format is not { } value)
            return true;

        foreach (var plane in GetPlanes(value))
        {
            if (x % plane.SampleWidth != 0 || y % plane.SampleHeight != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Converts the pixels of a frame to RGBA (8 bits per sample, not premultiplied) in the RGB color space of the frame:
    /// the YUV formats are converted with the matrix and the range of the color space
    /// (https://w3c.github.io/webcodecs/#convert-to-rgb-frame).
    /// </summary>
    /// <param name="format">The format of the frame.</param>
    /// <param name="planes">The planes of the frame (tightly packed).</param>
    /// <param name="width">The coded width.</param>
    /// <param name="height">The coded height.</param>
    /// <param name="colorSpace">The color space of the frame.</param>
    public static byte[] ConvertToRgba(VideoPixelFormat format, byte[][] planes, int width, int height, VideoColorSpace colorSpace)
    {
        var output = new byte[(long)width * height * 4];
        var planeInfos = GetPlanes(format);
        if (IsRgb(format))
        {
            var source = planes[0];
            for (var i = 0; i < output.Length; i += 4)
            {
                var (r, g, b) = IsBgr(format) ? (source[i + 2], source[i + 1], source[i]) : (source[i], source[i + 1], source[i + 2]);
                output[i] = r;
                output[i + 1] = g;
                output[i + 2] = b;
                output[i + 3] = HasAlpha(format) ? source[i + 3] : (byte)255;
            }

            return output;
        }

        var bitDepth = GetBitDepth(format);
        var scale = 1 << (bitDepth - 8);
        var maxValue = (1 << bitDepth) - 1;
        var fullRange = colorSpace.FullRange == true;
        var (kr, kb) = colorSpace.Matrix switch
        {
            VideoMatrixCoefficients.Bt470bg or VideoMatrixCoefficients.Smpte170m => (0.299, 0.114),
            VideoMatrixCoefficients.Bt2020Ncl => (0.2627, 0.0593),
            _ => (0.2126, 0.0722),
        };
        var kg = 1 - kr - kb;
        var isRgbMatrix = colorSpace.Matrix == VideoMatrixCoefficients.Rgb;
        var lumaRowBytes = planeInfos[0].GetRowBytes(width);
        var isNv12 = format == VideoPixelFormat.NV12;
        var chroma = planeInfos[1];
        var chromaRowBytes = chroma.GetRowBytes(width);
        var hasAlpha = planeInfos.Length == 4 && HasAlpha(format);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double luma = ReadSample(planes[0], (y * lumaRowBytes) + (x * planeInfos[0].SampleBytes), planeInfos[0].SampleBytes);
                double cb;
                double cr;
                var chromaOffset = ((y / chroma.SampleHeight) * chromaRowBytes) + ((x / chroma.SampleWidth) * chroma.SampleBytes);
                if (isNv12)
                {
                    cb = planes[1][chromaOffset];
                    cr = planes[1][chromaOffset + 1];
                }
                else
                {
                    cb = ReadSample(planes[1], chromaOffset, chroma.SampleBytes);
                    cr = ReadSample(planes[2], chromaOffset, chroma.SampleBytes);
                }

                double r;
                double g;
                double b;
                if (isRgbMatrix)
                {
                    // The planes are G, B and R
                    (g, b, r) = (luma / maxValue, cb / maxValue, cr / maxValue);
                }
                else
                {
                    double yNorm;
                    double cbNorm;
                    double crNorm;
                    if (fullRange)
                    {
                        yNorm = luma / maxValue;
                        cbNorm = (cb - (128 * scale)) / maxValue;
                        crNorm = (cr - (128 * scale)) / maxValue;
                    }
                    else
                    {
                        yNorm = (luma - (16 * scale)) / (219d * scale);
                        cbNorm = (cb - (128 * scale)) / (224d * scale);
                        crNorm = (cr - (128 * scale)) / (224d * scale);
                    }

                    r = yNorm + (2 * (1 - kr) * crNorm);
                    b = yNorm + (2 * (1 - kb) * cbNorm);
                    g = (yNorm - (kr * r) - (kb * b)) / kg;
                }

                var index = ((long)y * width + x) * 4;
                output[index] = ToByte(r);
                output[index + 1] = ToByte(g);
                output[index + 2] = ToByte(b);
                output[index + 3] = hasAlpha ? ToByte(ReadSample(planes[3], (y * lumaRowBytes) + (x * planeInfos[3].SampleBytes), planeInfos[3].SampleBytes) / (double)maxValue) : (byte)255;
            }
        }

        return output;

        static int ReadSample(byte[] plane, long offset, int sampleBytes) => sampleBytes == 2 ? plane[offset] | (plane[offset + 1] << 8) : plane[offset];

        static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);
    }

    /// <summary>The RGB color space of the pixels of a frame converted to RGB: Display P3 for the SMPTE 432 primaries, sRGB otherwise.</summary>
    public static PredefinedColorSpace GetRgbColorSpace(VideoColorSpace colorSpace) => colorSpace.Primaries == VideoColorPrimaries.Smpte432 ? PredefinedColorSpace.DisplayP3 : PredefinedColorSpace.Srgb;

    public static RenderingColorSpace ToRenderingColorSpace(PredefinedColorSpace colorSpace) => colorSpace == PredefinedColorSpace.DisplayP3 ? RenderingColorSpace.DisplayP3 : RenderingColorSpace.Srgb;
}
