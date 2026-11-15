using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Dng;

/// <summary>
/// The EXIF profile the library documents for a DNG file, assembled independently from the directories of the file
/// (<see cref="TiffFile"/>): a little-endian TIFF structure whose IFD 0 holds the description, make, model, orientation, software, date,
/// artist and copyright of IFD 0 of the file and a pointer to a copy of its EXIF IFD (without the GPS, interoperability
/// and EXIF pointers, nor the values of 64-bit and IFD types), each directory sorted by tag and followed by its values.
/// </summary>
public static class DngExifReference
{
    private static readonly ushort[] Ifd0Tags = [270, 271, 272, 274, 305, 306, 315, 33432];

    /// <summary>Builds the EXIF profile, or returns <see langword="null"/> when IFD 0 has no EXIF IFD.</summary>
    public static byte[]? Build(byte[] data)
    {
        var file = new TiffFile(data);
        var ifd0 = file.ReadDirectory(file.FirstDirectory);
        if (ifd0.TryNumber(34665) is not { } offset)
            return null;

        var exif = file.ReadDirectory((uint)offset);
        var first = Entries(file, ifd0).Where(entry => Array.IndexOf(Ifd0Tags, entry.Tag) >= 0).ToList();
        var second = Entries(file, exif).Where(entry => entry.Tag is not (40965 or 34853 or 34665)).ToList();
        var ifd0Length = 2 + ((first.Count + 1) * 12) + 4 + first.Sum(entry => Padded(entry.Values.Length));
        var exifOffset = 8 + ifd0Length;
        var exifLength = 2 + (second.Count * 12) + 4 + second.Sum(entry => Padded(entry.Values.Length));
        var result = new byte[exifOffset + exifLength];
        "II*\0"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 8);
        var pointer = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(pointer, (uint)exifOffset);
        first.Add((34665, 4, 1u, pointer));
        Write(result, 8, [.. first.OrderBy(entry => entry.Tag)]);
        Write(result, exifOffset, [.. second.OrderBy(entry => entry.Tag)]);
        return result;
    }

    private static int Padded(int length) => length <= 4 ? 0 : (length + 1) & ~1;

    private static IEnumerable<(ushort Tag, ushort Type, uint Count, byte[] Values)> Entries(TiffFile file, TiffFile.Directory directory)
    {
        foreach (var (tag, type, count, values) in directory.RawEntries)
        {
            var size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 => 4, 5 or 10 or 12 => 8, _ => 0 };
            if (size == 0)
                continue;

            var copy = values.ToArray();
            if (file.IsBigEndian)
            {
                var element = type is 5 or 10 ? 4 : size;
                for (var i = 0; element > 1 && i + element <= copy.Length; i += element)
                {
                    copy.AsSpan(i, element).Reverse();
                }
            }

            yield return (tag, type, (uint)count, copy);
        }
    }

    private static void Write(byte[] data, int offset, List<(ushort Tag, ushort Type, uint Count, byte[] Values)> entries)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), (ushort)entries.Count);
        var valueOffset = offset + 2 + (entries.Count * 12) + 4;
        for (var i = 0; i < entries.Count; i++)
        {
            var (tag, type, count, values) = entries[i];
            var position = offset + 2 + (i * 12);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(position), tag);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(position + 2), type);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position + 4), count);
            if (values.Length <= 4)
            {
                values.CopyTo(data, position + 8);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position + 8), (uint)valueOffset);
                values.CopyTo(data, valueOffset);
                valueOffset += Padded(values.Length);
            }
        }
    }
}
