namespace Meziantou.Framework.Fonts;

/// <summary>The table directory of one face: where each table is in the bytes of the font.</summary>
/// <remarks>
/// OpenType specification, "Organization of an OpenType Font". Every record is checked when the directory is read: a
/// table lies inside the file, and a tag appears once. Checksums are not verified: they detect accidental corruption
/// only, and many fonts in use carry wrong ones.
/// </remarks>
internal sealed class SfntDirectory
{
    private const int HeaderLength = 12;
    private const int RecordLength = 16;

    private readonly uint[] _tags;
    private readonly int[] _offsets;
    private readonly int[] _lengths;

    private SfntDirectory(uint version, uint[] tags, int[] offsets, int[] lengths)
    {
        Version = version;
        _tags = tags;
        _offsets = offsets;
        _lengths = lengths;
    }

    /// <summary>Gets the signature of the face: TrueType outlines or CFF outlines.</summary>
    public uint Version { get; }

    public int TableCount => _tags.Length;

    /// <summary>Gets the tags of the tables, in increasing order.</summary>
    public ReadOnlySpan<uint> Tags => _tags;

    /// <summary>Reads the directory at an offset of the file.</summary>
    public static SfntDirectory Read(ReadOnlySpan<byte> file, long offset)
    {
        var length = GetLength(FontBinary.Slice(file, offset, HeaderLength));
        return ReadDirectory(FontBinary.Slice(file, offset, length), file.Length);
    }

    /// <summary>Gets the length of a directory, its header and its records, from its header.</summary>
    /// <param name="header">The first <see cref="HeaderLength"/> bytes of the directory.</param>
    public static int GetLength(ReadOnlySpan<byte> header)
    {
        var version = FontBinary.UInt32(header, 0);
        switch (version)
        {
            case FontTags.TrueType or FontTags.AppleTrueType or FontTags.OpenTypeCff:
                break;

            case FontTags.PostScriptType1:
                throw FontErrors.Unsupported("PostScript Type 1 fonts in an sfnt wrapper are not supported.", "Type 1 outlines");

            default:
                throw FontErrors.Invalid($"a face has the unknown signature 0x{version:X8}.");
        }

        return HeaderLength + (FontBinary.UInt16(header, 4) * RecordLength);
    }

    /// <summary>
    /// Reads a directory from its bytes, the header and the records, without the rest of the file: a table must lie
    /// inside a file of <paramref name="fileLength"/> bytes.
    /// </summary>
    public static SfntDirectory ReadDirectory(ReadOnlySpan<byte> directory, long fileLength)
    {
        var header = FontBinary.Slice(directory, 0, HeaderLength);
        var version = FontBinary.UInt32(header, 0);
        var count = FontBinary.UInt16(header, 4);
        var records = FontBinary.Slice(directory, HeaderLength, GetLength(header) - HeaderLength);

        // The offsets are kept as 32-bit integers: a table lies within the first 2 GiB of the file.
        var end = Math.Min(fileLength, int.MaxValue);
        var tags = new uint[count];
        var offsets = new int[count];
        var lengths = new int[count];
        for (var i = 0; i < count; i++)
        {
            var record = records.Slice(i * RecordLength, RecordLength);
            var tag = FontBinary.UInt32(record, 0);
            var tableOffset = FontBinary.UInt32(record, 8);
            var tableLength = FontBinary.UInt32(record, 12);
            if ((long)tableOffset + tableLength > end)
                throw FontErrors.InvalidTable(tag, "lies outside the file.");

            tags[i] = tag;
            offsets[i] = (int)tableOffset;
            lengths[i] = (int)tableLength;
        }

        // The records are sorted by tag in a conforming font; sort them here so that a lookup never depends on it.
        var order = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
        }

        var sortedTags = (uint[])tags.Clone();
        Array.Sort(sortedTags, order);
        var sortedOffsets = new int[count];
        var sortedLengths = new int[count];
        for (var i = 0; i < count; i++)
        {
            sortedOffsets[i] = offsets[order[i]];
            sortedLengths[i] = lengths[order[i]];
            if (i > 0 && sortedTags[i] == sortedTags[i - 1])
                throw FontErrors.InvalidTable(sortedTags[i], "is listed twice.");
        }

        return new SfntDirectory(version, sortedTags, sortedOffsets, sortedLengths);
    }

    /// <summary>Finds a table.</summary>
    /// <returns><see langword="false"/> when the face does not have the table.</returns>
    public bool TryFind(uint tag, out int offset, out int length)
    {
        var index = Array.BinarySearch(_tags, tag);
        if (index < 0)
        {
            offset = 0;
            length = 0;
            return false;
        }

        offset = _offsets[index];
        length = _lengths[index];
        return true;
    }
}
