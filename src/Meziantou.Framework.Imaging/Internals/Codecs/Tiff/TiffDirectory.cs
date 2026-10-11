using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One image file directory (IFD) of a TIFF or BigTIFF, read through a <see cref="RandomAccessSource"/>: the entries, the
/// offset of the next directory, and bounds-checked access to the values, inline or stored elsewhere in the file.
/// </summary>
/// <remarks>
/// <para>
/// Every count and offset is validated against the length of the input before anything is read or allocated: a directory
/// whose entries do not fit, a value array that runs past the end of the file, or an entry count larger than
/// <see cref="MaxEntries"/> is reported as <see cref="InvalidImageContentException"/>. Values are read on demand, so an
/// unread tag never costs anything.
/// </para>
/// <para>The chain of directories is walked by <see cref="TiffDirectoryChain"/>, which rejects cycles and bounds the page count.</para>
/// </remarks>
internal sealed class TiffDirectory
{
    /// <summary>
    /// The largest number of entries one directory may declare. A classic IFD cannot declare more (the count is a 16-bit
    /// field); the same bound is applied to BigTIFF, whose 64-bit count would otherwise ask for an arbitrarily large
    /// allocation from a small file.
    /// </summary>
    public const int MaxEntries = ushort.MaxValue;

    private readonly RandomAccessSource _source;
    private readonly TiffHeader _header;
    private readonly TiffField[] _fields;

    private TiffDirectory(RandomAccessSource source, TiffHeader header, ulong offset, TiffField[] fields, ulong nextOffset, long length)
    {
        _source = source;
        _header = header;
        _fields = fields;
        Offset = offset;
        NextDirectoryOffset = nextOffset;
        Length = length;
    }

    /// <summary>Gets the file header (byte order and offset size).</summary>
    public TiffHeader Header => _header;

    /// <summary>Gets the input the values are read from.</summary>
    public RandomAccessSource Source => _source;

    /// <summary>Gets the offset of this directory in the file.</summary>
    public ulong Offset { get; }

    /// <summary>Gets the number of bytes of the directory itself (count, entries and the next-directory pointer).</summary>
    public long Length { get; }

    /// <summary>Gets the offset of the next directory, or 0 for the last one.</summary>
    public ulong NextDirectoryOffset { get; }

    /// <summary>Gets the entries, in file order.</summary>
    public ReadOnlySpan<TiffField> Fields => _fields;

    /// <summary>Reads the directory at an offset.</summary>
    /// <param name="source">The input.</param>
    /// <param name="header">The file header.</param>
    /// <param name="offset">The offset of the directory.</param>
    /// <returns>The directory.</returns>
    /// <exception cref="InvalidImageContentException">The offset or the entry count is invalid, or the directory does not fit in the input.</exception>
    public static TiffDirectory Read(RandomAccessSource source, TiffHeader header, ulong offset)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (offset > long.MaxValue)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF directory offset {offset} does not fit in the input."));

        var start = (long)offset;
        var countLength = header.IsBigTiff ? 8 : 2;
        source.EnsureRange(start, countLength);
        Span<byte> countBuffer = stackalloc byte[8];
        source.Read(start, countBuffer[..countLength]);
        var count = header.IsBigTiff ? header.ReadUInt64(countBuffer) : header.ReadUInt16(countBuffer);
        if (count > MaxEntries)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF directory at offset {offset} declares {count} entries; at most {MaxEntries} are allowed."));

        var entryCount = (int)count;
        var entriesLength = (long)entryCount * header.EntryLength;
        var totalLength = countLength + entriesLength + header.OffsetSize;
        source.EnsureRange(start, totalLength);

        var fields = new TiffField[entryCount];
        if (entryCount > 0)
        {
            using var buffer = source.ReadToBuffer(start + countLength, entriesLength);
            var span = buffer.Span;
            Span<byte> raw = stackalloc byte[8];
            for (var i = 0; i < entryCount; i++)
            {
                var entry = span.Slice(i * header.EntryLength, header.EntryLength);
                var tag = header.ReadUInt16(entry);
                var type = (TiffFieldType)header.ReadUInt16(entry[2..]);
                var valueCount = header.IsBigTiff ? header.ReadUInt64(entry[4..]) : header.ReadUInt32(entry[4..]);
                raw.Clear();
                entry[(header.IsBigTiff ? 12 : 8)..].CopyTo(raw);
                fields[i] = new TiffField(tag, type, valueCount, BinaryPrimitives.ReadUInt64LittleEndian(raw));
            }
        }

        Span<byte> nextBuffer = stackalloc byte[8];
        source.Read(start + countLength + entriesLength, nextBuffer[..header.OffsetSize]);
        var next = header.ReadOffset(nextBuffer);
        return new TiffDirectory(source, header, offset, fields, next, totalLength);
    }

    /// <summary>Finds an entry by tag.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The entry, or <see langword="null"/> when the directory has no such tag.</returns>
    public TiffField? Find(ushort tag)
    {
        foreach (var field in _fields)
        {
            if (field.Tag == tag)
                return field;
        }

        return null;
    }

    /// <summary>Gets the total length of the values of an entry, in bytes.</summary>
    /// <param name="field">The entry.</param>
    /// <returns>The length.</returns>
    /// <exception cref="InvalidImageContentException">The type is unknown or the length overflows.</exception>
    public static long GetValueLength(in TiffField field)
    {
        var size = TiffFieldTypes.GetSize(field.Type);
        if (size == 0)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF tag {field.Tag} uses the unknown field type {(ushort)field.Type}."));

        if (field.Count > (ulong)(long.MaxValue / size))
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF tag {field.Tag} declares {field.Count} values of {size} bytes, which overflows."));

        return (long)field.Count * size;
    }

    /// <summary>Reads the values of an entry into a buffer.</summary>
    /// <param name="field">The entry.</param>
    /// <returns>The raw value bytes, in file byte order. The caller disposes the buffer.</returns>
    /// <exception cref="InvalidImageContentException">The values do not fit in the input.</exception>
    public PooledBuffer ReadValues(in TiffField field)
    {
        var length = GetValueLength(field);
        if (length <= _header.OffsetSize)
        {
            var inline = _source.Scope.Rent((int)length, AllocationKind.DecoderState, clear: false);
            try
            {
                Span<byte> raw = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64LittleEndian(raw, field.RawValue);
                raw[..(int)length].CopyTo(inline.Span);
                return inline;
            }
            catch
            {
                inline.Dispose();
                throw;
            }
        }

        var offset = GetValueOffset(field);
        return _source.ReadToBuffer(offset, length);
    }

    /// <summary>Gets the offset of the out-of-line values of an entry.</summary>
    /// <param name="field">The entry.</param>
    /// <returns>The offset.</returns>
    public long GetValueOffset(in TiffField field)
    {
        Span<byte> raw = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(raw, field.RawValue);
        var offset = _header.ReadOffset(raw);
        if (offset > long.MaxValue)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF tag {field.Tag} stores its values at offset {offset}, outside the input."));

        return (long)offset;
    }

    /// <summary>Reads a single unsigned integer value.</summary>
    /// <param name="tag">The tag.</param>
    /// <param name="value">The value when the tag exists and holds exactly one unsigned integer.</param>
    /// <returns><see langword="true"/> when the value was read.</returns>
    /// <exception cref="InvalidImageContentException">The tag exists but does not hold exactly one unsigned integer.</exception>
    public bool TryGetUnsigned(ushort tag, out ulong value)
    {
        value = 0;
        if (Find(tag) is not { } field)
            return false;

        if (field.Count != 1)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF tag {tag} declares {field.Count} values; exactly one is expected."));

        if (!TiffFieldTypes.IsUnsignedInteger(field.Type))
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF tag {tag} uses {TiffFieldTypes.GetName(field.Type)}; an unsigned integer type is expected."));

        using var buffer = ReadValues(field);
        value = ReadUnsignedAt(buffer.Span, field.Type, 0);
        return true;
    }

    /// <summary>Reads an array of unsigned integers whose length is already known.</summary>
    /// <param name="field">The entry.</param>
    /// <param name="expectedCount">The required number of values.</param>
    /// <returns>The values.</returns>
    /// <exception cref="InvalidImageContentException">The count differs, the type is not an unsigned integer, or the values do not fit in the input.</exception>
    public ulong[] ReadUnsignedArray(in TiffField field, long expectedCount)
    {
        if (field.Count != (ulong)expectedCount)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF tag {field.Tag} declares {field.Count} values; {expectedCount} are expected."));

        if (!TiffFieldTypes.IsUnsignedInteger(field.Type))
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF tag {field.Tag} uses {TiffFieldTypes.GetName(field.Type)}; an unsigned integer type is expected."));

        var result = new ulong[expectedCount];
        using var buffer = ReadValues(field);
        var span = buffer.Span;
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ReadUnsignedAt(span, field.Type, i);
        }

        return result;
    }

    /// <summary>Reads an array of unsigned integers of a bounded, unknown length.</summary>
    /// <param name="tag">The tag.</param>
    /// <param name="maxCount">The largest accepted number of values.</param>
    /// <returns>The values, or <see langword="null"/> when the tag is absent.</returns>
    /// <exception cref="InvalidImageContentException">The count exceeds <paramref name="maxCount"/>, or the type is not an unsigned integer.</exception>
    public ulong[]? TryReadUnsignedArray(ushort tag, int maxCount)
    {
        if (Find(tag) is not { } field)
            return null;

        if (field.Count > (ulong)maxCount)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF tag {tag} declares {field.Count} values; at most {maxCount} are supported."));

        return ReadUnsignedArray(field, (long)field.Count);
    }

    /// <summary>Reads a single RATIONAL value as a positive number of units.</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The value, or <see langword="null"/> when the tag is absent, is not a usable RATIONAL, or is not finite and positive.</returns>
    public double? TryReadPositiveRational(ushort tag)
    {
        if (Find(tag) is not { } field || field.Type != TiffFieldType.Rational || field.Count != 1)
            return null;

        using var buffer = ReadValues(field);
        var numerator = _header.ReadUInt32(buffer.Span);
        var denominator = _header.ReadUInt32(buffer.Span[4..]);
        if (numerator == 0 || denominator == 0)
            return null;

        var value = (double)numerator / denominator;
        return double.IsFinite(value) && value > 0 ? value : null;
    }

    /// <summary>Reads the bytes of a BYTE, ASCII or UNDEFINED entry (an embedded profile).</summary>
    /// <param name="tag">The tag.</param>
    /// <param name="maxLength">The largest accepted length.</param>
    /// <returns>The bytes, or <see langword="null"/> when the tag is absent, has another type, is empty or is longer than <paramref name="maxLength"/>.</returns>
    public byte[]? TryReadBytes(ushort tag, long maxLength)
    {
        if (Find(tag) is not { } field)
            return null;

        if (field.Type is not (TiffFieldType.Byte or TiffFieldType.Undefined or TiffFieldType.Ascii))
            return null;

        var length = GetValueLength(field);
        if (length == 0 || length > maxLength)
            return null;

        using var buffer = ReadValues(field);
        return buffer.Span.ToArray();
    }

    /// <summary>Reads one unsigned value of an array of raw value bytes.</summary>
    /// <param name="values">The raw bytes, in file byte order.</param>
    /// <param name="type">The field type.</param>
    /// <param name="index">The index of the value.</param>
    /// <returns>The value.</returns>
    public ulong ReadUnsignedAt(ReadOnlySpan<byte> values, TiffFieldType type, int index) => type switch
    {
        TiffFieldType.Byte => values[index],
        TiffFieldType.Short => _header.ReadUInt16(values[(index * 2)..]),
        TiffFieldType.Long or TiffFieldType.Ifd => _header.ReadUInt32(values[(index * 4)..]),
        _ => _header.ReadUInt64(values[(index * 8)..]),
    };
}
