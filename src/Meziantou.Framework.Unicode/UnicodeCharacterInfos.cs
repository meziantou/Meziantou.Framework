using System.Collections;
using System.Reflection;
using static Meziantou.Framework.UnicodeDataFormat;

namespace Meziantou.Framework;

internal static partial class UnicodeCharacterInfos
{
    private const string ResourceName = "Meziantou.Framework.Resources.UnicodeData.bin";

    private static readonly UnicodeDataTable Table = UnicodeDataTable.Load();

    public static IReadOnlyCollection<UnicodeCharacterInfo> AllCharacters => Table;

    public static bool TryGetInfo(Rune rune, out UnicodeCharacterInfo info)
    {
        return Table.TryGetInfo(rune, out info);
    }

    /// <summary>Reads the Unicode Character Database out of the embedded resource, in place.</summary>
    /// <remarks>
    /// The resource is kept as a single byte array and searched rather than expanded into a
    /// dictionary of every code point: the database describes 297,334 characters, but they only
    /// take a few thousand distinct property tuples once contiguous ranges are kept as ranges.
    /// Only names and decomposition mappings are materialized as strings, lazily and cached, so a
    /// program that never asks for them never pays for them. See <see cref="UnicodeDataFormat"/>
    /// for the layout.
    /// </remarks>
    private sealed class UnicodeDataTable : IReadOnlyCollection<UnicodeCharacterInfo>
    {
        private readonly byte[] _data;

        private readonly UnicodeCharacterProperties[] _properties;
        private readonly int[] _runStarts;
        private readonly int[] _runLengths;
        private readonly int[] _runProperties;

        private readonly int[] _namedCodePoints;
        private readonly int[] _nameBucketOffsets;
        private readonly int _nameTextStart;
        private string?[]? _names;

        private readonly string[] _rangeNames;
        private readonly int[] _rangeNameStarts;
        private readonly int[] _rangeNameLengths;
        private readonly int[] _rangeNameIndexes;

        private readonly string[] _decompositionTags;
        private readonly int[] _decompositionCodePoints;
        private readonly int[] _decompositionOffsets;
        private string?[]? _decompositions;

        private readonly StringColumn _numericValues;
        private readonly StringColumn _unicode1Names;
        private readonly StringColumn _isoComments;

        public int Count { get; }

        public static UnicodeDataTable Load()
        {
            using var stream = typeof(Unicode).GetTypeInfo().Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                throw new InvalidOperationException("Cannot find Unicode data resource: " + ResourceName);

            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            return new UnicodeDataTable(data);
        }

        private UnicodeDataTable(byte[] data)
        {
            _data = data;
            var span = data.AsSpan();
            if (!span[..Magic.Length].SequenceEqual(Magic) || span[Magic.Length] != LayoutVersion)
                throw new InvalidDataException("The Unicode data resource is not in the expected format.");

            var position = HeaderLength;

            var propertyCount = ReadVarInt(span, ref position);
            _properties = new UnicodeCharacterProperties[propertyCount];
            for (var i = 0; i < propertyCount; i++)
            {
                var fixedPart = span.Slice(position, PropertyFixedLength);
                position += PropertyFixedLength;
                _properties[i] = new UnicodeCharacterProperties(
                    (UnicodeCategory)fixedPart[0],
                    (UnicodeBidirectionalCategory)fixedPart[1],
                    fixedPart[2],
                    (sbyte)fixedPart[3],
                    (sbyte)fixedPart[4],
                    fixedPart[5],
                    fixedPart[6],
                    ReadZigZagVarInt(span, ref position),
                    ReadZigZagVarInt(span, ref position),
                    ReadZigZagVarInt(span, ref position));
            }

            var runCount = ReadVarInt(span, ref position);
            _runStarts = new int[runCount];
            _runLengths = new int[runCount];
            _runProperties = new int[runCount];
            var count = 0;
            var previousEnd = 0;
            for (var i = 0; i < runCount; i++)
            {
                var start = previousEnd + ReadVarInt(span, ref position);
                var length = ReadVarInt(span, ref position);
                _runStarts[i] = start;
                _runLengths[i] = length;
                _runProperties[i] = ReadVarInt(span, ref position);
                previousEnd = start + length;
                count += length;
            }

            Count = count;

            var namedCount = ReadVarInt(span, ref position);
            _namedCodePoints = ReadCodePoints(span, ref position, namedCount);
            _nameBucketOffsets = new int[(namedCount + NameBucketSize - 1) / NameBucketSize];
            var offset = 0;
            for (var i = 0; i < _nameBucketOffsets.Length; i++)
            {
                offset += ReadVarInt(span, ref position);
                _nameBucketOffsets[i] = offset;
            }

            var nameTextLength = ReadVarInt(span, ref position);
            _nameTextStart = position;
            position += nameTextLength;

            _rangeNames = ReadStringList(span, ref position);
            var rangeNameRunCount = ReadVarInt(span, ref position);
            _rangeNameStarts = new int[rangeNameRunCount];
            _rangeNameLengths = new int[rangeNameRunCount];
            _rangeNameIndexes = new int[rangeNameRunCount];
            previousEnd = 0;
            for (var i = 0; i < rangeNameRunCount; i++)
            {
                var start = previousEnd + ReadVarInt(span, ref position);
                var length = ReadVarInt(span, ref position);
                _rangeNameStarts[i] = start;
                _rangeNameLengths[i] = length;
                _rangeNameIndexes[i] = ReadVarInt(span, ref position);
                previousEnd = start + length;
            }

            _decompositionTags = ReadStringList(span, ref position);
            var decompositionCount = ReadVarInt(span, ref position);
            _decompositionCodePoints = new int[decompositionCount];
            _decompositionOffsets = new int[decompositionCount];
            var codePoint = 0;
            for (var i = 0; i < decompositionCount; i++)
            {
                codePoint += ReadVarInt(span, ref position);
                _decompositionCodePoints[i] = codePoint;
                _decompositionOffsets[i] = position;
                position++; // tag
                var mappingLength = span[position];
                position++;
                for (var j = 0; j < mappingLength; j++)
                {
                    ReadVarInt(span, ref position);
                }
            }

            _numericValues = StringColumn.Read(span, ref position);
            _unicode1Names = StringColumn.Read(span, ref position);
            _isoComments = StringColumn.Read(span, ref position);
        }

        public bool TryGetInfo(Rune rune, out UnicodeCharacterInfo info)
        {
            var index = FindRange(_runStarts, _runLengths, rune.Value);
            if (index < 0)
            {
                info = default;
                return false;
            }

            info = CreateInfo(rune.Value, _runProperties[index]);
            return true;
        }

        public IEnumerator<UnicodeCharacterInfo> GetEnumerator()
        {
            for (var i = 0; i < _runStarts.Length; i++)
            {
                var end = _runStarts[i] + _runLengths[i];
                var propertyIndex = _runProperties[i];
                for (var codePoint = _runStarts[i]; codePoint < end; codePoint++)
                {
                    yield return CreateInfo(codePoint, propertyIndex);
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private UnicodeCharacterInfo CreateInfo(int codePoint, int propertyIndex)
        {
            var properties = _properties[propertyIndex];
            return new UnicodeCharacterInfo(
                rune: new Rune(codePoint),
                name: GetName(codePoint),
                category: properties.Category,
                bidiCategory: properties.BidiCategory,
                block: UnicodeBlocks.GetBlock(codePoint),
                canonicalCombiningClass: properties.CanonicalCombiningClass,
                decompositionMapping: GetDecompositionMapping(codePoint),
                decimalDigitValue: properties.DecimalDigitValue,
                digitValue: properties.DigitValue,
                numericValue: _numericValues.Get(codePoint),
                mirrored: (properties.Flags & MirroredFlag) != 0,
                unicode1Name: _unicode1Names.Get(codePoint),
                isoComment: _isoComments.Get(codePoint),
                simpleUppercaseMapping: properties.GetMapping(codePoint, HasUppercaseFlag),
                simpleLowercaseMapping: properties.GetMapping(codePoint, HasLowercaseFlag),
                simpleTitlecaseMapping: properties.GetMapping(codePoint, HasTitlecaseFlag),
                emojiProperties: properties.EmojiProperties);
        }

        private string GetName(int codePoint)
        {
            var index = Array.BinarySearch(_namedCodePoints, codePoint);
            if (index >= 0)
                return GetNameAt(index);

            var rangeIndex = FindRange(_rangeNameStarts, _rangeNameLengths, codePoint);
            return rangeIndex < 0 ? string.Empty : _rangeNames[_rangeNameIndexes[rangeIndex]];
        }

        /// <summary>Decodes the name at <paramref name="index"/> by replaying its front-coding bucket.</summary>
        private string GetNameAt(int index)
        {
            // Caching keeps a code point looked up more than once from decoding and allocating its
            // name again, which the eagerly expanded table this replaced did for free.
            var names = _names ?? CreateCache(ref _names, _namedCodePoints.Length);
            var name = names[index];
            if (name is not null)
                return name;

            var span = _data.AsSpan();
            var position = _nameTextStart + _nameBucketOffsets[index / NameBucketSize];
            Span<byte> buffer = stackalloc byte[MaxSerializedStringLength];
            var length = 0;
            for (var i = index - (index % NameBucketSize); i <= index; i++)
            {
                length = ReadFrontCoded(span, ref position, buffer);
            }

            name = Encoding.UTF8.GetString(buffer[..length]);
            names[index] = name;
            return name;
        }

        private string? GetDecompositionMapping(int codePoint)
        {
            var index = Array.BinarySearch(_decompositionCodePoints, codePoint);
            if (index < 0)
                return null;

            var decompositions = _decompositions ?? CreateCache(ref _decompositions, _decompositionCodePoints.Length);
            var mapping = decompositions[index];
            if (mapping is not null)
                return mapping;

            var span = _data.AsSpan();
            var position = _decompositionOffsets[index];
            var tag = span[position];
            position++;
            var mappingLength = span[position];
            position++;

            var builder = new StringBuilder();
            if (tag != 0)
            {
                builder.Append(_decompositionTags[tag - 1]);
            }

            for (var i = 0; i < mappingLength; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(CultureInfo.InvariantCulture, $"{ReadVarInt(span, ref position):X4}");
            }

            mapping = builder.ToString();
            decompositions[index] = mapping;
            return mapping;
        }

        /// <summary>Finds the range containing <paramref name="codePoint"/>, or a negative value.</summary>
        private static int FindRange(int[] starts, int[] lengths, int codePoint)
        {
            var index = Array.BinarySearch(starts, codePoint);
            if (index >= 0)
                return index;

            // The greatest start below the code point; ~index is where it would be inserted.
            index = ~index - 1;
            if (index < 0 || codePoint >= starts[index] + lengths[index])
                return -1;

            return index;
        }

        private static int[] ReadCodePoints(ReadOnlySpan<byte> data, ref int position, int count)
        {
            var values = new int[count];
            var codePoint = 0;
            for (var i = 0; i < count; i++)
            {
                codePoint += ReadVarInt(data, ref position);
                values[i] = codePoint;
            }

            return values;
        }

        private static string[] ReadStringList(ReadOnlySpan<byte> data, ref int position)
        {
            var values = new string[ReadVarInt(data, ref position)];
            for (var i = 0; i < values.Length; i++)
            {
                var length = data[position];
                position++;
                values[i] = Encoding.UTF8.GetString(data.Slice(position, length));
                position += length;
            }

            return values;
        }

        /// <summary>Decodes one front-coded entry into <paramref name="buffer"/> and returns its length.</summary>
        private static int ReadFrontCoded(ReadOnlySpan<byte> data, ref int position, Span<byte> buffer)
        {
            var sharedPrefixLength = data[position];
            var suffixLength = data[position + 1];
            position += 2;
            data.Slice(position, suffixLength).CopyTo(buffer[sharedPrefixLength..]);
            position += suffixLength;
            return sharedPrefixLength + suffixLength;
        }

        private static string?[] CreateCache(ref string?[]? location, int length)
        {
            // Two threads may build the array at once; the loser's entries are simply recomputed.
            return Interlocked.CompareExchange(ref location, new string?[length], comparand: null) ?? location;
        }

        /// <summary>A property that only a few thousand code points have, stored as a sorted side table.</summary>
        private readonly struct StringColumn
        {
            private readonly string[] _values;
            private readonly int[] _codePoints;
            private readonly int[] _indexes;

            private StringColumn(string[] values, int[] codePoints, int[] indexes)
            {
                _values = values;
                _codePoints = codePoints;
                _indexes = indexes;
            }

            public static StringColumn Read(ReadOnlySpan<byte> data, ref int position)
            {
                var values = new string[ReadVarInt(data, ref position)];
                Span<byte> buffer = stackalloc byte[MaxSerializedStringLength];
                for (var i = 0; i < values.Length; i++)
                {
                    var length = ReadFrontCoded(data, ref position, buffer);
                    values[i] = Encoding.UTF8.GetString(buffer[..length]);
                }

                var count = ReadVarInt(data, ref position);
                var codePoints = new int[count];
                var indexes = new int[count];
                var codePoint = 0;
                for (var i = 0; i < count; i++)
                {
                    codePoint += ReadVarInt(data, ref position);
                    codePoints[i] = codePoint;
                    indexes[i] = ReadVarInt(data, ref position);
                }

                return new StringColumn(values, codePoints, indexes);
            }

            public string? Get(int codePoint)
            {
                var index = Array.BinarySearch(_codePoints, codePoint);
                return index < 0 ? null : _values[_indexes[index]];
            }
        }
    }
}
