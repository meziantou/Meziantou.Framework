using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Lists the images that follow the primary image of a JPEG file in a Multi-Picture Format container (CIPA DC-007: an
/// APP2 segment <c>MPF</c> whose index gives the offset, the length and the type of each image). An image is a gain map
/// when it carries the ISO 21496-1 metadata (an APP2 segment) or the <c>hdrgm</c> XMP properties of Ultra HDR, a
/// thumbnail when the index says so, and another image otherwise.
/// </summary>
/// <remarks>
/// The index is optional metadata: a file whose index cannot be read (a truncated segment, an image outside the file) lists
/// no image, and its primary image decodes as usual.
/// </remarks>
internal static class JpegAuxiliaryScanner
{
    private const ushort NumberOfImagesTag = 0xB001;
    private const ushort MpEntryTag = 0xB002;
    private const int MpEntryLength = 16;

    // CIPA DC-007, table 4: the type of the image, in the low 24 bits of the attributes of an entry
    private const int LargeThumbnailClass = 0x010000;

    private static ReadOnlySpan<byte> MpfSignature => "MPF\0"u8;

    private static ReadOnlySpan<byte> XmpSignature => "http://ns.adobe.com/xap/1.0/\0"u8;

    public static List<AuxiliaryImage> Scan(AuxiliaryImageCollection collection, ReadOnlySpan<byte> data)
    {
        var result = new List<AuxiliaryImage>();
        if (!TryFindMpfIndex(data, out var headerOffset, out var index))
            return result;

        var entries = ReadEntries(data, headerOffset, index);
        foreach (var (type, offset, length) in entries)
        {
            var image = data.Slice(offset, length);
            if (!TryReadSegments(image, out var size, out var iso, out var xmp))
                continue;

            var kind = (type & 0xFF0000) is LargeThumbnailClass ? AuxiliaryImageKind.Thumbnail : AuxiliaryImageKind.Other;
            var entry = new AuxiliaryImage(collection, kind, auxiliaryType: null, size) { JpegRange = (offset, length) };
            try
            {
                // ISO 21496-1 first: Ultra HDR 1.1 files carry both, and readers prefer it
                var metadata = iso.IsEmpty ? null : GainMapMetadataReader.ReadIso(iso, ImageFormat.Jpeg);
                metadata ??= xmp.IsEmpty ? null : GainMapMetadataReader.ReadXmp(xmp, ImageFormat.Jpeg);
                if (metadata is not null)
                {
                    entry.Kind = AuxiliaryImageKind.GainMap;
                    entry.GainMapMetadata = metadata;
                }
            }
            catch (InvalidImageContentException exception)
            {
                entry.Kind = AuxiliaryImageKind.GainMap;
                entry.MetadataError = exception;
            }

            result.Add(entry);
        }

        return result;
    }

    // The segments before the first scan of the primary image hold the index
    private static bool TryFindMpfIndex(ReadOnlySpan<byte> data, out int headerOffset, out ReadOnlySpan<byte> index)
    {
        var position = 2;
        while (TryReadSegment(data, ref position, out var marker, out var payloadOffset, out var payload))
        {
            if (marker is 0xE2 && payload.StartsWith(MpfSignature))
            {
                headerOffset = payloadOffset + MpfSignature.Length;
                index = payload[MpfSignature.Length..];
                return true;
            }
        }

        headerOffset = 0;
        index = default;
        return false;
    }

    // The index is a TIFF structure (CIPA DC-007, section 5.2): a byte order, an offset to the first directory, and the
    // entries of the images, whose offsets are relative to the byte order mark (the first image, at offset 0, is the primary)
    private static List<(int Type, int Offset, int Length)> ReadEntries(ReadOnlySpan<byte> data, int headerOffset, ReadOnlySpan<byte> index)
    {
        var result = new List<(int Type, int Offset, int Length)>();
        if (index.Length < 8)
            return result;

        bool bigEndian;
        if (index.StartsWith("MM\0*"u8))
        {
            bigEndian = true;
        }
        else if (index.StartsWith("II*\0"u8))
        {
            bigEndian = false;
        }
        else
        {
            return result;
        }

        var directory = ReadUInt32(index[4..], bigEndian);
        if (directory > (uint)index.Length - 2)
            return result;

        var count = ReadUInt16(index[(int)directory..], bigEndian);
        var entriesOffset = (int)directory + 2;
        if (count * 12L > index.Length - entriesOffset)
            return result;

        uint imageCount = 0;
        uint entryOffset = 0;
        uint entryLength = 0;
        for (var i = 0; i < count; i++)
        {
            var field = index.Slice(entriesOffset + (i * 12), 12);
            var tag = ReadUInt16(field, bigEndian);
            switch (tag)
            {
                case NumberOfImagesTag:
                    imageCount = ReadUInt32(field[8..], bigEndian);
                    break;
                case MpEntryTag:
                    entryLength = ReadUInt32(field[4..], bigEndian);
                    entryOffset = ReadUInt32(field[8..], bigEndian);
                    break;
                default:
                    break;
            }
        }

        if (imageCount < 2 || entryLength != imageCount * MpEntryLength || entryOffset > index.Length || entryLength > index.Length - entryOffset)
            return result;

        for (var i = 1; i < imageCount; i++)
        {
            var entry = index.Slice((int)entryOffset + (i * MpEntryLength), MpEntryLength);
            var attributes = ReadUInt32(entry, bigEndian);
            var length = ReadUInt32(entry[4..], bigEndian);
            var offset = ReadUInt32(entry[8..], bigEndian);
            if (offset is 0 || length < 4 || offset > data.Length - headerOffset || length > data.Length - headerOffset - offset)
                continue;

            var start = headerOffset + (int)offset;
            if (data[start] is not 0xFF || data[start + 1] is not 0xD8)
                continue;

            result.Add(((int)(attributes & 0xFFFFFF), start, (int)length));
        }

        return result;
    }

    // The size of an image (its frame header), its ISO 21496-1 metadata and its XMP packet, from its segments
    private static bool TryReadSegments(ReadOnlySpan<byte> image, out Size size, out ReadOnlySpan<byte> iso, out ReadOnlySpan<byte> xmp)
    {
        size = default;
        iso = default;
        xmp = default;
        var position = 2;
        while (TryReadSegment(image, ref position, out var marker, out _, out var payload))
        {
            if (marker is 0xE2 && payload.StartsWith(GainMapMetadataReader.IsoUrn))
            {
                iso = payload[GainMapMetadataReader.IsoUrn.Length..];
            }
            else if (marker is 0xE1 && payload.StartsWith(XmpSignature))
            {
                xmp = payload[XmpSignature.Length..];
            }
            else if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC) && payload.Length >= 5)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(payload[1..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(payload[3..]);
                if (width is 0 || height is 0)
                    return false;

                size = new Size(width, height);
                return true;
            }
        }

        return false;
    }

    // A marker segment before the first scan: its marker, and its payload after the length field
    private static bool TryReadSegment(ReadOnlySpan<byte> data, scoped ref int position, out byte marker, out int payloadOffset, out ReadOnlySpan<byte> payload)
    {
        marker = 0;
        payloadOffset = 0;
        payload = default;
        while (position < data.Length && data[position] is 0xFF && position + 1 < data.Length && data[position + 1] is 0xFF)
        {
            position++;
        }

        if (position + 4 > data.Length || data[position] is not 0xFF)
            return false;

        marker = data[position + 1];
        if (marker is 0xDA or 0xD9 or 0xD8 or 0x00)
            return false;

        var length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
        if (length < 2 || position + 2 + length > data.Length)
            return false;

        payloadOffset = position + 4;
        payload = data.Slice(payloadOffset, length - 2);
        position += 2 + length;
        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool bigEndian) => bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(data) : BinaryPrimitives.ReadUInt16LittleEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool bigEndian) => bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(data) : BinaryPrimitives.ReadUInt32LittleEndian(data);
}
