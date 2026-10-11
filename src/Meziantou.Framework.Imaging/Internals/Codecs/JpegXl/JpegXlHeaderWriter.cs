using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the headers of a codestream produced by the lossless encoder (ISO/IEC 18181-1, annexes A and C): the image
/// header of an image that is not stored in XYB, and the header and the table of contents of a Modular frame that replaces
/// the whole image.
/// </summary>
internal static class JpegXlHeaderWriter
{
    /// <summary>The number of ticks per second of the animations the encoder writes: durations are milliseconds.</summary>
    public const int TicksPerSecond = 1000;

    /// <summary>The group size shift of the frames the encoder writes: groups of 256 samples.</summary>
    public const int GroupSizeShift = 1;

    /// <summary>Writes the signature, the size, the image metadata and the default transform data.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="size">The size of the image.</param>
    /// <param name="isGray">Whether the image has a single color channel.</param>
    /// <param name="bitsPerSample">The bit depth of the color channels (8 or 16).</param>
    /// <param name="hasAlpha">Whether the image has an alpha channel, of the same depth.</param>
    /// <param name="orientation">The orientation.</param>
    /// <param name="animation">The animation parameters, or <see langword="null"/> for a still image.</param>
    /// <param name="color">How the colors are described.</param>
    public static void WriteImageHeader(LsbBitWriter writer, Size size, bool isGray, int bitsPerSample, bool hasAlpha, ExifOrientation orientation, AnimationMetadata? animation, JpegXlEncoderColor color)
    {
        writer.WriteBits(0x0AFF, 16);
        WriteSize(writer, size);

        // ImageMetadata
        writer.WriteBool(false);
        var extraFields = orientation != ExifOrientation.TopLeft || animation is not null;
        writer.WriteBool(extraFields);
        if (extraFields)
        {
            writer.WriteBits((uint)orientation - 1, 3);
            writer.WriteBool(false);
            writer.WriteBool(false);
            writer.WriteBool(animation is not null);
            if (animation is not null)
            {
                // 1000 ticks per second; the number of plays, 0 meaning forever; no time code
                writer.WriteU32(TicksPerSecond, 100, 0, 1000, 0, 1, 10, 1, 30);
                writer.WriteU32(1, 1, 0, 1001, 0, 1, 8, 1, 10);
                writer.WriteU32((uint)(animation.TotalPlays ?? 0), 0, 0, 0, 3, 0, 16, 0, 32);
                writer.WriteBool(false);
            }
        }

        WriteBitDepth(writer, bitsPerSample);

        // Samples of at most 8 bits and their reversible color transform fit 16-bit buffers
        writer.WriteBool(bitsPerSample <= 8);
        writer.WriteU32(hasAlpha ? 1u : 0u, 0, 0, 1, 0, 2, 4, 1, 12);
        if (hasAlpha)
        {
            if (bitsPerSample == 8)
            {
                // The default extra channel: 8-bit alpha, not premultiplied
                writer.WriteBool(true);
            }
            else
            {
                writer.WriteBool(false);
                writer.WriteEnum(JpegXlExtraChannelInfo.TypeAlpha);
                WriteBitDepth(writer, bitsPerSample);
                writer.WriteU32(0, 0, 0, 3, 0, 4, 0, 1, 3);
                writer.WriteU32(0, 0, 0, 0, 4, 16, 5, 48, 10);
                writer.WriteBool(false);
            }
        }

        // The samples are not stored in XYB
        writer.WriteBool(false);
        WriteColorEncoding(writer, isGray, color);
        if (extraFields)
        {
            // Default tone mapping
            writer.WriteBool(true);
        }

        // No extension, default transform data
        writer.WriteU64(0);
        writer.WriteBool(true);
    }

    /// <summary>Writes the header of a regular Modular frame that covers the image and replaces it, without restoration filter.</summary>
    /// <param name="writer">The writer, at a byte boundary.</param>
    /// <param name="hasAlpha">Whether the image has an alpha channel.</param>
    /// <param name="duration">The duration in ticks, for an animation; otherwise <see langword="null"/>.</param>
    /// <param name="isLast">Whether the frame is the last one of the codestream.</param>
    public static void WriteFrameHeader(LsbBitWriter writer, bool hasAlpha, uint? duration, bool isLast)
    {
        writer.WriteBool(false);
        writer.WriteBits(JpegXlFrameHeader.TypeRegular, 2);
        writer.WriteBool(true);
        writer.WriteU64(0);

        // Not YCbCr; no upsampling of the color channels nor of the alpha channel
        writer.WriteBool(false);
        writer.WriteBits(0, 2);
        if (hasAlpha)
        {
            writer.WriteBits(0, 2);
        }

        writer.WriteBits(GroupSizeShift, 2);

        // One pass, no custom size or origin, every channel replaces the image
        writer.WriteU32(1, 1, 0, 2, 0, 3, 0, 4, 3);
        writer.WriteBool(false);
        writer.WriteU32(JpegXlBlendingInfo.ModeReplace, 0, 0, 1, 0, 2, 0, 3, 2);
        if (hasAlpha)
        {
            writer.WriteU32(JpegXlBlendingInfo.ModeReplace, 0, 0, 1, 0, 2, 0, 3, 2);
        }

        if (duration is { } ticks)
        {
            writer.WriteU32(ticks, 0, 0, 1, 0, 0, 8, 0, 32);
        }

        writer.WriteBool(isLast);
        if (!isLast)
        {
            // Not saved as a reference frame; a frame without duration that is not the last one could be referenced
            writer.WriteBits(0, 2);
            if ((duration ?? 0) == 0)
            {
                writer.WriteBool(false);
            }
        }

        // No name; no Gabor-like filter, no edge-preserving filter, no extension
        writer.WriteU32(0, 0, 0, 0, 4, 16, 5, 48, 10);
        writer.WriteBool(false);
        writer.WriteBool(false);
        writer.WriteBits(0, 2);
        writer.WriteU64(0);
        writer.WriteU64(0);
    }

    /// <summary>Writes a table of contents that is not permuted.</summary>
    /// <param name="writer">The writer, after the frame header.</param>
    /// <param name="sizes">The size in bytes of each section.</param>
    public static void WriteToc(LsbBitWriter writer, ReadOnlySpan<int> sizes)
    {
        writer.WriteBool(false);
        writer.ZeroPadToByte();
        foreach (var size in sizes)
        {
            writer.WriteU32((uint)size, 0, 10, 1024, 14, 17408, 22, 4211712, 30);
        }

        writer.ZeroPadToByte();
    }

    private static void WriteSize(LsbBitWriter writer, Size size)
    {
        var width = (uint)size.Width;
        var height = (uint)size.Height;
        var small = width % 8 == 0 && height % 8 == 0 && width <= 256 && height <= 256;
        writer.WriteBool(small);
        WriteSide(writer, height, small);

        // No aspect ratio: the width is written like the height
        writer.WriteBits(0, 3);
        WriteSide(writer, width, small);

        static void WriteSide(LsbBitWriter writer, uint value, bool small)
        {
            if (small)
            {
                writer.WriteBits((value / 8) - 1, 5);
            }
            else
            {
                writer.WriteU32(value, 1, 9, 1, 13, 1, 18, 1, 30);
            }
        }
    }

    private static void WriteBitDepth(LsbBitWriter writer, int bitsPerSample)
    {
        // Integer samples
        writer.WriteBool(false);
        writer.WriteU32((uint)bitsPerSample, 8, 0, 10, 0, 12, 0, 1, 6);
    }

    private static void WriteColorEncoding(LsbBitWriter writer, bool isGray, JpegXlEncoderColor color)
    {
        if (!isGray && color.IsSrgb)
        {
            // The default encoding: sRGB
            writer.WriteBool(true);
            return;
        }

        writer.WriteBool(false);
        writer.WriteBool(color.HasIccProfile);
        writer.WriteEnum(isGray ? JpegXlColorEncoding.ColorSpaceGray : JpegXlColorEncoding.ColorSpaceRgb);
        if (color.HasIccProfile)
            return;

        writer.WriteEnum(color.WhitePoint);
        if (!isGray)
        {
            writer.WriteEnum(color.Primaries);
        }

        // An enumerated transfer function, not a gamma; relative rendering intent
        writer.WriteBool(false);
        writer.WriteEnum(color.TransferFunction);
        writer.WriteEnum(1);
    }
}
