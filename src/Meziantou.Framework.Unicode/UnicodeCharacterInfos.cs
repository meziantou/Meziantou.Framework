using System.Collections;
using System.Runtime.CompilerServices;
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

    // The decoded names, by bucket: a program that reads a few names does not allocate a slot for every one.
    private static readonly NameBucket?[] NameCache = new NameBucket?[(NamedCharacterCount + NameBucketSize - 1) / NameBucketSize];

    // The names of the ranges of NameRanges: one slot for a range whose characters share their name, one per code point
    // for a range whose names end with the code point.
    private static readonly string?[]?[] RangeNameCache = new string?[]?[NameRangeCount];
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
        var ranges = NameRanges;
        var range = FindFloorRecord(ranges, NameRangeRecordSize, codePoint);
        if (range >= 0 && codePoint <= ReadUInt24(ranges, range + 3))
            return GetRangeName(range, codePoint, addToCache: true);

        var bucket = FindFloorRecord(NameBuckets, NameBucketRecordSize, codePoint);
        if (bucket >= 0)
        {
            var name = GetNameInBucket(bucket, codePoint);
            if (name is not null)
                return name;
        }

        return string.Empty;
    }

    /// <summary>Gets the name of a code point of a record of <c>NameRanges</c>.</summary>
    /// <param name="rangeOffset">The offset of the record.</param>
    /// <param name="codePoint">The code point, in the range.</param>
    /// <param name="addToCache">
    /// Whether to cache a name that ends with the code point. A name that the whole range shares is always cached: it is a
    /// single string.
    /// </param>
    private static string GetRangeName(int rangeOffset, int codePoint, bool addToCache)
    {
        var ranges = NameRanges;
        var start = ReadUInt24(ranges, rangeOffset);
        var text = ReadUInt16(ranges, rangeOffset + 6);
        var derived = (text & DerivedNameFlag) != 0;
        var rangeIndex = rangeOffset / NameRangeRecordSize;
        var names = RangeNameCache[rangeIndex];
        if (names is null)
        {
            if (derived && !addToCache)
                return CreateRangeName(text, codePoint);

            names = CreateCache(ref RangeNameCache[rangeIndex], derived ? ReadUInt24(ranges, rangeOffset + 3) - start + 1 : 1);
        }

        var index = derived ? codePoint - start : 0;
        var name = names[index];
        if (name is not null)
            return name;

        name = CreateRangeName(text, codePoint);
        if (!derived || addToCache)
        {
            names[index] = name;
        }

        return name;
    }

    private static string CreateRangeName(int text, int codePoint)
    {
        var rangeText = NameRangeText;
        var offset = text & ~DerivedNameFlag;
        var prefix = rangeText.Slice(offset + 1, rangeText[offset]);
        if ((text & DerivedNameFlag) == 0)
            return Encoding.ASCII.GetString(prefix);

        // The rule NR2 of UAX #44: the prefix, a hyphen, then the code point in hexadecimal, such as "CJK COMPATIBILITY IDEOGRAPH-F900".
        Span<char> buffer = stackalloc char[MaxSerializedStringLength];
        var length = Encoding.ASCII.GetChars(prefix, buffer);
        buffer[length] = '-';
        length++;
        codePoint.TryFormat(buffer[length..], out var written, "X4", CultureInfo.InvariantCulture);
        return new string(buffer[..(length + written)]);
    }

    /// <summary>Gets the name of <paramref name="codePoint"/> from its bucket, decoding it by replaying the front-coding of the bucket the first time.</summary>
    private static string? GetNameInBucket(int bucketOffset, int codePoint)
    {
        var bucket = bucketOffset / NameBucketRecordSize;
        var names = NameCache[bucket];
        if (names is null)
            return CreateNameBucket(bucketOffset, codePoint);

        var index = names.CodePoints.AsSpan().BinarySearch(codePoint);
        if (index < 0)
            return null;

        var name = names.Names[index];
        if (name is null)
        {
            // Caching keeps a code point looked up more than once from decoding and allocating its name again.
            name = DecodeNameInBucket(bucketOffset, index);
            names.Names[index] = name;
        }

        return name;
    }

    /// <summary>Reads the code points of the names of a bucket, so that a name is found without replaying the bucket, and decodes the name of a code point.</summary>
    /// <returns>The name of <paramref name="codePoint"/>, or <see langword="null"/> when it is not in the bucket.</returns>
    private static string? CreateNameBucket(int bucketOffset, int codePoint)
    {
        var entries = NameEntries;
        var buckets = NameBuckets;
        var position = ReadUInt24(buckets, bucketOffset + 3);
        var current = ReadUInt24(buckets, bucketOffset);
        var bucket = bucketOffset / NameBucketRecordSize;
        var codePoints = new int[Math.Min(NameBucketSize, NamedCharacterCount - (bucket * NameBucketSize))];
        Span<int> words = stackalloc int[MaxNameWordCount];
        var wordCount = 0;
        var names = new string?[codePoints.Length];
        string? name = null;
        for (var index = 0; index < codePoints.Length; index++)
        {
            current += ReadNameEntry(entries, ref position, words, ref wordCount);
            codePoints[index] = current;
            if (current == codePoint)
            {
                name = DecodeName(words[..wordCount]);
                names[index] = name;
            }
        }

        // Two threads may build the bucket at once; the loser's names are simply decoded again.
        Interlocked.CompareExchange(ref NameCache[bucket], new NameBucket(codePoints, names), comparand: null);
        return name;
    }

    /// <summary>Decodes a name of a bucket by replaying the front-coding of the bucket up to it.</summary>
    private static string DecodeNameInBucket(int bucketOffset, int index)
    {
        var entries = NameEntries;
        var position = ReadUInt24(NameBuckets, bucketOffset + 3);
        Span<int> words = stackalloc int[MaxNameWordCount];
        var wordCount = 0;
        for (var i = 0; i <= index; i++)
        {
            ReadNameEntry(entries, ref position, words, ref wordCount);
        }

        return DecodeName(words[..wordCount]);
    }

    private static string DecodeName(ReadOnlySpan<int> words)
    {
        Span<byte> buffer = stackalloc byte[MaxSerializedStringLength];
        var length = WriteName(words, NameEntries, NameWordOffsets, NameWordText, buffer);
        return Encoding.ASCII.GetString(buffer[..length]);
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
        private readonly int[] _words = new int[MaxNameWordCount];
        private int _codePoint;
        private int _nameOrdinal;
        private int _nextNamedCodePoint;
        private int _nameEntryPosition;
        private int _wordCount;
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
            _nameOrdinal = -1;
            _nameEntryPosition = 0;
            _wordCount = 0;
            _rangeOffset = 0;
            _current = default;
            ReadNextNameEntry();
        }

        public void Dispose()
        {
        }

        /// <summary>Reads the entry of the next name of <c>NameEntries</c>: its code point and its words.</summary>
        // The enumeration of the characters usually runs once, faster than the tiered JIT compiler promotes the methods it
        // calls for every character: the decoding of the names is compiled optimized right away.
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private void ReadNextNameEntry()
        {
            _nameOrdinal++;
            if (_nameOrdinal >= NamedCharacterCount)
            {
                _nextNamedCodePoint = int.MaxValue;
                return;
            }

            if (_nameOrdinal % NameBucketSize == 0)
            {
                _nextNamedCodePoint = ReadUInt24(NameBuckets, _nameOrdinal / NameBucketSize * NameBucketRecordSize);
                _wordCount = 0;
            }

            _nextNamedCodePoint += ReadNameEntry(NameEntries, ref _nameEntryPosition, _words, ref _wordCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private string GetNextName(int codePoint)
        {
            if (codePoint == _nextNamedCodePoint)
            {
                var name = NameCache[_nameOrdinal / NameBucketSize]?.Names[_nameOrdinal % NameBucketSize];
                if (name is null)
                {
                    var length = WriteName(_words.AsSpan(0, _wordCount), NameEntries, NameWordOffsets, NameWordText, _nameBuffer);
                    name = Encoding.ASCII.GetString(_nameBuffer, 0, length);
                }

                ReadNextNameEntry();
                return name;
            }

            var ranges = NameRanges;
            while (_rangeOffset < ranges.Length && ReadUInt24(ranges, _rangeOffset + 3) < codePoint)
            {
                _rangeOffset += NameRangeRecordSize;
            }

            if (_rangeOffset < ranges.Length && ReadUInt24(ranges, _rangeOffset) <= codePoint)
                return GetRangeName(_rangeOffset, codePoint, addToCache: false);

            return string.Empty;
        }
    }

    /// <summary>The code points of the names of a bucket, in increasing order, and the names decoded so far.</summary>
    private sealed record NameBucket(int[] CodePoints, string?[] Names);
}
