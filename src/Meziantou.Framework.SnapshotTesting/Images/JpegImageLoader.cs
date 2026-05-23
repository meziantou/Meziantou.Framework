using System.Buffers.Binary;
using JpegLibrary;

namespace Meziantou.Framework.SnapshotTesting;

internal static class JpegImageLoader
{
    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfImageMarker = 0xD8;
    private const byte EndOfImageMarker = 0xD9;
    private const byte StartOfScanMarker = 0xDA;
    private const byte App1Marker = 0xE1;

    internal static bool IsJpeg(ReadOnlySpan<byte> data)
    {
        return data.Length >= 2 && data[0] == MarkerPrefix && data[1] == StartOfImageMarker;
    }

    internal static Image Load(ReadOnlySpan<byte> data)
    {
        if (!IsJpeg(data))
            throw new InvalidDataException("The JPEG signature is invalid.");

        var orientation = ReadExifOrientation(data);
        var image = DecodeImage(data);
        return orientation == 1 ? image : ApplyOrientation(image, orientation);
    }

    private static Image DecodeImage(ReadOnlySpan<byte> data)
    {
        var decoder = new JpegDecoder();
        decoder.SetInput(data.ToArray());
        decoder.Identify();

        if (decoder.NumberOfComponents is not 1 and not 3)
            throw new NotSupportedException("Only grayscale and YCbCr JPEG images are supported.");

        if (decoder.Precision != 8)
            throw new NotSupportedException("Only 8-bit JPEG precision is supported.");

        var width = decoder.Width;
        var height = decoder.Height;
        if (width <= 0 || height <= 0)
            throw new NotSupportedException("Unsupported JPEG dimensions.");

        var ycbcr = new byte[checked(width * height * 3)];
        decoder.SetOutputWriter(new JpegBufferOutputWriter8Bit(width, height, 3, ycbcr));
        decoder.Decode();

        if (decoder.NumberOfComponents == 1)
        {
            for (var i = 0; i < ycbcr.Length; i += 3)
            {
                ycbcr[i + 1] = 128;
                ycbcr[i + 2] = 128;
            }
        }

        var pixels = new Argb[checked(width * height)];
        for (var pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
        {
            var sourceOffset = pixelIndex * 3;
            var y = ycbcr[sourceOffset];
            var cb = ycbcr[sourceOffset + 1];
            var cr = ycbcr[sourceOffset + 2];
            pixels[pixelIndex] = ConvertYcbcrToArgb(y, cb, cr);
        }

        return Image.Create(width, height, pixels);
    }

    private static ushort ReadExifOrientation(ReadOnlySpan<byte> data)
    {
        var position = 2;
        while (position + 1 < data.Length)
        {
            if (data[position] != MarkerPrefix)
            {
                position++;
                continue;
            }

            position++;
            while (position < data.Length && data[position] == MarkerPrefix)
            {
                position++;
            }

            if (position >= data.Length)
                break;

            var marker = data[position++];
            if (marker == StartOfScanMarker || marker == EndOfImageMarker)
                break;

            if (marker is >= 0xD0 and <= 0xD7 || marker == 0x01 || marker == StartOfImageMarker)
                continue;

            if (position + 2 > data.Length)
                break;

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data[position..]);
            position += 2;
            if (segmentLength < 2)
                break;

            var payloadLength = segmentLength - 2;
            if (position + payloadLength > data.Length)
                break;

            if (marker == App1Marker)
            {
                var segment = data.Slice(position, payloadLength);
                if (TryGetExifOrientation(segment, out var orientation))
                    return orientation;
            }

            position += payloadLength;
        }

        return 1;
    }

    private static bool TryGetExifOrientation(ReadOnlySpan<byte> segment, out ushort orientation)
    {
        orientation = 1;
        if (segment.Length < 6 ||
            segment[0] != (byte)'E' ||
            segment[1] != (byte)'x' ||
            segment[2] != (byte)'i' ||
            segment[3] != (byte)'f' ||
            segment[4] != 0 ||
            segment[5] != 0)
        {
            return false;
        }

        return TryReadExifOrientation(segment[6..], out orientation);
    }

    private static bool TryReadExifOrientation(ReadOnlySpan<byte> exifData, out ushort orientation)
    {
        orientation = 1;
        if (exifData.Length < 8)
            return false;

        var littleEndian = exifData[0] == (byte)'I' && exifData[1] == (byte)'I';
        var bigEndian = exifData[0] == (byte)'M' && exifData[1] == (byte)'M';
        if (!littleEndian && !bigEndian)
            return false;

        var magic = ReadUInt16(exifData, 2, littleEndian);
        if (magic != 42)
            return false;

        var ifdOffset = ReadUInt32(exifData, 4, littleEndian);
        if (ifdOffset > int.MaxValue)
            return false;

        var ifdPosition = checked((int)ifdOffset);
        if (ifdPosition + 2 > exifData.Length)
            return false;

        var entryCount = ReadUInt16(exifData, ifdPosition, littleEndian);
        ifdPosition += 2;

        for (var i = 0; i < entryCount; i++)
        {
            var entryOffset = ifdPosition + i * 12;
            if (entryOffset + 12 > exifData.Length)
                return false;

            var tag = ReadUInt16(exifData, entryOffset, littleEndian);
            if (tag != 0x0112)
                continue;

            var type = ReadUInt16(exifData, entryOffset + 2, littleEndian);
            var count = ReadUInt32(exifData, entryOffset + 4, littleEndian);
            if (type != 3 || count != 1)
                return false;

            orientation = ReadUInt16(exifData, entryOffset + 8, littleEndian);
            return orientation is >= 1 and <= 8;
        }

        return false;
    }

    private static Image ApplyOrientation(Image image, ushort orientation)
    {
        if (orientation is < 2 or > 8)
            return image;

        var width = image.Width;
        var height = image.Height;
        var sourcePixels = image.Pixels.ToArray();

        var targetWidth = orientation is 5 or 6 or 7 or 8 ? height : width;
        var targetHeight = orientation is 5 or 6 or 7 or 8 ? width : height;
        var targetPixels = new Argb[checked(targetWidth * targetHeight)];

        for (var y = 0; y < targetHeight; y++)
        {
            for (var x = 0; x < targetWidth; x++)
            {
                var sourceIndex = orientation switch
                {
                    2 => y * width + (width - 1 - x),
                    3 => (height - 1 - y) * width + (width - 1 - x),
                    4 => (height - 1 - y) * width + x,
                    5 => x * width + y,
                    6 => (height - 1 - x) * width + y,
                    7 => (height - 1 - x) * width + (width - 1 - y),
                    8 => x * width + (width - 1 - y),
                    _ => y * width + x,
                };

                targetPixels[y * targetWidth + x] = sourcePixels[sourceIndex];
            }
        }

        return Image.Create(targetWidth, targetHeight, targetPixels);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian)
    {
        if (offset + 2 > data.Length)
            throw new InvalidDataException("The EXIF metadata is truncated.");

        var value = data[offset..(offset + 2)];
        return littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(value) : BinaryPrimitives.ReadUInt16BigEndian(value);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian)
    {
        if (offset + 4 > data.Length)
            throw new InvalidDataException("The EXIF metadata is truncated.");

        var value = data[offset..(offset + 4)];
        return littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(value) : BinaryPrimitives.ReadUInt32BigEndian(value);
    }

    private static Argb ConvertYcbcrToArgb(byte y, byte cb, byte cr)
    {
        var cbOffset = cb - 128.0;
        var crOffset = cr - 128.0;
        var red = ClampToByte((int)Math.Round(y + 1.40200 * crOffset, MidpointRounding.AwayFromZero));
        var green = ClampToByte((int)Math.Round(y - 0.344136 * cbOffset - 0.714136 * crOffset, MidpointRounding.AwayFromZero));
        var blue = ClampToByte((int)Math.Round(y + 1.77200 * cbOffset, MidpointRounding.AwayFromZero));
        return new Argb((uint)(0xFF000000u | (uint)(red << 16) | (uint)(green << 8) | (uint)blue));
    }

    private static int ClampToByte(int value)
    {
        return Math.Clamp(value, 0, 255);
    }
}
