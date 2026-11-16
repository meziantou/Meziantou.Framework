using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Builds the EXIF profile of a DNG file: the EXIF IFD that IFD 0 points to (DNG prefers that location to the TIFF-EP
/// tags of IFD 0, chapter 2) and the tags of IFD 0 that EXIF keeps there (make, model, orientation, software, date, artist,
/// copyright, description), copied into a little-endian TIFF structure of their own, as the EXIF data of other formats is.
/// </summary>
/// <remarks>
/// The values are copied with their type and count, in little-endian order. The directories the EXIF IFD points to (the
/// interoperability IFD, a maker note's offsets) are not followed: their pointers are left out, and a maker note keeps its
/// bytes. The file has no EXIF profile when IFD 0 has no EXIF IFD, or when it cannot be read.
/// </remarks>
internal static class DngExifExtractor
{
    private const ushort InteroperabilityIfdTag = 40965;
    private const ushort GpsIfdTag = 34853;

    private static readonly ushort[] Ifd0Tags = [TiffFormat.ImageDescriptionTag, DngFormat.MakeTag, DngFormat.ModelTag, TiffFormat.OrientationTag, TiffFormat.SoftwareTag, 306, 315, 33432];

    /// <summary>Extracts the EXIF profile, or returns <see langword="null"/>.</summary>
    /// <param name="document">The DNG file.</param>
    /// <param name="maxBytes">The largest profile that is built.</param>
    public static byte[]? TryExtract(DngDocument document, long maxBytes)
    {
        var ifd0 = document.Ifd0;
        if (!ifd0.TryGetUnsigned(TiffFormat.ExifIfdPointerTag, out var offset))
            return null;

        try
        {
            var exif = TiffDirectory.Read(ifd0.Source, document.Header, offset);
            var first = Copy(ifd0, ifd0.Fields.ToArray().Where(field => Array.IndexOf(Ifd0Tags, field.Tag) >= 0), maxBytes);
            var second = Copy(exif, exif.Fields.ToArray().Where(field => field.Tag is not (InteroperabilityIfdTag or GpsIfdTag or TiffFormat.ExifIfdPointerTag)), maxBytes);
            if (first is null || second is null)
                return null;

            // IFD 0 (with the pointer to the EXIF IFD), then the EXIF IFD, each followed by its out-of-line values
            var entries0 = first.Count + 1;
            var ifd0Length = 2 + (entries0 * 12) + 4 + first.Sum(entry => Padded(entry.Values.Length));
            var exifOffset = 8 + ifd0Length;
            var exifLength = 2 + (second.Count * 12) + 4 + second.Sum(entry => Padded(entry.Values.Length));
            if (exifOffset + exifLength > maxBytes)
                return null;

            var result = new byte[exifOffset + exifLength];
            "II*\0"u8.CopyTo(result);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 8);
            var withPointer = first.Append(new Entry(TiffFormat.ExifIfdPointerTag, TiffFieldType.Long, 1, LittleEndian((uint)exifOffset))).OrderBy(entry => entry.Tag).ToList();
            WriteDirectory(result, 8, withPointer);
            WriteDirectory(result, exifOffset, [.. second.OrderBy(entry => entry.Tag)]);
            return result;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    private static byte[] LittleEndian(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static int Padded(int length) => length <= 4 ? 0 : (length + 1) & ~1;

    private static List<Entry>? Copy(TiffDirectory directory, IEnumerable<TiffField> fields, long maxBytes)
    {
        var entries = new List<Entry>();
        var total = 0L;
        foreach (var field in fields)
        {
            var size = TiffFieldTypes.GetSize(field.Type);
            if (size == 0 || field.Type is TiffFieldType.Long8 or TiffFieldType.SLong8 or TiffFieldType.Ifd8 or TiffFieldType.Ifd || field.Count > uint.MaxValue)
                continue;

            var length = TiffDirectory.GetValueLength(field);
            total += length;
            if (total > maxBytes)
                return null;

            using var buffer = directory.ReadValues(field);
            var values = buffer.Span.ToArray();
            if (directory.Header.IsBigEndian)
            {
                // Each element of the numeric types, and both halves of a rational, in little-endian order
                var element = field.Type is TiffFieldType.Rational or TiffFieldType.SRational ? 4 : size;
                if (element > 1)
                {
                    for (var i = 0; i + element <= values.Length; i += element)
                    {
                        values.AsSpan(i, element).Reverse();
                    }
                }
            }

            entries.Add(new Entry(field.Tag, field.Type, (uint)field.Count, values));
        }

        return entries;
    }

    private static void WriteDirectory(byte[] data, int offset, List<Entry> entries)
    {
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)entries.Count);
        var valueOffset = offset + 2 + (entries.Count * 12) + 4;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var position = offset + 2 + (i * 12);
            BinaryPrimitives.WriteUInt16LittleEndian(span[position..], entry.Tag);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(position + 2)..], (ushort)entry.Type);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 4)..], entry.Count);
            if (entry.Values.Length <= 4)
            {
                entry.Values.CopyTo(span[(position + 8)..]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(span[(position + 8)..], (uint)valueOffset);
                entry.Values.CopyTo(span[valueOffset..]);
                valueOffset += Padded(entry.Values.Length);
            }
        }

        // The next directory: none
        BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 2 + (entries.Count * 12))..], 0);
    }

    private sealed record Entry(ushort Tag, TiffFieldType Type, uint Count, byte[] Values);
}
