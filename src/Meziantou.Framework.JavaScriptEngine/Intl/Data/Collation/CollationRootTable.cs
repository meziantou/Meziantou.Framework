using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;

/// <summary>
/// The CLDR root collation (allkeys_CLDR.txt of the CLDR release, the DUCET with the CLDR changes), with the implicit
/// weights of UCA (section 10.1). Decoded from the root chunk of collation.bin:
/// <code>
/// ucaVersion entryCount (keyLength codePoint* elementCount element*)*     (sorted by key; elements as CollationElement.Value)
/// siniformRangeCount (start end base firstSecondWeight)*                (Tangut, Nushu, Khitan: from FractionalUCA.txt)
/// unifiedIdeographRangeCount (start end)*                               (Unified_Ideograph of PropList.txt)
/// </code>
/// The instances are immutable and thread-safe.
/// </summary>
internal sealed class CollationRootTable
{
    private const int PageShift = 8;
    private const int PageSize = 1 << PageShift;
    private const int ContractionFlag = 1 << 5;
    private const int CountMask = ContractionFlag - 1;

    private readonly CollationElement[] _elements;
    private readonly int[] _pageIndexes;
    private readonly int[] _pages;
    private readonly Dictionary<string, (int Offset, int Count)> _contractions;
    private readonly Dictionary<string, (int Offset, int Count)>.AlternateLookup<ReadOnlySpan<char>> _contractionLookup;
    private readonly (int Start, int End, int Base, int FirstSecondWeight)[] _siniformRanges;
    private readonly (int Start, int End)[] _unifiedIdeographRanges;
    private readonly List<(int[] Key, int Offset, int Count)> _entries;

    internal CollationRootTable(byte[] data)
    {
        var reader = new CldrBinaryReader(data, 0);
        UcaVersion = reader.ReadString();
        var entryCount = reader.ReadVarInt32();
        var elements = new List<CollationElement>(entryCount * 2);
        var singles = new Dictionary<int, int>(entryCount);
        var contractionStarts = new HashSet<int>();
        _contractions = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        _entries = new List<(int[], int, int)>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var key = new int[reader.ReadVarInt32()];
            for (var j = 0; j < key.Length; j++)
            {
                key[j] = reader.ReadVarInt32();
            }

            var count = reader.ReadVarInt32();
            var offset = elements.Count;
            for (var j = 0; j < count; j++)
            {
                elements.Add(new CollationElement((uint)reader.ReadVarUInt64()));
            }

            if (count is 0 or > CountMask)
                throw new InvalidDataException("Invalid collation element count");

            _entries.Add((key, offset, count));
            if (key.Length == 1)
            {
                singles[key[0]] = (offset << 6) | count;
            }
            else
            {
                _contractions.Add(ToString(key), (offset, count));
                contractionStarts.Add(key[0]);
                MaxContractionLength = Math.Max(MaxContractionLength, key.Length);
            }
        }

        _elements = [.. elements];
        _contractionLookup = _contractions.GetAlternateLookup<ReadOnlySpan<char>>();

        // Two-level table of the single code points: entry = offset << 6 | hasContraction << 5 | count
        _pageIndexes = new int[(0x10FFFF >> PageShift) + 1];
        _pageIndexes.AsSpan().Fill(-1);
        var pages = new List<int>();
        foreach (var codePoint in singles.Keys.Concat(contractionStarts))
        {
            var pageNumber = codePoint >> PageShift;
            if (_pageIndexes[pageNumber] < 0)
            {
                _pageIndexes[pageNumber] = pages.Count / PageSize;
                pages.AddRange(new int[PageSize]);
            }
        }

        _pages = [.. pages];
        foreach (var (codePoint, value) in singles)
        {
            _pages[(_pageIndexes[codePoint >> PageShift] * PageSize) + (codePoint & (PageSize - 1))] = value;
        }

        foreach (var codePoint in contractionStarts)
        {
            _pages[(_pageIndexes[codePoint >> PageShift] * PageSize) + (codePoint & (PageSize - 1))] |= ContractionFlag;
        }

        _siniformRanges = new (int, int, int, int)[reader.ReadVarInt32()];
        for (var i = 0; i < _siniformRanges.Length; i++)
        {
            _siniformRanges[i] = (reader.ReadVarInt32(), reader.ReadVarInt32(), reader.ReadVarInt32(), reader.ReadVarInt32());
        }

        _unifiedIdeographRanges = new (int, int)[reader.ReadVarInt32()];
        for (var i = 0; i < _unifiedIdeographRanges.Length; i++)
        {
            _unifiedIdeographRanges[i] = (reader.ReadVarInt32(), reader.ReadVarInt32());
        }
    }

    /// <summary>Gets the version of UCA of the table (e.g. "17.0.0").</summary>
    public string UcaVersion { get; }

    /// <summary>Gets the length (in code points) of the longest contraction.</summary>
    public int MaxContractionLength { get; } = 1;

    /// <summary>Gets the number of mappings (single code points and contractions).</summary>
    public int Count => _entries.Count;

    /// <summary>Enumerates the mappings in the order of their key (code points).</summary>
    public IEnumerable<(int[] CodePoints, CollationElement[] Elements)> GetMappings()
    {
        foreach (var (key, offset, count) in _entries)
        {
            yield return (key, _elements.AsSpan(offset, count).ToArray());
        }
    }

    /// <summary>Gets the collation elements of a single code point listed in the table (not the implicit weights).</summary>
    public bool TryGetMapping(int codePoint, out ReadOnlySpan<CollationElement> elements)
    {
        var entry = GetEntry(codePoint);
        if ((entry & CountMask) == 0)
        {
            elements = default;
            return false;
        }

        elements = _elements.AsSpan(entry >> 6, entry & CountMask);
        return true;
    }

    /// <summary>Gets whether a code point is the first one of a contraction.</summary>
    public bool StartsContraction(int codePoint) => (GetEntry(codePoint) & ContractionFlag) != 0;

    /// <summary>Gets the collation elements of a contraction (a sequence of at least two code points).</summary>
    public bool TryGetContraction(scoped ReadOnlySpan<int> codePoints, out ReadOnlySpan<CollationElement> elements)
    {
        Span<char> buffer = stackalloc char[codePoints.Length * 2];
        var length = 0;
        foreach (var codePoint in codePoints)
        {
            length += new Rune(codePoint).EncodeToUtf16(buffer[length..]);
        }

        if (_contractionLookup.TryGetValue(buffer[..length], out var value))
        {
            elements = _elements.AsSpan(value.Offset, value.Count);
            return true;
        }

        elements = default;
        return false;
    }

    /// <summary>Gets the two implicit collation elements of a code point that is not in the table (UCA 10.1.3).</summary>
    public (CollationElement First, CollationElement Second) GetImplicitElements(int codePoint)
    {
        int aaaa, bbbb;
        foreach (var (start, end, @base, firstSecondWeight) in _siniformRanges)
        {
            if (codePoint >= start && codePoint <= end)
            {
                aaaa = @base;
                bbbb = firstSecondWeight + (codePoint - start);
                return (CollationElement.Create(aaaa, 0x20, 0x02, isVariable: false), CollationElement.Create(bbbb, 0, 0, isVariable: false));
            }
        }

        var baseWeight = 0xFBC0;
        if (IsUnifiedIdeograph(codePoint))
        {
            baseWeight = codePoint is (>= 0x4E00 and <= 0x9FFF) or (>= 0xF900 and <= 0xFAFF) ? 0xFB40 : 0xFB80;
        }

        aaaa = baseWeight + (codePoint >> 15);
        bbbb = (codePoint & 0x7FFF) | 0x8000;
        return (CollationElement.Create(aaaa, 0x20, 0x02, isVariable: false), CollationElement.Create(bbbb, 0, 0, isVariable: false));
    }

    /// <summary>Gets whether a code point has the Unified_Ideograph property (of the version of Unicode of the table).</summary>
    public bool IsUnifiedIdeograph(int codePoint)
    {
        foreach (var (start, end) in _unifiedIdeographRanges)
        {
            if (codePoint >= start && codePoint <= end)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Appends the canonical decomposition (NFD) of a string to a list of code points; a lone surrogate is kept as a
    /// code point (UCA step S1).
    /// </summary>
    public static void AppendNfd(ReadOnlySpan<char> text, List<int> output)
    {
        Span<int> buffer = stackalloc int[UnicodeNormalization.MaxCanonicalDecompositionLength];
        for (var i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            int count;
            if (codePoint <= 0xFFFF && char.IsSurrogate((char)codePoint))
            {
                buffer[0] = codePoint;
                count = 1;
            }
            else
            {
                count = UnicodeNormalization.GetCanonicalDecomposition(codePoint, buffer);
            }

            foreach (var item in buffer[..count])
            {
                // Canonical ordering: a non-starter moves before the preceding non-starters of a higher class
                var combiningClass = UnicodeProperties.GetCanonicalCombiningClass(item);
                var position = output.Count;
                if (combiningClass != 0)
                {
                    while (position > 0 && UnicodeProperties.GetCanonicalCombiningClass(output[position - 1]) > combiningClass)
                    {
                        position--;
                    }
                }

                output.Insert(position, item);
            }
        }
    }

    /// <summary>Appends the collation elements of a string (NFD, then <see cref="AppendCollationElements(ReadOnlySpan{int}, List{CollationElement})"/>).</summary>
    public void AppendCollationElements(ReadOnlySpan<char> text, List<CollationElement> output)
    {
        var codePoints = new List<int>(text.Length);
        AppendNfd(text, codePoints);
        AppendCollationElements(CollectionsMarshal.AsSpan(codePoints), output);
    }

    /// <summary>
    /// Appends the collation elements of a string in NFD (UCA step S2, with the contiguous and discontiguous
    /// contractions and the implicit weights), without tailoring.
    /// </summary>
    public void AppendCollationElements(ReadOnlySpan<int> nfdCodePoints, List<CollationElement> output)
    {
        var codePoints = new List<int>(nfdCodePoints.Length);
        foreach (var codePoint in nfdCodePoints)
        {
            codePoints.Add(codePoint);
        }

        Span<int> key = stackalloc int[MaxContractionLength + 8];
        var i = 0;
        while (i < codePoints.Count)
        {
            var codePoint = codePoints[i];
            var matchLength = 1;
            ReadOnlySpan<CollationElement> elements = default;
            var found = TryGetMapping(codePoint, out elements);
            if (StartsContraction(codePoint))
            {
                // S2.1: the longest contiguous match
                for (var length = Math.Min(MaxContractionLength, codePoints.Count - i); length >= 2; length--)
                {
                    for (var j = 0; j < length; j++)
                    {
                        key[j] = codePoints[i + j];
                    }

                    if (TryGetContraction(key[..length], out var contraction))
                    {
                        elements = contraction;
                        found = true;
                        matchLength = length;
                        break;
                    }
                }

                // S2.1.1-S2.1.3: the unblocked non-starters that extend the match
                var keyLength = matchLength;
                for (var j = 0; j < matchLength; j++)
                {
                    key[j] = codePoints[i + j];
                }

                var position = i + matchLength;
                var lastSkippedCombiningClass = -1;
                while (position < codePoints.Count && keyLength < key.Length)
                {
                    var combiningClass = UnicodeProperties.GetCanonicalCombiningClass(codePoints[position]);
                    if (combiningClass == 0)
                        break;

                    if (combiningClass > lastSkippedCombiningClass)
                    {
                        key[keyLength] = codePoints[position];
                        if (TryGetContraction(key[..(keyLength + 1)], out var contraction))
                        {
                            elements = contraction;
                            found = true;
                            keyLength++;
                            codePoints.RemoveAt(position);
                            continue;
                        }
                    }

                    lastSkippedCombiningClass = combiningClass;
                    position++;
                }
            }

            if (found)
            {
                foreach (var element in elements)
                {
                    output.Add(element);
                }
            }
            else
            {
                var (first, second) = GetImplicitElements(codePoint);
                output.Add(first);
                output.Add(second);
            }

            i += matchLength;
        }
    }

    private int GetEntry(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF)
            return 0;

        var page = _pageIndexes[codePoint >> PageShift];
        return page < 0 ? 0 : _pages[(page * PageSize) + (codePoint & (PageSize - 1))];
    }

    private static string ToString(int[] codePoints)
    {
        var builder = new StringBuilder(codePoints.Length * 2);
        foreach (var codePoint in codePoints)
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }

        return builder.ToString();
    }
}
