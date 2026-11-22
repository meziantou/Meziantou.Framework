using System.Buffers.Binary;

namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>
/// A MOBI book (Mobipocket, Kindle): a Palm database whose first record holds the headers of the book, followed by the
/// records of its compressed text and its resources (images, fonts). The book is a MOBI 6 book, a KF8 book, or a joint
/// file that holds a MOBI 6 version followed by a KF8 version, after a <c>BOUNDARY</c> record.
/// </summary>
internal sealed class MobiBook
{
    /// <summary>The type and creator of the Palm database of a MOBI book, at offset 60.</summary>
    public static ReadOnlySpan<byte> Signature => "BOOKMOBI"u8;

    /// <summary>The maximum length of the decompressed text of a book.</summary>
    internal const int MaxTextLength = 64 * 1024 * 1024;

    private const int PalmDatabaseHeaderLength = 78;

    private readonly ReadOnlyMemory<byte> _data;
    private readonly (int Offset, int Length)[] _records;

    // The ranges of the records of the resources: the resources of a joint file are those of its MOBI 6 version, then those of its KF8 version
    private readonly (int Start, int End)[] _resourceRanges;

    private MobiBook(ReadOnlyMemory<byte> data, (int Offset, int Length)[] records, MobiHeader? mobi6, MobiHeader? kf8, (int Start, int End)[] resourceRanges)
    {
        _data = data;
        _records = records;
        Mobi6 = mobi6;
        Kf8 = kf8;
        _resourceRanges = resourceRanges;
    }

    /// <summary>Gets the headers of the MOBI 6 version of the book, or null for a KF8 book.</summary>
    public MobiHeader? Mobi6 { get; }

    /// <summary>Gets the headers of the KF8 version of the book, or null for a MOBI 6 book.</summary>
    public MobiHeader? Kf8 { get; }

    public string? Title => Kf8?.Title ?? Mobi6?.Title;

    public string? Author => Kf8?.Author ?? Mobi6?.Author;

    /// <summary>Gets the index of the cover image, as the resources are numbered (from 1), or null when the book has no cover.</summary>
    public int? CoverImageIndex => Kf8?.CoverImageIndex ?? Mobi6?.CoverImageIndex;

    /// <summary>Gets the number of records of the Palm database.</summary>
    public int RecordCount => _records.Length;

    /// <summary>Gets a value indicating whether a file starts like a MOBI book.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> prefix) => prefix.Length >= 68 && prefix[60..68].SequenceEqual(Signature);

    /// <summary>Reads the headers of a book.</summary>
    /// <exception cref="InvalidDataException">The data is not a MOBI book, or its text is protected by DRM or uses an unknown compression.</exception>
    public static MobiBook Read(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length < PalmDatabaseHeaderLength || !HasSignature(span))
            throw new InvalidDataException("The file is not a MOBI book");

        var records = ReadRecords(span);
        var name = ReadName(span);
        var first = MobiHeader.Read(span, records, recordIndex: 0, name);
        if (first.IsKf8)
            return new MobiBook(data, records, mobi6: null, first, [(first.FirstResourceIndex, records.Length)]);

        // A joint file: the KF8 version starts after a BOUNDARY record. A KF8 version that cannot be read leaves the MOBI 6 version
        MobiHeader? kf8 = null;
        if (first.Kf8RecordIndex is { } kf8Index && kf8Index > 1 && kf8Index < records.Length && span.Slice(records[kf8Index - 1].Offset, records[kf8Index - 1].Length).SequenceEqual("BOUNDARY"u8))
        {
            try
            {
                kf8 = MobiHeader.Read(span, records, kf8Index, name);
            }
            catch (InvalidDataException)
            {
            }

            if (kf8 is { IsKf8: true })
                return new MobiBook(data, records, first, kf8, [(first.FirstResourceIndex, kf8Index - 1), (kf8.FirstResourceIndex, records.Length)]);
        }

        return new MobiBook(data, records, first, kf8: null, [(first.FirstResourceIndex, records.Length)]);
    }

    /// <summary>Gets the decompressed text of a version of the book, in the encoding of the version.</summary>
    /// <exception cref="InvalidDataException">The compressed text is not valid, or the text is longer than <see cref="MaxTextLength"/>.</exception>
    public byte[] ReadText(MobiHeader header)
    {
        var output = new MobiBuffer(MaxTextLength);
        var huffCdic = header.Compression is MobiHeader.HuffCdicCompressionType ? HuffCdicDecoder.Create(GetRecords(header.HuffmanRecordIndex, header.HuffmanRecordCount)) : null;
        for (var i = 1; i <= header.TextRecordCount; i++)
        {
            var record = GetRecord(header.RecordIndex + i).Span;
            record = record[..GetTextLength(record, header.ExtraRecordDataFlags)];
            switch (header.Compression)
            {
                case MobiHeader.NoCompression:
                    output.Append(record);
                    break;
                case MobiHeader.PalmDocCompressionType:
                    PalmDocCompression.Decompress(record, output);
                    break;
                default:
                    huffCdic!.Decompress(record, output);
                    break;
            }
        }

        return output.ToArray();
    }

    /// <summary>Gets a record of the file.</summary>
    public ReadOnlyMemory<byte> GetRecord(int index)
    {
        var record = _records[index];
        return _data.Slice(record.Offset, record.Length);
    }

    /// <summary>
    /// Gets the data of a resource (an image, a font...), by its number in the text (from 1: the <c>recindex</c> attributes
    /// of a MOBI 6 version, the <c>kindle:embed</c> URLs of a KF8 version), or null when the book has no such record.
    /// </summary>
    public ReadOnlyMemory<byte>? GetImage(int index)
    {
        if (index < 1)
            return null;

        var remaining = (long)index - 1;
        foreach (var (start, end) in _resourceRanges)
        {
            if (start < 0 || end <= start)
                continue;

            if (remaining < end - start)
                return GetRecord(start + (int)remaining);

            remaining -= end - start;
        }

        return null;
    }

    /// <summary>Gets the length of the data of a text record, without the trailing entries the flags of the header describe.</summary>
    /// <exception cref="InvalidDataException">A trailing entry is longer than the record.</exception>
    internal static int GetTextLength(ReadOnlySpan<byte> record, ushort flags)
    {
        var length = record.Length;
        for (var remainingFlags = flags >> 1; remainingFlags is not 0; remainingFlags >>= 1)
        {
            if ((remainingFlags & 1) is 0)
                continue;

            // The size of an entry, which includes the bytes of the size, ends the entry: a variable-length integer
            // read backward, whose first byte has its high bit set
            var size = 0;
            var shift = 0;
            for (var position = length - 1; position >= 0 && shift < 28; position--)
            {
                var value = record[position];
                size |= (value & 0x7F) << shift;
                shift += 7;
                if ((value & 0x80) is not 0)
                    break;
            }

            if (size > length)
                throw new InvalidDataException("A trailing entry of a text record is longer than the record");

            length -= size;
        }

        // The bytes of a multibyte character that continues in the next record, and their count
        if ((flags & 1) is not 0 && length > 0)
        {
            var size = (record[length - 1] & 0x3) + 1;
            if (size > length)
                throw new InvalidDataException("A trailing entry of a text record is longer than the record");

            length -= size;
        }

        return length;
    }

    private static (int Offset, int Length)[] ReadRecords(ReadOnlySpan<byte> data)
    {
        var count = BinaryPrimitives.ReadUInt16BigEndian(data[76..]);
        var listEnd = PalmDatabaseHeaderLength + (count * 8);
        if (count is 0 || listEnd > data.Length)
            throw new InvalidDataException("The record list of the book is truncated");

        var result = new (int Offset, int Length)[count];
        var previous = (long)listEnd;
        var offsets = new long[count];
        for (var i = 0; i < count; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(data[(PalmDatabaseHeaderLength + (i * 8))..]);
            if (offset < previous || offset > data.Length)
                throw new InvalidDataException("The records of the book are not in order");

            offsets[i] = offset;
            previous = offset;
        }

        for (var i = 0; i < count; i++)
        {
            var end = i + 1 < count ? offsets[i + 1] : data.Length;
            result[i] = ((int)offsets[i], (int)(end - offsets[i]));
        }

        return result;
    }

    private static ReadOnlySpan<byte> ReadName(ReadOnlySpan<byte> data)
    {
        var name = data[..32];
        var end = name.IndexOf((byte)0);
        return end >= 0 ? name[..end] : name;
    }

    private List<ReadOnlyMemory<byte>> GetRecords(int index, int count)
    {
        var result = new List<ReadOnlyMemory<byte>>(count);
        for (var i = 0; i < count; i++)
        {
            result.Add(GetRecord(index + i));
        }

        return result;
    }
}
