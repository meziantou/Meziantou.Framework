using System.Buffers;
using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Assembles an Ultra HDR file from the JPEG files of its primary image and of its gain map image, both written by the
/// JPEG encoder: the gain map image gets the metadata of ISO 21496-1 (an APP2 segment) and the <c>hdrgm</c> XMP properties;
/// the primary image gets the version of ISO 21496-1 (an APP2 segment without parameters), the container directory of
/// Ultra HDR in its XMP packet, and the Multi-Picture Format index (CIPA DC-007, an APP2 <c>MPF</c> segment) that locates the
/// gain map image, which follows it. <see cref="JpegAuxiliaryScanner"/> reads the result.
/// </summary>
/// <remarks>
/// The new segments follow the application segments that the encoder wrote (after <c>APP0</c>, so that the file stays a
/// JFIF file); an XMP packet the image already has is merged in place. libultrahdr 1.4 writes the same index: the attributes
/// of the primary image are those of a baseline primary image (0x030000), and the offset of the gain map image is counted
/// from the byte order mark of the index.
/// </remarks>
internal static class JpegGainMapAssembler
{
    private const byte MarkerApp1 = 0xE1;
    private const byte MarkerApp2 = 0xE2;
    private const int MpfPayloadLength = 4 + 8 + 2 + (3 * 12) + 4 + (2 * 16);
    private const int MpEntriesOffset = 8 + 2 + (3 * 12) + 4;

    private static ReadOnlySpan<byte> XmpSignature => "http://ns.adobe.com/xap/1.0/\0"u8;

    private static ReadOnlySpan<byte> MpfSignature => "MPF\0"u8;

    /// <summary>Writes the Ultra HDR file.</summary>
    /// <param name="output">The output.</param>
    /// <param name="primary">The JPEG file of the primary image.</param>
    /// <param name="gainMap">The JPEG file of the gain map image.</param>
    /// <param name="metadata">The parameters of the gain map.</param>
    /// <exception cref="UnsupportedImageFeatureException">An XMP packet is too large for a segment, or a value cannot be stored.</exception>
    /// <exception cref="InvalidImageContentException">The XMP packet of an image is not well-formed.</exception>
    public static void Write(IBufferWriter<byte> output, ReadOnlySpan<byte> primary, ReadOnlySpan<byte> gainMap, GainMapMetadata metadata)
    {
        var iso = GainMapMetadataWriter.WriteIso(metadata, ImageFormat.Jpeg);
        var gainMapFile = Rewrite(gainMap, xmp => GainMapMetadataWriter.MergeGainMapXmp(xmp, metadata), [.. GainMapMetadataReader.IsoUrn, .. iso], gainMapLength: -1);
        var primaryFile = Rewrite(primary, xmp => GainMapMetadataWriter.MergePrimaryXmp(xmp, gainMapFile.Length), [.. GainMapMetadataReader.IsoUrn, 0, 0, 0, 0], gainMapFile.Length);
        output.Write(primaryFile);
        output.Write(gainMapFile);
    }

    // The file with its XMP packet merged and the new APP2 segments after its application segments; for the primary image,
    // the index of the two images too, whose offsets are known once the length of the file is
    private static byte[] Rewrite(ReadOnlySpan<byte> jpeg, Func<byte[], byte[]> mergeXmp, ReadOnlySpan<byte> isoPayload, int gainMapLength)
    {
        if (jpeg.Length < 4 || jpeg[0] is not 0xFF || jpeg[1] is not 0xD8)
            throw new InvalidOperationException("The JPEG encoder did not write a JPEG file.");

        // The application segments and comments that precede the tables
        var position = 2;
        var xmpStart = -1;
        var xmpEnd = -1;
        var insertion = 2;
        while (position + 4 <= jpeg.Length && jpeg[position] is 0xFF && jpeg[position + 1] is (>= 0xE0 and <= 0xEF) or 0xFE)
        {
            var marker = jpeg[position + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 2)..]);
            var payload = jpeg.Slice(position + 4, length - 2);
            if (marker is MarkerApp1 && payload.StartsWith(XmpSignature) && xmpStart < 0)
            {
                xmpStart = position;
                xmpEnd = position + 2 + length;
            }

            position += 2 + length;
            if (marker is not 0xFE)
            {
                insertion = position;
            }
        }

        var existingXmp = xmpStart < 0 ? [] : jpeg[(xmpStart + 4 + XmpSignature.Length)..xmpEnd].ToArray();
        var xmp = mergeXmp(existingXmp);
        if (XmpSignature.Length + xmp.Length > JpegMetadataWriter.MaxSegmentPayload)
            throw new UnsupportedImageFeatureException("The XMP packet of the gain map image is too large for a JPEG segment.", ImageFormat.Jpeg, "Metadata: XMP packet");

        var result = new ArrayBufferWriter<byte>(jpeg.Length + xmp.Length + isoPayload.Length + 256);
        if (xmpStart < 0)
        {
            // A new packet follows the APP0 (JFIF) and APP1 (EXIF) segments, and precedes the others
            var xmpPosition = FindNewXmpPosition(jpeg, insertion);
            result.Write(jpeg[..xmpPosition]);
            JpegMetadataWriter.WriteSegment(result, MarkerApp1, [.. XmpSignature, .. xmp]);
            result.Write(jpeg[xmpPosition..insertion]);
        }
        else
        {
            result.Write(jpeg[..xmpStart]);
            JpegMetadataWriter.WriteSegment(result, MarkerApp1, [.. XmpSignature, .. xmp]);
            result.Write(jpeg[xmpEnd..insertion]);
        }

        JpegMetadataWriter.WriteSegment(result, MarkerApp2, isoPayload);
        var mpfStart = -1;
        if (gainMapLength >= 0)
        {
            mpfStart = result.WrittenCount;
            JpegMetadataWriter.WriteSegment(result, MarkerApp2, new byte[MpfPayloadLength]);
        }

        result.Write(jpeg[insertion..]);
        var file = result.WrittenSpan.ToArray();
        if (mpfStart >= 0)
        {
            WriteIndex(file.AsSpan(mpfStart + 4), file.Length, mpfStart + 4 + MpfSignature.Length, gainMapLength);
        }

        return file;
    }

    private static int FindNewXmpPosition(ReadOnlySpan<byte> jpeg, int end)
    {
        var position = 2;
        while (position < end && jpeg[position + 1] is 0xE0 or MarkerApp1)
        {
            position += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 2)..]);
        }

        return position;
    }

    // CIPA DC-007, section 5.2: a big-endian TIFF header, a directory of three fields (the version of the format, the number
    // of images and the offset of their entries) and the entries of the two images, whose offsets are counted from the byte
    // order mark (0 for the primary image)
    private static void WriteIndex(Span<byte> payload, int primaryLength, int headerOffset, int gainMapLength)
    {
        MpfSignature.CopyTo(payload);
        var index = payload[MpfSignature.Length..];
        "MM\0*"u8.CopyTo(index);
        BinaryPrimitives.WriteUInt32BigEndian(index[4..], 8);
        BinaryPrimitives.WriteUInt16BigEndian(index[8..], 3);
        WriteField(index[10..], 0xB000, type: 7, count: 4, "0100"u8);
        WriteField(index[22..], 0xB001, type: 4, count: 1, [0, 0, 0, 2]);
        Span<byte> entriesOffset = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(entriesOffset, MpEntriesOffset);
        WriteField(index[34..], 0xB002, type: 7, count: 32, entriesOffset);
        BinaryPrimitives.WriteUInt32BigEndian(index[46..], 0); // no other directory

        var entries = index[MpEntriesOffset..];
        BinaryPrimitives.WriteUInt32BigEndian(entries, 0x030000); // baseline primary image
        BinaryPrimitives.WriteUInt32BigEndian(entries[4..], (uint)primaryLength);
        BinaryPrimitives.WriteUInt32BigEndian(entries[8..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(entries[16..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(entries[20..], (uint)gainMapLength);
        BinaryPrimitives.WriteUInt32BigEndian(entries[24..], (uint)(primaryLength - headerOffset));

        static void WriteField(Span<byte> field, ushort tag, ushort type, uint count, ReadOnlySpan<byte> value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(field, tag);
            BinaryPrimitives.WriteUInt16BigEndian(field[2..], type);
            BinaryPrimitives.WriteUInt32BigEndian(field[4..], count);
            value.CopyTo(field[8..]);
        }
    }
}
