using System.Collections;
using static Meziantou.Framework.UnicodeDataFormat;

namespace Meziantou.Framework;

/// <summary>Reads the Unicode Character Database out of the generated tables, in place.</summary>
/// <remarks>
/// The tables are spans over static data: nothing is copied or parsed when the program starts, and a lookup only
/// finds the property record of a character. Every other field of <see cref="UnicodeCharacterInfo"/> is decoded
/// when it is read. Names, decomposition mappings and strings are materialized lazily and cached, so a program
/// that never asks for them never pays for them. See <see cref="UnicodeDataFormat"/> for the layout.
/// </remarks>
internal static partial class UnicodeCharacterInfos
{
    private const int MaxCodePoint = 0x10FFFF;

    private static string?[]? s_names;
    private static string?[]? s_decompositions;
    private static string?[]? s_strings;
    private static int s_count = -1;

    public static IReadOnlyCollection<UnicodeCharacterInfo> AllCharacters { get; } = new CharacterCollection();

    public static bool TryGetInfo(Rune rune, out UnicodeCharacterInfo info)
    {
        var propertyIndex = GetPropertyIndex(rune.Value);
        if (propertyIndex == 0)
        {
            info = default;
            return false;
        }

        info = new UnicodeCharacterInfo(rune, propertyIndex, name: null);
        return true;
    }

    /// <summary>Gets the index of the property record of a code point, or 0 when the code point is not a character.</summary>
    private static int GetPropertyIndex(int codePoint)
    {
        var block3 = GetBlock3(GetBlock2(codePoint), codePoint);
        return ReadUInt16(PropertyIndexStage3, ((block3 << PropertyIndexShift2) + (codePoint & ((1 << PropertyIndexShift2) - 1))) * 2);
    }

    private static int GetBlock2(int codePoint) => ReadUInt16(PropertyIndexStage1, (codePoint >> PropertyIndexShift1) * 2);

    private static int GetBlock3(int block2, int codePoint)
    {
        const int Bits2 = PropertyIndexShift1 - PropertyIndexShift2;
        return ReadUInt16(PropertyIndexStage2, ((block2 << Bits2) + ((codePoint >> PropertyIndexShift2) & ((1 << Bits2) - 1))) * 2);
    }

    /// <summary>Finds the first character after <paramref name="codePoint"/>.</summary>
    /// <returns>The code point of the character, or -1 when there is none.</returns>
    private static int GetNextCharacter(int codePoint, out int propertyIndex)
    {
        const int Block2Mask = (1 << PropertyIndexShift1) - 1;
        const int Block3Mask = (1 << PropertyIndexShift2) - 1;
        while (codePoint < MaxCodePoint)
        {
            codePoint++;

            // Most code points are not characters, and they come in whole blocks of the table.
            var block2 = GetBlock2(codePoint);
            if (block2 == PropertyIndexEmptyBlock2)
            {
                codePoint |= Block2Mask;
                continue;
            }

            var block3 = GetBlock3(block2, codePoint);
            if (block3 == PropertyIndexEmptyBlock3)
            {
                codePoint |= Block3Mask;
                continue;
            }

            propertyIndex = ReadUInt16(PropertyIndexStage3, ((block3 << PropertyIndexShift2) + (codePoint & Block3Mask)) * 2);
            if (propertyIndex != 0)
                return codePoint;
        }

        propertyIndex = 0;
        return -1;
    }

    /// <summary>Gets a one-byte field of a property record.</summary>
    public static byte GetPropertyField(int propertyIndex, int field) => Properties[(propertyIndex * PropertyRecordSize) + field];

    /// <summary>Gets a simple case mapping, or <see langword="null"/> when the character does not have one.</summary>
    public static Rune? GetCaseMapping(int codePoint, int propertyIndex, byte flag, int field)
    {
        if ((GetPropertyField(propertyIndex, FlagsField) & flag) == 0)
            return null;

        if (Rune.TryCreate(codePoint + ReadInt24(Properties, (propertyIndex * PropertyRecordSize) + field), out var result))
            return result;

        return null;
    }

    public static string GetName(int codePoint)
    {
        var range = FindFloorRecord(RangeNames, RangeNameRecordSize, codePoint);
        if (range >= 0 && codePoint <= ReadUInt24(RangeNames, range + 3))
            return GetString(ReadUInt16(RangeNames, range + 6));

        var bucket = FindFloorRecord(NameBuckets, NameBucketRecordSize, codePoint);
        if (bucket >= 0)
        {
            var name = GetNameInBucket(bucket, codePoint);
            if (name is not null)
                return name;
        }

        return string.Empty;
    }

    /// <summary>Decodes the name of <paramref name="codePoint"/> by replaying the front-coding of its bucket.</summary>
    private static string? GetNameInBucket(int bucketOffset, int codePoint)
    {
        var entries = NameEntries;
        var firstEntryPosition = ReadUInt24(NameBuckets, bucketOffset + 3);
        var firstOrdinal = bucketOffset / NameBucketRecordSize * NameBucketSize;
        var count = Math.Min(NameBucketSize, NamedCharacterCount - firstOrdinal);

        // Find the entry first: when its name is cached, nothing has to be decoded.
        var position = firstEntryPosition;
        var current = ReadUInt24(NameBuckets, bucketOffset);
        var index = 0;
        while (true)
        {
            current += ReadVarInt(entries, ref position);
            if (current == codePoint)
                break;

            index++;
            if (current > codePoint || index == count)
                return null;

            position += 2;
        }

        // Caching keeps a code point looked up more than once from decoding and allocating its name again.
        var names = s_names ?? CreateCache(ref s_names, NamedCharacterCount);
        var name = names[firstOrdinal + index];
        if (name is not null)
            return name;

        Span<byte> buffer = stackalloc byte[MaxSerializedStringLength];
        position = firstEntryPosition;
        var textPosition = ReadUInt24(NameBuckets, bucketOffset + 6);
        var length = 0;
        for (var i = 0; i <= index; i++)
        {
            ReadVarInt(entries, ref position);
            length = DecodeFrontCodedName(ref position, ref textPosition, buffer);
        }

        name = Encoding.UTF8.GetString(buffer[..length]);
        names[firstOrdinal + index] = name;
        return name;
    }

    /// <summary>Applies the next name entry to <paramref name="buffer"/>, which holds the previous name, and returns the length of the name.</summary>
    private static int DecodeFrontCodedName(ref int entryPosition, ref int textPosition, Span<byte> buffer)
    {
        var entries = NameEntries;
        var sharedPrefixLength = entries[entryPosition];
        var suffixLength = entries[entryPosition + 1];
        entryPosition += 2;
        NameText.Slice(textPosition, suffixLength).CopyTo(buffer[sharedPrefixLength..]);
        textPosition += suffixLength;
        return sharedPrefixLength + suffixLength;
    }

    public static string? GetDecompositionMapping(int codePoint)
    {
        var record = UnicodeTableReader.FindRecord(Decompositions, DecompositionRecordSize, codePoint);
        if (record < 0)
            return null;

        var decompositions = s_decompositions ?? CreateCache(ref s_decompositions, Decompositions.Length / DecompositionRecordSize);
        var index = record / DecompositionRecordSize;
        var mapping = decompositions[index];
        if (mapping is not null)
            return mapping;

        var data = DecompositionMappings;
        var position = ReadUInt24(Decompositions, record + 3);
        var tag = data[position];
        var mappingLength = data[position + 1];
        position += 2;

        var builder = new StringBuilder();
        if (tag != 0)
        {
            builder.Append(GetString(tag - 1));
        }

        for (var i = 0; i < mappingLength; i++)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(CultureInfo.InvariantCulture, $"{ReadVarInt(data, ref position):X4}");
        }

        mapping = builder.ToString();
        decompositions[index] = mapping;
        return mapping;
    }

    public static string? GetNumericValue(int codePoint) => GetStringValue(NumericValues, codePoint);

    public static string? GetUnicode1Name(int codePoint) => GetStringValue(Unicode1Names, codePoint);

    public static string? GetIsoComment(int codePoint) => GetStringValue(IsoComments, codePoint);

    private static string? GetStringValue(ReadOnlySpan<byte> records, int codePoint)
    {
        var record = UnicodeTableReader.FindRecord(records, StringValueRecordSize, codePoint);
        return record < 0 ? null : GetString(ReadUInt16(records, record + 3));
    }

    private static string GetString(int index)
    {
        var strings = s_strings ?? CreateCache(ref s_strings, (StringOffsets.Length / StringOffsetSize) - 1);
        var value = strings[index];
        if (value is not null)
            return value;

        var start = ReadUInt24(StringOffsets, index * StringOffsetSize);
        var end = ReadUInt24(StringOffsets, (index + 1) * StringOffsetSize);
        value = Encoding.UTF8.GetString(Strings[start..end]);
        strings[index] = value;
        return value;
    }

    /// <summary>Finds the last record whose three-byte key is not greater than <paramref name="key"/>, in records sorted by that key.</summary>
    /// <returns>The offset of the record, or -1.</returns>
    private static int FindFloorRecord(ReadOnlySpan<byte> records, int recordSize, int key)
    {
        var low = 0;
        var high = (records.Length / recordSize) - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) / 2);
            if (ReadUInt24(records, middle * recordSize) <= key)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return high < 0 ? -1 : high * recordSize;
    }

    private static string?[] CreateCache(ref string?[]? location, int length)
    {
        // Two threads may build the array at once; the loser's entries are simply recomputed.
        return Interlocked.CompareExchange(ref location, new string?[length], comparand: null) ?? location;
    }

    private sealed class CharacterCollection : IReadOnlyCollection<UnicodeCharacterInfo>
    {
        public int Count
        {
            get
            {
                // Counted from the table rather than taken from the generator, so that it describes what is
                // enumerated. It is only computed once, and only for the programs that ask.
                var count = s_count;
                if (count < 0)
                {
                    count = 0;
                    var codePoint = -1;
                    while ((codePoint = GetNextCharacter(codePoint, out _)) >= 0)
                    {
                        count++;
                    }

                    s_count = count;
                }

                return count;
            }
        }

        public IEnumerator<UnicodeCharacterInfo> GetEnumerator() => new CharacterEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Enumerates the characters in code point order, decoding the names as it goes.</summary>
    /// <remarks>
    /// The names are read forward, so each one costs a single front-coding step instead of a search and the replay
    /// of its bucket. They are not added to the cache: a program that enumerates every character once does not
    /// keep tens of thousands of names alive for the rest of its life.
    /// </remarks>
    private sealed class CharacterEnumerator : IEnumerator<UnicodeCharacterInfo>
    {
        private readonly byte[] _nameBuffer = new byte[MaxSerializedStringLength];
        private int _codePoint;
        private int _nameOrdinal;
        private int _nextNamedCodePoint;
        private int _nameEntryPosition;
        private int _nameTextPosition;
        private int _rangeOffset;
        private UnicodeCharacterInfo _current;

        public CharacterEnumerator()
        {
            Reset();
        }

        public UnicodeCharacterInfo Current => _current;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var codePoint = GetNextCharacter(_codePoint, out var propertyIndex);
            if (codePoint < 0)
            {
                _codePoint = MaxCodePoint;
                _current = default;
                return false;
            }

            _codePoint = codePoint;
            _current = new UnicodeCharacterInfo(new Rune(codePoint), propertyIndex, GetNextName(codePoint));
            return true;
        }

        public void Reset()
        {
            _codePoint = -1;
            _nameOrdinal = 0;
            _nameEntryPosition = 0;
            _nameTextPosition = 0;
            _rangeOffset = 0;
            _current = default;
            _nextNamedCodePoint = NamedCharacterCount > 0 ? ReadUInt24(NameBuckets, 0) + ReadVarInt(NameEntries, ref _nameEntryPosition) : int.MaxValue;
        }

        public void Dispose()
        {
        }

        private string GetNextName(int codePoint)
        {
            if (codePoint == _nextNamedCodePoint)
            {
                var length = DecodeFrontCodedName(ref _nameEntryPosition, ref _nameTextPosition, _nameBuffer);
                var name = s_names?[_nameOrdinal] ?? Encoding.UTF8.GetString(_nameBuffer, 0, length);
                _nameOrdinal++;
                if (_nameOrdinal == NamedCharacterCount)
                {
                    _nextNamedCodePoint = int.MaxValue;
                }
                else
                {
                    if (_nameOrdinal % NameBucketSize == 0)
                    {
                        _nextNamedCodePoint = ReadUInt24(NameBuckets, _nameOrdinal / NameBucketSize * NameBucketRecordSize);
                    }

                    _nextNamedCodePoint += ReadVarInt(NameEntries, ref _nameEntryPosition);
                }

                return name;
            }

            var rangeNames = RangeNames;
            while (_rangeOffset < rangeNames.Length && ReadUInt24(rangeNames, _rangeOffset + 3) < codePoint)
            {
                _rangeOffset += RangeNameRecordSize;
            }

            if (_rangeOffset < rangeNames.Length && ReadUInt24(rangeNames, _rangeOffset) <= codePoint)
                return GetString(ReadUInt16(rangeNames, _rangeOffset + 6));

            return string.Empty;
        }
    }
}
