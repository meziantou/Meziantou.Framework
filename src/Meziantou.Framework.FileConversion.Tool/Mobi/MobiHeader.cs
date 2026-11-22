using System.Buffers.Binary;

namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>
/// The headers of a version of a book (PalmDOC, MOBI 6 or KF8), read from its first record. The record indexes are
/// indexes of the records of the file: the indexes of a KF8 header are relative to its first record.
/// </summary>
internal sealed class MobiHeader
{
    internal const ushort NoCompression = 1;
    internal const ushort PalmDocCompressionType = 2;
    internal const ushort HuffCdicCompressionType = 17480; // "DH"
    internal const uint NoIndex = uint.MaxValue;

    private const int PalmDocHeaderLength = 16;

    // The offsets of the fields of the first record (PalmDOC header, then MOBI header)
    private const int CompressionOffset = 0;
    private const int TextRecordCountOffset = 8;
    private const int EncryptionOffset = 12;
    private const int MobiHeaderOffset = 16;
    private const int MobiHeaderLengthOffset = 20;
    private const int TextEncodingOffset = 28;
    private const int FileVersionOffset = 36;
    private const int FullNameOffsetOffset = 84;
    private const int FullNameLengthOffset = 88;
    private const int FirstResourceIndexOffset = 108;
    private const int HuffmanRecordOffsetOffset = 112;
    private const int HuffmanRecordCountOffset = 116;
    private const int ExthFlagsOffset = 128;
    private const int FdstIndexOffset = 192;
    private const int ExtraRecordDataFlagsOffset = 242;
    private const int FragmentIndexOffset = 248;
    private const int SkeletonIndexOffset = 252;

    // The types of the EXTH records
    private const uint ExthAuthor = 100;
    private const uint ExthKf8Boundary = 121;
    private const uint ExthCoverOffset = 201;
    private const uint ExthUpdatedTitle = 503;

    private MobiHeader()
    {
    }

    /// <summary>Gets the index of the first record of the version.</summary>
    public int RecordIndex { get; private init; }

    public ushort Compression { get; private init; }

    public int TextRecordCount { get; private init; }

    /// <summary>Gets the flags of the trailing entries of the text records.</summary>
    public ushort ExtraRecordDataFlags { get; private init; }

    public int HuffmanRecordIndex { get; private init; } = -1;

    public int HuffmanRecordCount { get; private init; }

    /// <summary>Gets the index of the first resource record (images, fonts...), or -1.</summary>
    public int FirstResourceIndex { get; private init; } = -1;

    /// <summary>Gets a value indicating whether the text is UTF-8. It is Windows-1252 otherwise.</summary>
    public bool IsUtf8 { get; private init; }

    /// <summary>Gets the version of the format: 8 for KF8, 6 or less for MOBI.</summary>
    public uint FileVersion { get; private init; }

    public bool IsKf8 => FileVersion >= 8;

    public string? Title { get; private init; }

    public string? Author { get; private init; }

    /// <summary>Gets the index of the cover image, as the resources are numbered (from 1), or null when the book has no cover.</summary>
    public int? CoverImageIndex { get; private init; }

    /// <summary>Gets the index of the first record of the KF8 version that follows the MOBI 6 version of a joint file, or null.</summary>
    public int? Kf8RecordIndex { get; private init; }

    /// <summary>Gets the index of the FDST record of a KF8 version, which splits its text into flows, or -1.</summary>
    public int FdstIndex { get; private init; } = -1;

    /// <summary>Gets the index of the first record of the skeleton index of a KF8 version, or -1.</summary>
    public int SkeletonIndex { get; private init; } = -1;

    /// <summary>Gets the index of the first record of the fragment index of a KF8 version, or -1.</summary>
    public int FragmentIndex { get; private init; } = -1;

    /// <summary>Gets the name of the encoding of the text, as the media type of the text gives it.</summary>
    public string EncodingName => IsUtf8 ? "utf-8" : "windows-1252";

    /// <summary>Reads the headers of the version whose first record is <paramref name="recordIndex"/>.</summary>
    /// <param name="data">The file.</param>
    /// <param name="records">The records of the file.</param>
    /// <param name="recordIndex">The index of the first record of the version.</param>
    /// <param name="name">The name of the Palm database, the title of a book that has no other title.</param>
    /// <exception cref="InvalidDataException">The headers are not valid, the text is protected by DRM, or uses an unknown compression.</exception>
    public static MobiHeader Read(ReadOnlySpan<byte> data, IReadOnlyList<(int Offset, int Length)> records, int recordIndex, ReadOnlySpan<byte> name)
    {
        var record0 = data.Slice(records[recordIndex].Offset, records[recordIndex].Length);
        if (record0.Length < PalmDocHeaderLength)
            throw new InvalidDataException("The header of the book is truncated");

        var compression = BinaryPrimitives.ReadUInt16BigEndian(record0[CompressionOffset..]);
        if (compression is not (NoCompression or PalmDocCompressionType or HuffCdicCompressionType))
            throw new InvalidDataException($"The text of the book uses an unknown compression ({compression.ToString(CultureInfo.InvariantCulture)})");

        if (BinaryPrimitives.ReadUInt16BigEndian(record0[EncryptionOffset..]) is not 0)
            throw new InvalidDataException("The book is protected by DRM");

        var textRecordCount = BinaryPrimitives.ReadUInt16BigEndian(record0[TextRecordCountOffset..]);
        if (textRecordCount >= records.Count - recordIndex)
            throw new InvalidDataException("The book has fewer records than its text");

        // A PalmDOC book has no MOBI header: its text is Windows-1252 and it has no image
        if (record0.Length < MobiHeaderOffset + 8 || !record0[MobiHeaderOffset..].StartsWith("MOBI"u8))
        {
            return new MobiHeader
            {
                RecordIndex = recordIndex,
                Compression = compression,
                TextRecordCount = textRecordCount,
                Title = NullIfEmpty(Encoding.Latin1.GetString(name)),
            };
        }

        var mobiHeaderLength = BinaryPrimitives.ReadUInt32BigEndian(record0[MobiHeaderLengthOffset..]);
        var mobiHeaderEnd = (int)Math.Min(record0.Length, MobiHeaderOffset + (long)mobiHeaderLength);
        var header = record0[..mobiHeaderEnd];

        var fileVersion = ReadUInt32(header, FileVersionOffset) ?? 0;
        var isUtf8 = ReadUInt32(header, TextEncodingOffset) is 65001;
        var encoding = isUtf8 ? Encoding.UTF8 : CodePagesEncodingProvider.Instance.GetEncoding(1252) ?? Encoding.Latin1;

        // The trailing entries of the text records exist from version 5 of the format
        ushort extraFlags = 0;
        if (fileVersion >= 5 && header.Length >= ExtraRecordDataFlagsOffset + 2)
        {
            extraFlags = BinaryPrimitives.ReadUInt16BigEndian(header[ExtraRecordDataFlagsOffset..]);
        }

        var huffmanRecordIndex = -1;
        var huffmanRecordCount = 0;
        if (compression is HuffCdicCompressionType)
        {
            huffmanRecordIndex = GetRecordIndex(ReadUInt32(header, HuffmanRecordOffsetOffset), records.Count, recordIndex);
            var count = ReadUInt32(header, HuffmanRecordCountOffset) ?? 0;
            if (huffmanRecordIndex < 0 || count is 0 || count > records.Count - huffmanRecordIndex)
                throw new InvalidDataException("The book has no valid HUFF/CDIC records");

            huffmanRecordCount = (int)count;
        }

        string? title = null;
        string? author = null;
        int? coverImageIndex = null;
        int? kf8RecordIndex = null;
        if (ReadUInt32(header, ExthFlagsOffset) is { } exthFlags && (exthFlags & 0x40) is not 0)
        {
            ReadExth(record0[mobiHeaderEnd..], encoding, ref title, ref author, ref coverImageIndex, ref kf8RecordIndex);
        }

        if (title is null)
        {
            var fullNameOffset = ReadUInt32(header, FullNameOffsetOffset) ?? 0;
            var fullNameLength = ReadUInt32(header, FullNameLengthOffset) ?? 0;
            if (fullNameOffset > 0 && fullNameOffset <= record0.Length && fullNameLength <= record0.Length - fullNameOffset)
            {
                title = NullIfEmpty(encoding.GetString(record0.Slice((int)fullNameOffset, (int)fullNameLength)));
            }
        }

        var isKf8 = fileVersion >= 8;
        return new MobiHeader
        {
            RecordIndex = recordIndex,
            Compression = compression,
            TextRecordCount = textRecordCount,
            ExtraRecordDataFlags = extraFlags,
            HuffmanRecordIndex = huffmanRecordIndex,
            HuffmanRecordCount = huffmanRecordCount,
            FirstResourceIndex = GetRecordIndex(ReadUInt32(header, FirstResourceIndexOffset), records.Count, recordIndex),
            IsUtf8 = isUtf8,
            FileVersion = fileVersion,
            Title = title ?? NullIfEmpty(encoding.GetString(name)),
            Author = author,
            CoverImageIndex = coverImageIndex,
            Kf8RecordIndex = isKf8 ? null : kf8RecordIndex,
            FdstIndex = isKf8 ? GetRecordIndex(ReadUInt32(header, FdstIndexOffset), records.Count, recordIndex) : -1,
            SkeletonIndex = isKf8 ? GetRecordIndex(ReadUInt32(header, SkeletonIndexOffset), records.Count, recordIndex) : -1,
            FragmentIndex = isKf8 ? GetRecordIndex(ReadUInt32(header, FragmentIndexOffset), records.Count, recordIndex) : -1,
        };
    }

    /// <summary>Gets the index of a record of the file from an index relative to the first record of the version, or -1 when there is no such record.</summary>
    private static int GetRecordIndex(uint? value, int recordCount, int recordIndex)
    {
        if (value is not { } relative || relative is 0 or NoIndex)
            return -1;

        var index = recordIndex + (long)relative;
        return index < recordCount ? (int)index : -1;
    }

    private static void ReadExth(ReadOnlySpan<byte> data, Encoding encoding, ref string? title, ref string? author, ref int? coverImageIndex, ref int? kf8RecordIndex)
    {
        if (data.Length < 12 || !data.StartsWith("EXTH"u8))
            return;

        var length = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var count = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        data = data[..(int)Math.Min(data.Length, length)];
        var position = 12;
        for (var i = 0u; i < count && position <= data.Length - 8; i++)
        {
            var type = BinaryPrimitives.ReadUInt32BigEndian(data[position..]);
            var recordLength = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 4)..]);
            if (recordLength < 8 || recordLength > data.Length - position)
                return;

            var value = data.Slice(position + 8, (int)recordLength - 8);
            var number = value.Length >= 4 ? BinaryPrimitives.ReadUInt32BigEndian(value) : NoIndex;
            switch (type)
            {
                case ExthAuthor:
                    author ??= NullIfEmpty(encoding.GetString(value));
                    break;
                case ExthUpdatedTitle:
                    title ??= NullIfEmpty(encoding.GetString(value));
                    break;
                case ExthCoverOffset when number < int.MaxValue:
                    coverImageIndex ??= (int)number + 1;
                    break;
                case ExthKf8Boundary when number < int.MaxValue:
                    kf8RecordIndex ??= (int)number;
                    break;
            }

            position += (int)recordLength;
        }
    }

    private static uint? ReadUInt32(ReadOnlySpan<byte> header, int offset) => header.Length >= offset + 4 ? BinaryPrimitives.ReadUInt32BigEndian(header[offset..]) : null;

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim().Trim('\0').Trim();
        return trimmed.Length is 0 ? null : trimmed;
    }
}
