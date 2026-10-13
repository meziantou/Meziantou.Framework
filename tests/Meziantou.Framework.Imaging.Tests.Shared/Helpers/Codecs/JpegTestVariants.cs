using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests.Codecs;

/// <summary>
/// Variants of two JPEG files of an encoder, made by moving, repeating and changing their segments: what a decoder reads
/// and no encoder of the corpus writes. Each one decodes to the pixels of its source, or is not a file a decoder is
/// asked to read.
/// </summary>
internal static class JpegTestVariants
{
    private const int ScanData = -1;

    /// <summary>A variant of a JPEG file.</summary>
    /// <param name="Name">What was done to the file.</param>
    /// <param name="File">The file.</param>
    /// <param name="IsRecompressed">Whether a JPEG XL file can describe it: <see cref="JpegXlTranscoder"/> recompresses it, and refuses it otherwise.</param>
    /// <param name="IsRecompressedByCjxl">Whether cjxl 0.12.0 recompresses it.</param>
    public sealed record Variant(string Name, byte[] File, bool IsRecompressed, bool IsRecompressedByCjxl);

    /// <summary>Creates the variants.</summary>
    /// <param name="baseline">A baseline file of one scan, with a table of the luma in the slot 0 and one of the chroma in the slot 1 (<c>jpeg/baseline-420</c>).</param>
    /// <param name="twoScans">A baseline file whose first scan holds the luma and whose second scan holds the chroma, with the same tables (<c>jpeg/multiscan-chroma-interleaved</c>).</param>
    public static IReadOnlyList<Variant> Create(byte[] baseline, byte[] twoScans)
    {
        var variants = new List<Variant>();
        void Add(string name, IEnumerable<(int Marker, byte[] Bytes)> pieces, bool isRecompressed = true, bool isRecompressedByCjxl = true)
            => variants.Add(new Variant(name, [.. pieces.SelectMany(piece => piece.Bytes)], isRecompressed, isRecompressed && isRecompressedByCjxl));

        // Bytes after the last block of a scan, before the next marker
        var pieces = Split(baseline);
        var data = pieces.FindLastIndex(piece => piece.Marker == ScanData);
        Add("data-after-scan", [.. pieces[..data], (ScanData, [.. pieces[data].Bytes, 0x12, 0x34, 0x56]), .. pieces[(data + 1)..]]);
        Add("zero-after-scan", [.. pieces[..data], (ScanData, [.. pieces[data].Bytes, 0x00]), .. pieces[(data + 1)..]]);
        Add("stuffed-byte-after-scan", [.. pieces[..data], (ScanData, [.. pieces[data].Bytes, 0xFF, 0x00, 0x07]), .. pieces[(data + 1)..]]);

        // A table that no component uses: it can only be described as a copy of the table defined before it
        var lumaTable = pieces.FindIndex(piece => IsTable(piece, slot: 0));
        var chromaTable = pieces.FindIndex(piece => IsTable(piece, slot: 1));
        Assert.True(lumaTable >= 0 && chromaTable > lumaTable, "The baseline file defines the table of the luma, then the one of the chroma, in two segments.");
        Add("unused-copy-of-previous-table", [.. pieces[..(chromaTable + 1)], WithSlot(pieces[chromaTable], 3), .. pieces[(chromaTable + 1)..]]);
        Add("unused-copy-of-another-table", [.. pieces[..(chromaTable + 1)], WithSlot(pieces[lumaTable], 3), .. pieces[(chromaTable + 1)..]], isRecompressed: false);
        Add("unused-table", [.. pieces[..(chromaTable + 1)], WithSlot(Changed(pieces[chromaTable]), 3), .. pieces[(chromaTable + 1)..]], isRecompressed: false);

        // Tables between the scans
        pieces = Split(twoScans);
        lumaTable = pieces.FindIndex(piece => IsTable(piece, slot: 0));
        chromaTable = pieces.FindIndex(piece => IsTable(piece, slot: 1));
        var frame = pieces.FindIndex(piece => piece.Marker == 0xC0);
        var secondScan = pieces.FindIndex(pieces.FindIndex(piece => piece.Marker == 0xDA) + 1, piece => piece.Marker == 0xDA);
        var end = pieces.FindIndex(piece => piece.Marker == 0xD9);
        Assert.True(lumaTable >= 0 && chromaTable > lumaTable && frame > chromaTable && secondScan > frame && end > secondScan, "The file of two scans defines its two tables, in two segments, before its frame.");

        // The first scan has one component, the first of the frame
        var firstScan = pieces[pieces.FindIndex(piece => piece.Marker == 0xDA)].Bytes;
        Assert.True(firstScan[4] == 1 && firstScan[5] == pieces[frame].Bytes[10], "The first scan of the file of two scans holds the luma only.");

        var withoutChromaTable = pieces.Where((_, index) => index != chromaTable).ToList();
        var scanOfChroma = secondScan - 1;
        Add("table-defined-before-its-scan", [.. withoutChromaTable[..scanOfChroma], pieces[chromaTable], .. withoutChromaTable[scanOfChroma..]]);
        Add("table-repeated-between-scans", [.. pieces[..secondScan], pieces[chromaTable], .. pieces[secondScan..]]);
        Add("table-twice-before-scans", [.. pieces[..lumaTable], pieces[lumaTable], .. pieces[lumaTable..]]);
        Add("other-table-repeated-between-scans", [.. pieces[..secondScan], pieces[lumaTable], .. pieces[secondScan..]], isRecompressed: false);
        Add("table-changed-after-its-scan", [.. pieces[..secondScan], Changed(pieces[lumaTable]), .. pieces[secondScan..]], isRecompressed: false);
        Add("table-changed-after-last-scan", [.. pieces[..end], Changed(pieces[chromaTable]), .. pieces[end..]], isRecompressed: false);
        Add("table-changed-before-first-scan", [.. pieces[..chromaTable], Changed(pieces[chromaTable]), .. pieces[chromaTable..]], isRecompressed: false);

        // One slot for the two tables: the one of the luma, the scan of the luma, then the one of the chroma
        var frameBytes = (byte[])pieces[frame].Bytes.Clone();
        for (var c = 0; c < frameBytes[9]; c++)
        {
            frameBytes[12 + (3 * c)] = 0;
        }

        List<(int Marker, byte[] Bytes)> oneSlot = [.. pieces[..chromaTable], .. pieces[(chromaTable + 1)..frame], (0xC0, frameBytes), .. pieces[(frame + 1)..]];
        Add("slot-reused-between-scans", [.. oneSlot[..scanOfChroma], WithSlot(pieces[chromaTable], 0), .. oneSlot[scanOfChroma..]], isRecompressedByCjxl: false);

        // A restart interval defined after the first scan, larger than the scan that follows: no restart marker is written
        Add("restart-interval-after-first-scan", [.. pieces[..secondScan], (0xDD, [0xFF, 0xDD, 0x00, 0x04, 0xFF, 0xFF]), .. pieces[secondScan..]]);
        return variants;
    }

    /// <summary>
    /// Splits a JPEG file into its marker segments (the marker and the segment), the entropy-coded data of each scan
    /// (<see cref="ScanData"/>), and its end of image with what follows it.
    /// </summary>
    private static List<(int Marker, byte[] Bytes)> Split(byte[] file)
    {
        Assert.True(file is [0xFF, 0xD8, ..], "The file starts with the start of image marker.");
        var pieces = new List<(int Marker, byte[] Bytes)> { (0xD8, file[..2]) };
        var position = 2;
        while (true)
        {
            Assert.Equal((byte)0xFF, file[position]);
            var marker = file[position + 1];
            if (marker == 0xD9)
            {
                pieces.Add((marker, file[position..]));
                return pieces;
            }

            var length = 2 + BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(position + 2));
            pieces.Add((marker, file[position..(position + length)]));
            position += length;
            if (marker == 0xDA)
            {
                // To the next marker that is neither a stuffed byte nor a restart marker
                var start = position;
                while (file[position] != 0xFF || file[position + 1] is 0x00 or (>= 0xD0 and <= 0xD7))
                {
                    position++;
                }

                pieces.Add((ScanData, file[start..position]));
            }
        }
    }

    private static bool IsTable((int Marker, byte[] Bytes) piece, int slot) => piece.Marker == 0xDB && piece.Bytes.Length == 4 + 65 && (piece.Bytes[4] & 15) == slot;

    private static (int Marker, byte[] Bytes) WithSlot((int Marker, byte[] Bytes) table, int slot)
    {
        var bytes = (byte[])table.Bytes.Clone();
        bytes[4] = (byte)((bytes[4] & 0xF0) | slot);
        return (table.Marker, bytes);
    }

    private static (int Marker, byte[] Bytes) Changed((int Marker, byte[] Bytes) table)
    {
        var bytes = (byte[])table.Bytes.Clone();
        bytes[10] = (byte)((bytes[10] % 200) + 7);
        return (table.Marker, bytes);
    }
}
