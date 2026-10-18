using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// The orientation of an image (the Orientation tag 0x0112 of its EXIF metadata, https://drafts.csswg.org/css-images-3/#image-orientation):
/// where the first row and the first column of the encoded pixels are displayed.
/// </summary>
internal enum ExifOrientation : byte
{
    /// <summary>1: the pixels are displayed as encoded.</summary>
    TopLeft = 1,

    /// <summary>2: mirrored horizontally.</summary>
    TopRight = 2,

    /// <summary>3: rotated by 180 degrees.</summary>
    BottomRight = 3,

    /// <summary>4: mirrored vertically.</summary>
    BottomLeft = 4,

    /// <summary>5: mirrored horizontally and rotated by 270 degrees clockwise (transposed).</summary>
    LeftTop = 5,

    /// <summary>6: rotated by 90 degrees clockwise.</summary>
    RightTop = 6,

    /// <summary>7: mirrored horizontally and rotated by 90 degrees clockwise (transversed).</summary>
    RightBottom = 7,

    /// <summary>8: rotated by 270 degrees clockwise.</summary>
    LeftBottom = 8,
}

/// <summary>
/// Reads the metadata of an encoded image that the user agent applies (https://drafts.csswg.org/css-images-3/#url-metadata):
/// its EXIF orientation and its EXIF resolution (the density-corrected dimensions). As in the browsers, only the metadata that precedes the image data is used: the EXIF segment of a
/// JPEG image before its first frame, the <c>eXIf</c> chunk (or the EXIF raw profile of a text chunk) of a PNG image before
/// its <c>IDAT</c> chunk. The metadata of the other formats is ignored.
/// </summary>
internal static class ImageMetadata
{
    /// <summary>Returns the EXIF orientation of an encoded image, <see cref="ExifOrientation.TopLeft"/> when it has none.</summary>
    public static ExifOrientation GetExifOrientation(ReadOnlySpan<byte> bytes)
    {
        var orientation = 0;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            orientation = GetTiffOrientation(GetJpegExif(bytes));
        }
        else if (bytes.StartsWith(PngSignature))
        {
            orientation = GetPngOrientation(bytes);
        }

        return orientation is >= 1 and <= 8 ? (ExifOrientation)orientation : ExifOrientation.TopLeft;
    }

    /// <summary>
    /// Returns the density-corrected dimensions of an encoded image (the "prepare an image for presentation" algorithm,
    /// https://html.spec.whatwg.org/multipage/images.html#prepare-an-image-for-presentation), or null: the
    /// <c>PixelXDimension</c> and <c>PixelYDimension</c> tags of its EXIF metadata, when its resolution (<c>XResolution</c>,
    /// <c>YResolution</c>, in pixels per inch) says that the image has this size at 72 pixels per inch. The dimensions are
    /// the ones of the encoded image, before its EXIF orientation is applied.
    /// </summary>
    /// <param name="bytes">The encoded image (a JPEG image, or a PNG image with an <c>eXIf</c> chunk).</param>
    /// <param name="physicalWidth">The width of the encoded image in pixels.</param>
    /// <param name="physicalHeight">The height of the encoded image in pixels.</param>
    public static (uint Width, uint Height)? GetDensityCorrectedSize(ReadOnlySpan<byte> bytes, uint physicalWidth, uint physicalHeight)
    {
        ReadOnlySpan<byte> tiff;
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
        {
            tiff = GetJpegExif(bytes);
        }
        else if (bytes.StartsWith(PngSignature))
        {
            tiff = GetPngExifChunk(bytes);
        }
        else
        {
            return null;
        }

        if (!TiffReader.TryCreate(tiff, out var reader) || !reader.TryGetDirectory(out var directory))
            return null;

        // The resolution of the first image file directory, and the dimensions of the Exif directory it points to
        if (reader.GetRational(directory, 0x011A) is not (> 0 and var resolutionX)
            || reader.GetRational(directory, 0x011B) is not (> 0 and var resolutionY)
            || reader.GetInteger(directory, 0x0128) != 2
            || reader.GetInteger(directory, 0x8769) is not (> 0 and var exifOffset)
            || !reader.TryGetDirectory((int)Math.Min(exifOffset, int.MaxValue), out var exifDirectory)
            || reader.GetInteger(exifDirectory, 0xA002) is not (> 0 and var dimensionX)
            || reader.GetInteger(exifDirectory, 0xA003) is not (> 0 and var dimensionY))
        {
            return null;
        }

        // The resolution of EXIF is in CSS points per inch: 72 is the base (as in the browsers, the sizes are compared rounded)
        if (Math.Round(physicalWidth * 72 / resolutionX, MidpointRounding.AwayFromZero) != dimensionX || Math.Round(physicalHeight * 72 / resolutionY, MidpointRounding.AwayFromZero) != dimensionY)
            return null;

        return (dimensionX, dimensionY);
    }

    /// <summary>
    /// Returns the number of repetitions of an animated GIF or WebP image after it is played once, as the browsers interpret it
    /// (-1 for an animation that loops forever), or null for another format: the loop count of the NETSCAPE2.0 application
    /// extension of a GIF image (0: forever, n: repeated n times; without extension, played once), the loop count of the ANIM
    /// chunk of a WebP image (0: forever, n: played n times).
    /// </summary>
    public static int? GetAnimationRepetitionCount(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8))
            return GetGifRepetitionCount(bytes);

        if (bytes.Length >= 16 && bytes.StartsWith("RIFF"u8) && bytes[8..].StartsWith("WEBP"u8))
        {
            var offset = 12;
            while (offset + 8 <= bytes.Length)
            {
                var type = bytes.Slice(offset, 4);
                var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
                if (type.SequenceEqual("ANIM"u8) && length >= 6 && offset + 14 <= bytes.Length)
                {
                    var loops = BinaryPrimitives.ReadUInt16LittleEndian(bytes[(offset + 12)..]);
                    return loops == 0 ? -1 : loops - 1;
                }

                if (length > int.MaxValue - 9)
                    break;

                offset += 8 + (int)length + (int)(length & 1);
            }
        }

        return null;
    }

    /// <summary>The repetition count of a GIF image (https://www.w3.org/Graphics/GIF/spec-gif89a.txt, the NETSCAPE2.0 looping extension).</summary>
    private static int GetGifRepetitionCount(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 13)
            return 0;

        // The header, the logical screen descriptor and the global color table
        var offset = 13;
        if ((bytes[10] & 0x80) != 0)
        {
            offset += 3 * (1 << ((bytes[10] & 0x07) + 1));
        }

        while (offset < bytes.Length)
        {
            switch (bytes[offset])
            {
                case 0x21 when offset + 2 < bytes.Length:
                {
                    // An extension: the application extension NETSCAPE2.0 (or ANIMEXTS1.0) with its loop count sub-block
                    var label = bytes[offset + 1];
                    var position = offset + 2;
                    if (label == 0xFF && position + 12 <= bytes.Length && bytes[position] == 11 && (bytes.Slice(position + 1, 11).SequenceEqual("NETSCAPE2.0"u8) || bytes.Slice(position + 1, 11).SequenceEqual("ANIMEXTS1.0"u8)))
                    {
                        var subBlock = position + 12;
                        if (subBlock + 4 <= bytes.Length && bytes[subBlock] >= 3 && bytes[subBlock + 1] == 1)
                        {
                            var loops = bytes[subBlock + 2] | (bytes[subBlock + 3] << 8);
                            return loops == 0 ? -1 : loops;
                        }
                    }

                    offset = SkipSubBlocks(bytes, position);
                    break;
                }

                case 0x2C when offset + 10 <= bytes.Length:
                {
                    // An image descriptor, its local color table and its data
                    var packed = bytes[offset + 9];
                    offset += 10;
                    if ((packed & 0x80) != 0)
                    {
                        offset += 3 * (1 << ((packed & 0x07) + 1));
                    }

                    offset = SkipSubBlocks(bytes, offset + 1);
                    break;
                }

                default:
                    // The trailer (0x3B), or corrupted data
                    return 0;
            }
        }

        return 0;

        static int SkipSubBlocks(ReadOnlySpan<byte> bytes, int offset)
        {
            while (offset < bytes.Length && bytes[offset] != 0)
            {
                offset += bytes[offset] + 1;
            }

            return offset + 1;
        }
    }

    /// <summary>Whether an orientation swaps the width and the height of the image (a rotation by 90 or 270 degrees).</summary>
    public static bool SwapsAxes(this ExifOrientation orientation) => orientation >= ExifOrientation.LeftTop;

    /// <summary>The signature of the PNG images.</summary>
    internal static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>The EXIF data (a TIFF structure) of the APP1 segment that precedes the first start of frame segment of a JPEG image, or empty.</summary>
    private static ReadOnlySpan<byte> GetJpegExif(ReadOnlySpan<byte> bytes)
    {
        var position = 2;
        while (position + 4 <= bytes.Length)
        {
            if (bytes[position] != 0xFF)
                return default;

            var marker = bytes[position + 1];
            if (marker is 0xD8 or 0x01 or 0xFF || (marker >= 0xD0 && marker <= 0xD7))
            {
                position += marker == 0xFF ? 1 : 2;
                continue;
            }

            // The start of frame markers (C0 to CF, except DHT C4, JPG C8 and DAC CC) and the start of scan: the metadata ends
            if ((marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC)) || marker == 0xDA)
                return default;

            var length = (bytes[position + 2] << 8) | bytes[position + 3];
            if (marker == 0xE1 && length >= 8 && position + 2 + length <= bytes.Length && bytes[(position + 4)..].StartsWith("Exif\0\0"u8))
                return bytes.Slice(position + 10, length - 8);

            position += 2 + length;
        }

        return default;
    }

    /// <summary>The EXIF data (a TIFF structure) of the <c>eXIf</c> chunk that precedes the image data of a PNG image (https://w3c.github.io/png/#eXIf), or empty.</summary>
    private static ReadOnlySpan<byte> GetPngExifChunk(ReadOnlySpan<byte> bytes)
    {
        var offset = PngSignature.Length;
        while (offset + 8 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            var type = bytes.Slice(offset + 4, 4);
            if (type.SequenceEqual("IDAT"u8) || length > int.MaxValue - 12 || offset + 8 + (long)length > bytes.Length)
                return default;

            if (type.SequenceEqual("eXIf"u8))
                return bytes.Slice(offset + 8, (int)length);

            offset += 12 + (int)length;
        }

        return default;
    }

    /// <summary>
    /// The orientation of a PNG image (https://w3c.github.io/png/#eXIf): its <c>eXIf</c> chunk, or the EXIF raw profile of a
    /// <c>tEXt</c>, <c>zTXt</c> or <c>iTXt</c> chunk (<c>Raw profile type exif</c>, written by ImageMagick), before the image
    /// data (a later chunk is ignored, as in the browsers), or 0.
    /// </summary>
    private static int GetPngOrientation(ReadOnlySpan<byte> bytes)
    {
        var offset = PngSignature.Length;
        while (offset + 8 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            var type = bytes.Slice(offset + 4, 4);
            if (type.SequenceEqual("IDAT"u8) || length > int.MaxValue - 12 || offset + 8 + (long)length > bytes.Length)
                return 0;

            var data = bytes.Slice(offset + 8, (int)length);
            if (type.SequenceEqual("eXIf"u8))
                return GetTiffOrientation(data);

            if ((type.SequenceEqual("tEXt"u8) || type.SequenceEqual("zTXt"u8) || type.SequenceEqual("iTXt"u8)) && GetRawProfileExif(type, data) is { } exif)
            {
                var orientation = GetTiffOrientation(exif.AsSpan().StartsWith("Exif\0\0"u8) ? exif.AsSpan(6) : exif);
                if (orientation != 0)
                    return orientation;
            }

            offset += 12 + (int)length;
        }

        return 0;
    }

    /// <summary>
    /// Returns the EXIF data of a text chunk whose keyword is <c>Raw profile type exif</c> or <c>Raw profile type APP1</c>: the
    /// text is a line feed, the profile name, its length, then the bytes in hexadecimal.
    /// </summary>
    private static byte[]? GetRawProfileExif(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var separator = data.IndexOf((byte)0);
        if (separator < 0)
            return null;

        var keyword = data[..separator];
        if (!keyword.SequenceEqual("Raw profile type exif"u8) && !keyword.SequenceEqual("Raw profile type APP1"u8))
            return null;

        byte[] text;
        try
        {
            if (type.SequenceEqual("tEXt"u8))
            {
                text = data[(separator + 1)..].ToArray();
            }
            else if (type.SequenceEqual("zTXt"u8))
            {
                // The compression method (0: zlib), then the compressed text
                if (separator + 2 > data.Length || data[separator + 1] != 0)
                    return null;

                text = Inflate(data[(separator + 2)..]);
            }
            else
            {
                // iTXt: the compression flag and method, the language tag and the translated keyword, then the text
                if (separator + 3 > data.Length)
                    return null;

                var compressed = data[separator + 1] != 0;
                var rest = data[(separator + 3)..];
                for (var i = 0; i < 2; i++)
                {
                    var end = rest.IndexOf((byte)0);
                    if (end < 0)
                        return null;

                    rest = rest[(end + 1)..];
                }

                text = compressed ? Inflate(rest) : rest.ToArray();
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }

        // The profile name and the length are on their own lines, then the hexadecimal digits (with line feeds)
        var position = 0;
        var lines = 0;
        while (position < text.Length && lines < 3)
        {
            if (text[position] == (byte)'\n')
            {
                lines++;
            }

            position++;
        }

        var result = new List<byte>(Math.Max(0, (text.Length - position) / 2));
        var high = -1;
        for (; position < text.Length; position++)
        {
            var digit = HexValue(text[position]);
            if (digit < 0)
                continue;

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                result.Add((byte)((high << 4) | digit));
                high = -1;
            }
        }

        return [.. result];

        static int HexValue(byte c) => c switch
        {
            >= (byte)'0' and <= (byte)'9' => c - '0',
            >= (byte)'a' and <= (byte)'f' => c - 'a' + 10,
            >= (byte)'A' and <= (byte)'F' => c - 'A' + 10,
            _ => -1,
        };
    }

    private static byte[] Inflate(ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Returns the orientation tag (0x0112) of the first image file directory of EXIF data (a TIFF structure), or 0.</summary>
    internal static int GetTiffOrientation(ReadOnlySpan<byte> tiff)
    {
        if (!TiffReader.TryCreate(tiff, out var reader) || !reader.TryGetDirectory(out var directory))
            return 0;

        return (int)Math.Min(reader.GetInteger(directory, 0x0112) ?? 0, int.MaxValue);
    }

    /// <summary>Reads the tags of the image file directories of EXIF data (a TIFF structure, https://www.cipa.jp/std/documents/e/DC-008-2012_E.pdf).</summary>
    private readonly ref struct TiffReader
    {
        private readonly ReadOnlySpan<byte> _tiff;
        private readonly bool _littleEndian;

        private TiffReader(ReadOnlySpan<byte> tiff, bool littleEndian)
        {
            _tiff = tiff;
            _littleEndian = littleEndian;
        }

        public static bool TryCreate(ReadOnlySpan<byte> tiff, out TiffReader reader)
        {
            reader = default;
            if (tiff.Length < 8)
                return false;

            var littleEndian = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
            if (!littleEndian && !(tiff[0] == (byte)'M' && tiff[1] == (byte)'M'))
                return false;

            reader = new TiffReader(tiff, littleEndian);
            return true;
        }

        private int ReadUInt16(int offset) => _littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(_tiff[offset..]) : BinaryPrimitives.ReadUInt16BigEndian(_tiff[offset..]);

        private uint ReadUInt32(int offset) => _littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(_tiff[offset..]) : BinaryPrimitives.ReadUInt32BigEndian(_tiff[offset..]);

        /// <summary>Returns the offset of the first image file directory.</summary>
        public bool TryGetDirectory(out int directory) => TryGetDirectory((int)Math.Min(ReadUInt32(4), int.MaxValue), out directory);

        /// <summary>Checks the offset of an image file directory.</summary>
        public bool TryGetDirectory(int offset, out int directory)
        {
            directory = offset;
            return offset >= 8 && offset <= _tiff.Length - 2;
        }

        /// <summary>Returns the offset of the entry of a tag in a directory, or -1.</summary>
        private int FindEntry(int directory, int tag)
        {
            var count = ReadUInt16(directory);
            for (var i = 0; i < count; i++)
            {
                var entry = directory + 2 + (i * 12);
                if (entry + 12 > _tiff.Length)
                    return -1;

                if (ReadUInt16(entry) == tag)
                    return entry;
            }

            return -1;
        }

        /// <summary>Returns the first value of a tag whose type is SHORT (3) or LONG (4), or null.</summary>
        public uint? GetInteger(int directory, int tag)
        {
            var entry = FindEntry(directory, tag);
            if (entry < 0 || ReadUInt32(entry + 4) < 1)
                return null;

            return ReadUInt16(entry + 2) switch
            {
                3 => (uint)ReadUInt16(entry + 8),
                4 => ReadUInt32(entry + 8),
                _ => null,
            };
        }

        /// <summary>Returns the first value of a tag whose type is RATIONAL (5: two LONG values, the numerator and the denominator), or null.</summary>
        public double? GetRational(int directory, int tag)
        {
            var entry = FindEntry(directory, tag);
            if (entry < 0 || ReadUInt16(entry + 2) != 5 || ReadUInt32(entry + 4) < 1)
                return null;

            var offset = ReadUInt32(entry + 8);
            if (offset > _tiff.Length - 8)
                return null;

            var denominator = ReadUInt32((int)offset + 4);
            return denominator == 0 ? null : (double)ReadUInt32((int)offset) / denominator;
        }
    }
}
