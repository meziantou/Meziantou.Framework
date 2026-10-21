using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// A prefix (context) mapping: the code points that precede the mapped string (in text order), the code points of the
/// string after its first one (empty for a single code point), and the collation elements.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CollationPrefixMapping(int[] Prefix, int[] Suffix, int Offset, int Count);

/// <summary>
/// The mappings of a collation table (the converted root collation, or the mappings of a tailoring): the collation
/// elements of the code points (a two-level table), of the contractions (sequences of code points in NFD) and of the
/// prefix mappings (a code point after a given context). The collation elements use the format of
/// <see cref="CollationWeights"/>. A map is built once and then only read (thread-safe for reading).
/// </summary>
internal sealed class CollationMap
{
    /// <summary>The number of collation elements of a code point (0: no own mapping).</summary>
    public const int CountMask = 0x1F;

    /// <summary>The code point starts at least one contraction.</summary>
    public const int ContractionFlag = 0x20;

    /// <summary>The code point has prefix mappings.</summary>
    public const int PrefixFlag = 0x40;

    private const int OffsetShift = 7;
    private const int PageShift = 8;
    private const int PageSize = 1 << PageShift;

    private readonly int[] _pageIndexes;
    private int[] _pages = new int[PageSize * 8];
    private int _pageCount;
    private ulong[] _ces = new ulong[1024];
    private int _cesCount;
    private readonly Dictionary<string, (int Offset, int Count)> _contractions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Offset, int Count)>.AlternateLookup<ReadOnlySpan<char>> _contractionLookup;
    private readonly Dictionary<int, CollationPrefixMapping[]> _prefixes = [];
    private readonly ulong[] _continuations = new ulong[0x10000 / 64];
    private bool _hasSupplementaryContinuations;

    public CollationMap()
    {
        _pageIndexes = new int[(0x10FFFF >> PageShift) + 1];
        _pageIndexes.AsSpan().Fill(-1);
        _contractionLookup = _contractions.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    /// <summary>Gets the length (in code points) of the longest contraction.</summary>
    public int MaxContractionLength { get; private set; } = 1;

    /// <summary>Gets the length (in code points) of the longest prefix of the prefix mappings.</summary>
    public int MaxPrefixLength { get; private set; }

    /// <summary>Gets the number of collation elements of the pool (the elements of every mapping).</summary>
    public int ElementCount => _cesCount;

    /// <summary>Gets the number of contractions.</summary>
    public int ContractionCount => _contractions.Count;

    /// <summary>Gets the entry of a code point: <c>offset &lt;&lt; 7 | flags | count</c>, 0 when the map has nothing for it.</summary>
    public int GetEntry(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF)
            return 0;

        var page = _pageIndexes[codePoint >> PageShift];
        return page < 0 ? 0 : _pages[(page << PageShift) | (codePoint & (PageSize - 1))];
    }

    /// <summary>Gets the collation elements of an entry that has a mapping (count &gt; 0).</summary>
    public ReadOnlySpan<ulong> GetElements(int entry) => _ces.AsSpan(entry >>> OffsetShift, entry & CountMask);

    /// <summary>Gets the collation elements of a range of the pool.</summary>
    public ReadOnlySpan<ulong> GetElements(int offset, int count) => _ces.AsSpan(offset, count);

    /// <summary>Gets the collation elements of a contraction (at least two code points).</summary>
    public bool TryGetContraction(scoped ReadOnlySpan<int> codePoints, out ReadOnlySpan<ulong> elements)
    {
        Span<char> buffer = stackalloc char[codePoints.Length * 2];
        var length = 0;
        foreach (var codePoint in codePoints)
        {
            if (codePoint < 0x10000)
            {
                buffer[length++] = (char)codePoint;
            }
            else
            {
                buffer[length++] = (char)(0xD7C0 + (codePoint >> 10));
                buffer[length++] = (char)(0xDC00 | (codePoint & 0x3FF));
            }
        }

        if (_contractionLookup.TryGetValue(buffer[..length], out var value))
        {
            elements = _ces.AsSpan(value.Offset, value.Count);
            return true;
        }

        elements = default;
        return false;
    }

    /// <summary>Gets whether a code point is in a contraction after its first code point (a quick check before looking up the contractions).</summary>
    public bool IsContinuation(int codePoint)
    {
        if (codePoint > 0xFFFF)
            return _hasSupplementaryContinuations;

        return (_continuations[codePoint >> 6] & (1UL << (codePoint & 63))) != 0;
    }

    /// <summary>Gets the prefix mappings of a code point, the longest prefixes first.</summary>
    public CollationPrefixMapping[]? GetPrefixMappings(int codePoint) => _prefixes.TryGetValue(codePoint, out var value) ? value : null;

    /// <summary>Enumerates the contractions (code points, elements).</summary>
    public IEnumerable<(string Key, int Offset, int Count)> GetContractions()
    {
        foreach (var (key, (offset, count)) in _contractions)
        {
            yield return (key, offset, count);
        }
    }

    /// <summary>Sets the mapping of a code point (replaces the previous one).</summary>
    public void SetMapping(int codePoint, ReadOnlySpan<ulong> elements)
    {
        if (elements.Length is 0 or > CountMask)
            throw new InvalidOperationException("Invalid number of collation elements");

        var offset = AddElements(elements);
        ref var entry = ref GetOrCreateEntry(codePoint);
        entry = (offset << OffsetShift) | (entry & (ContractionFlag | PrefixFlag)) | elements.Length;
    }

    /// <summary>Sets the mapping of a contraction (two code points or more, in NFD).</summary>
    public void SetContraction(ReadOnlySpan<int> codePoints, ReadOnlySpan<ulong> elements)
    {
        var offset = AddElements(elements);
        _contractions[ToKey(codePoints)] = (offset, elements.Length);
        GetOrCreateEntry(codePoints[0]) |= ContractionFlag;
        foreach (var codePoint in codePoints[1..])
        {
            if (codePoint > 0xFFFF)
            {
                _hasSupplementaryContinuations = true;
            }
            else
            {
                _continuations[codePoint >> 6] |= 1UL << (codePoint & 63);
            }
        }

        MaxContractionLength = Math.Max(MaxContractionLength, codePoints.Length);
    }

    /// <summary>Sets a prefix mapping: <paramref name="codePoints"/> (in NFD) after <paramref name="prefix"/>.</summary>
    public void SetPrefixMapping(ReadOnlySpan<int> prefix, ReadOnlySpan<int> codePoints, ReadOnlySpan<ulong> elements)
    {
        var offset = AddElements(elements);
        var mapping = new CollationPrefixMapping(prefix.ToArray(), codePoints[1..].ToArray(), offset, elements.Length);
        var codePoint = codePoints[0];
        var list = _prefixes.TryGetValue(codePoint, out var existing) ? new List<CollationPrefixMapping>(existing) : [];
        list.RemoveAll(item => item.Prefix.AsSpan().SequenceEqual(mapping.Prefix) && item.Suffix.AsSpan().SequenceEqual(mapping.Suffix));
        list.Add(mapping);
        list.Sort((a, b) => a.Prefix.Length != b.Prefix.Length ? b.Prefix.Length.CompareTo(a.Prefix.Length) : b.Suffix.Length.CompareTo(a.Suffix.Length));
        _prefixes[codePoint] = [.. list];
        GetOrCreateEntry(codePoint) |= PrefixFlag;
        MaxPrefixLength = Math.Max(MaxPrefixLength, prefix.Length);
    }

    /// <summary>Replaces every collation element of the pool.</summary>
    public void TransformElements(Func<ulong, ulong> transform)
    {
        for (var i = 0; i < _cesCount; i++)
        {
            _ces[i] = transform(_ces[i]);
        }
    }

    /// <summary>Releases the unused capacity once the map is complete.</summary>
    public void Trim()
    {
        Array.Resize(ref _ces, _cesCount);
        Array.Resize(ref _pages, _pageCount * PageSize);
    }

    internal static string ToKey(ReadOnlySpan<int> codePoints)
    {
        var builder = new StringBuilder(codePoints.Length * 2);
        foreach (var codePoint in codePoints)
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }

        return builder.ToString();
    }

    private int AddElements(ReadOnlySpan<ulong> elements)
    {
        if (_cesCount + elements.Length > _ces.Length)
        {
            Array.Resize(ref _ces, Math.Max(_ces.Length * 2, _cesCount + elements.Length));
        }

        var offset = _cesCount;
        elements.CopyTo(_ces.AsSpan(offset));
        _cesCount += elements.Length;
        if (_cesCount >= 1 << (31 - OffsetShift))
            throw new InvalidOperationException("Too many collation elements");

        return offset;
    }

    private ref int GetOrCreateEntry(int codePoint)
    {
        ref var pageIndex = ref _pageIndexes[codePoint >> PageShift];
        if (pageIndex < 0)
        {
            if ((_pageCount + 1) * PageSize > _pages.Length)
            {
                Array.Resize(ref _pages, _pages.Length * 2);
            }

            pageIndex = _pageCount++;
        }

        return ref _pages[(pageIndex << PageShift) | (codePoint & (PageSize - 1))];
    }
}
