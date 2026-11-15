using System.Buffers.Binary;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Reads the size in pixels of a logo from the header of its PNG, GIF, WebP or JPEG file, without decoding it. The renderers
/// fit the pixels of the logo in its square, keeping their proportions, so this size gives the part of the square it covers.
/// </summary>
internal static class LogoImageSize
{
    /// <summary>
    /// Gets the size of the image, or <see langword="false"/> when the header cannot be read, or when the image has Exif
    /// metadata: its orientation can swap the sides in a viewer that applies it.
    /// </summary>
    public static bool TryGetSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        var found = data switch
        {
            [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => TryReadPng(data, out width, out height),
            [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7' or (byte)'9', (byte)'a', ..] => TryReadGif(data, out width, out height),
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => TryReadWebP(data, out width, out height),
            [0xFF, 0xD8, ..] => TryReadJpeg(data, out width, out height),
            _ => false,
        };

        return found && width > 0 && height > 0;
    }

    // ISO/IEC 15948: IHDR is the first chunk; eXIf comes before the image data
    private static bool TryReadPng(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 24 || !data.Slice(12, 4).SequenceEqual("IHDR"u8))
            return false;

        var w = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var h = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);
        if (w > int.MaxValue || h > int.MaxValue)
            return false;

        var offset = 8L;
        while (offset + 8 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[(int)offset..]);
            var type = data.Slice((int)offset + 4, 4);
            if (type.SequenceEqual("eXIf"u8))
                return false;

            if (type.SequenceEqual("IDAT"u8))
                break;

            offset += 12L + length;
        }

        width = (int)w;
        height = (int)h;
        return true;
    }

    // The logical screen, which the decoders draw the frames on
    private static bool TryReadGif(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 10)
            return false;

        width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
        height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
        return true;
    }

    private static bool TryReadWebP(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 30)
            return false;

        var chunk = data.Slice(12, 4);
        var payload = data[20..];
        if (chunk.SequenceEqual("VP8X"u8))
        {
            // The Exif flag, then the size of the canvas minus one, on 24 bits
            if ((payload[0] & 0x08) != 0)
                return false;

            width = 1 + payload[4] + (payload[5] << 8) + (payload[6] << 16);
            height = 1 + payload[7] + (payload[8] << 8) + (payload[9] << 16);
            return true;
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // The signature, then the width and the height minus one, on 14 bits each
            if (payload[0] != 0x2F)
                return false;

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(payload[1..]);
            width = 1 + (int)(bits & 0x3FFF);
            height = 1 + (int)((bits >> 14) & 0x3FFF);
            return true;
        }

        if (chunk.SequenceEqual("VP8 "u8))
        {
            // The frame tag, the start code, then the width and the height on 14 bits, followed by their scaling
            if (payload[3] != 0x9D || payload[4] != 0x01 || payload[5] != 0x2A)
                return false;

            width = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..]) & 0x3FFF;
            height = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..]) & 0x3FFF;
            return true;
        }

        return false;
    }

    // ITU-T T.81: the segments before the first start of frame, which holds the size
    private static bool TryReadJpeg(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        var offset = 2;
        while (offset + 4 <= data.Length)
        {
            if (data[offset] != 0xFF)
                return false;

            var marker = data[offset + 1];
            if (marker is 0xFF)
            {
                // Fill byte
                offset++;
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                // Markers without a segment
                offset += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
                return false;

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 2)..]);
            if (length < 2 || offset + 2 + length > data.Length)
                return false;

            var segment = data.Slice(offset + 4, length - 2);
            if (marker is 0xE1 && segment.StartsWith("Exif\0"u8))
                return false;

            if (marker is (>= 0xC0 and <= 0xC3) or (>= 0xC5 and <= 0xC7) or (>= 0xC9 and <= 0xCB) or (>= 0xCD and <= 0xCF))
            {
                if (segment.Length < 5)
                    return false;

                height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                return true;
            }

            offset += 2 + length;
        }

        return false;
    }
}
