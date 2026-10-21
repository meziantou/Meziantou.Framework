using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;
using Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>The case-first setting of a collation (UTS 35 <c>kf</c>).</summary>
internal enum CollationCaseFirst
{
    Off,
    Lower,
    Upper,
}

/// <summary>The settings of a comparison: the levels compared and the options of UTS 35 that ECMA-402 exposes.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CollatorSettings(int Strength, bool CaseLevel, CollationCaseFirst CaseFirst, bool Shifted, bool Numeric)
{
    /// <summary>Gets the settings of an ECMA-402 sensitivity: base, accent, case or variant.</summary>
    public static CollatorSettings FromSensitivity(string sensitivity, CollationCaseFirst caseFirst, bool ignorePunctuation, bool numeric) => sensitivity switch
    {
        "base" => new CollatorSettings(1, CaseLevel: false, caseFirst, ignorePunctuation, numeric),
        "accent" => new CollatorSettings(2, CaseLevel: false, caseFirst, ignorePunctuation, numeric),
        "case" => new CollatorSettings(1, CaseLevel: true, caseFirst, ignorePunctuation, numeric),
        _ => new CollatorSettings(3, CaseLevel: false, caseFirst, ignorePunctuation, numeric),
    };

    internal int Key => Strength | (CaseLevel ? 8 : 0) | ((int)CaseFirst << 4) | (Shifted ? 64 : 0) | (Numeric ? 128 : 0);
}

/// <summary>A collation of CLDR: the table of a tailoring and its settings (<c>[backwards 2]</c>, <c>[reorder]</c>, the default <c>caseFirst</c> and <c>alternate</c>).</summary>
internal sealed class TailoredCollation
{
    private static readonly ConcurrentDictionary<string, Lazy<TailoredCollation>> ByRules = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string Locale, string Type), TailoredCollation?> ByLocale = new();
    private readonly ConcurrentDictionary<int, StringCollator> _collators = new();
    private FastCharacters? _fastCharacters;

    private TailoredCollation(string rules)
    {
        var tailoring = CollationTailoring.Parse(rules);
        Table = tailoring.Chains.Count > 0 || tailoring.SuppressContractions.Count > 0 ? CollationBuilder.Build(tailoring) : new CollationTable(tailored: null, suppressedContractions: null);
        Backwards = tailoring.Backwards is true;
        CaseFirst = tailoring.CaseFirst switch
        {
            "upper" => CollationCaseFirst.Upper,
            "lower" => CollationCaseFirst.Lower,
            _ => CollationCaseFirst.Off,
        };
        IsShiftedByDefault = tailoring.Alternate is "shifted";
        ReorderTable = RootCollation.Instance.GetReorderTable(tailoring.Reorder);
    }

    /// <summary>Gets the root collation.</summary>
    public static TailoredCollation Root => Get("");

    public CollationTable Table { get; }

    /// <summary>Gets whether the secondary weights are compared backwards (<c>[backwards 2]</c>, French of Canada).</summary>
    public bool Backwards { get; }

    /// <summary>Gets the default case-first setting of the collation (<c>[caseFirst upper]</c> in Danish).</summary>
    public CollationCaseFirst CaseFirst { get; }

    /// <summary>Gets whether the variable characters are ignored by default (<c>[alternate shifted]</c> in Thai).</summary>
    public bool IsShiftedByDefault { get; }

    /// <summary>Gets the permutation of the primary leads of <c>[reorder]</c>, or null.</summary>
    public ushort[]? ReorderTable { get; }

    /// <summary>Gets the collation of tailoring rules (built once per process).</summary>
    public static TailoredCollation Get(string rules) => ByRules.GetOrAdd(rules, static r => new Lazy<TailoredCollation>(() => new TailoredCollation(r))).Value;

    /// <summary>Gets the collation of a CLDR collation type ("standard", "phonebook", "search"...) for a locale, or null when the locale does not have the type.</summary>
    public static TailoredCollation? Get(string locale, string type)
    {
        return ByLocale.GetOrAdd((locale, type), static key =>
        {
            var rules = CldrData.Default.Collation.GetRules(LanguageId.Parse(key.Locale), key.Type);
            return rules is null ? null : Get(rules);
        });
    }

    /// <summary>Gets the collation elements of the characters below U+0300 and their contraction data (for the fast path of the comparison).</summary>
    public FastCharacters FastCharacters => Volatile.Read(ref _fastCharacters) ?? CldrData.InitializeOnce(ref _fastCharacters, new FastCharacters(Table));

    /// <summary>Gets the collator of a set of settings.</summary>
    public StringCollator GetCollator(CollatorSettings settings) => _collators.GetOrAdd(settings.Key, static (_, state) => new StringCollator(state.Collation, state.Settings), (Collation: this, Settings: settings));
}

/// <summary>
/// The collation elements of the characters below U+0300 (not processed for the settings), or -1 when they depend on the
/// context (prefix mappings, a precomposed character that can start a longer contraction), and the contraction data
/// used to know whether a character starts a contraction with the next one.
/// </summary>
internal sealed class FastCharacters
{
    public const int Limit = 0x300;

    public FastCharacters(CollationTable table)
    {
        var root = table.Root;
        MaxContractionLength = Math.Max(root.Map.MaxContractionLength, table.Tailored?.MaxContractionLength ?? 1);

        // The contractions that start with a character of the fast range
        var contractionKeys = new List<string>();
        foreach (var map in (CollationMap?[])[table.Tailored, root.Map])
        {
            if (map is null)
                continue;

            foreach (var (key, _, _) in map.GetContractions())
            {
                var first = char.ConvertToUtf32(key, 0);
                if (first >= Limit || (map != table.Tailored && table.SuppressedContractions?.Contains(first) is true))
                    continue;

                contractionKeys.Add(key);
                if (key[1] < Limit)
                {
                    ContractionSecond[key[1]] = true;
                }
            }
        }

        var elements = new List<ulong>();
        var buffer = new ulong[32];
        var codePoints = new int[8];
        Span<char> text = stackalloc char[1];
        for (var c = 0; c < Limit; c++)
        {
            text[0] = (char)c;
            var nfd = root.Decompose(text, ref codePoints);
            StartsContraction[c] = (GetFlags(table, codePoints[0]) & CollationMap.ContractionFlag) != 0;
            if (nfd > 1 && codePoints[0] < Limit && ContractionSecond[codePoints[0]])
            {
                // A precomposed character whose decomposition starts with a second character of a contraction
                ContractionSecond[c] = true;
            }

            var complex = false;
            for (var i = 0; i < nfd; i++)
            {
                var flags = GetFlags(table, codePoints[i]);
                if ((flags & CollationMap.PrefixFlag) != 0 || (i > 0 && (flags & CollationMap.ContractionFlag) != 0))
                {
                    complex = true;
                }
            }

            if (nfd > 1 && !complex && StartsContraction[c])
            {
                // The collation elements of a precomposed character do not depend on its context unless a longer
                // contraction starts with its decomposition
                var decomposition = CollationMap.ToKey(codePoints.AsSpan(0, nfd));
                foreach (var key in contractionKeys)
                {
                    if (key.Length > decomposition.Length && key.StartsWith(decomposition, StringComparison.Ordinal))
                    {
                        complex = true;
                        break;
                    }
                }
            }

            if (complex)
            {
                Entries[c] = -1;
                continue;
            }

            var count = table.GetElements(codePoints.AsSpan(0, nfd), ref buffer, 0, numeric: false);
            Entries[c] = (elements.Count << 5) | count;
            elements.AddRange(buffer.AsSpan(0, count));
        }

        Elements = [.. elements];
    }

    /// <summary>Gets the entries of the characters: <c>offset &lt;&lt; 5 | count</c> in <see cref="Elements"/>, or -1.</summary>
    public int[] Entries { get; } = new int[Limit];

    public ulong[] Elements { get; }

    /// <summary>Gets whether a character can be the second character of a contraction that starts with a character of the fast range.</summary>
    public bool[] ContractionSecond { get; } = new bool[Limit];

    /// <summary>Gets whether a character (or the first code point of its decomposition) starts a contraction.</summary>
    public bool[] StartsContraction { get; } = new bool[Limit];

    public int MaxContractionLength { get; }

    /// <summary>Gets the contraction and prefix flags of a code point in a table.</summary>
    public static int GetFlags(CollationTable table, int codePoint)
    {
        var flags = table.Tailored?.GetEntry(codePoint) ?? 0;
        if (table.SuppressedContractions is null || !table.SuppressedContractions.Contains(codePoint))
        {
            flags |= table.Root.Map.GetEntry(codePoint) & CollationMap.ContractionFlag;
        }

        return flags & (CollationMap.ContractionFlag | CollationMap.PrefixFlag);
    }
}

/// <summary>
/// Compares strings with a collation and settings (UCA: the collation elements of the NFD of the strings, compared level
/// by level). Thread-safe. Strings of characters below U+0300 use precomputed collation elements and skip their common
/// prefix; the other strings are decomposed into reusable thread-static buffers.
/// </summary>
internal sealed class StringCollator
{
    private const int FastLimit = FastCharacters.Limit;
    private const int ComplexEntry = -1;

    [ThreadStatic]
    private static Buffers? s_buffers;

    private readonly CollationTable _table;
    private readonly RootCollation _root;
    private readonly CollatorSettings _settings;
    private readonly bool _backwards;
    private readonly ushort[]? _reorder;
    private readonly uint _variableTop;
    private readonly uint _firstVariable;

    // Fast path: the processed CEs (variable weighting and reordering applied) of the characters below U+0300
    private readonly int[] _fastEntries = new int[FastLimit];
    private readonly ulong[] _fastElements;
    private readonly bool[] _contractionSecond;
    private readonly bool[] _startsContraction;
    private readonly int _maxContractionLength;

    internal StringCollator(TailoredCollation collation, CollatorSettings settings)
    {
        _table = collation.Table;
        _root = _table.Root;
        _settings = settings;
        _backwards = collation.Backwards;
        _reorder = collation.ReorderTable;
        _variableTop = _root.VariableTop;
        _firstVariable = _root.FirstVariablePrimary;

        var characters = collation.FastCharacters;
        _contractionSecond = characters.ContractionSecond;
        _startsContraction = characters.StartsContraction;
        _maxContractionLength = characters.MaxContractionLength;
        var elements = new List<ulong>();
        Span<ulong> buffer = stackalloc ulong[CollationMap.CountMask];
        for (var c = 0; c < FastLimit; c++)
        {
            var entry = characters.Entries[c];
            if (entry == ComplexEntry || (settings.Numeric && c is >= '0' and <= '9'))
            {
                _fastEntries[c] = ComplexEntry;
                continue;
            }

            var raw = characters.Elements.AsSpan(entry >> 5, entry & 0x1F);
            raw.CopyTo(buffer);
            var count = Process(buffer[..raw.Length]);
            if (count > 0 && CollationWeights.Primary(buffer[0]) == 0 && CollationWeights.Secondary(buffer[0]) != 0)
            {
                // A primary-ignorable CE at the start of a character depends on the previous character (variable weighting)
                _fastEntries[c] = ComplexEntry;
                continue;
            }

            _fastEntries[c] = (elements.Count << 5) | count;
            elements.AddRange(buffer[..count]);
        }

        _fastElements = [.. elements];
    }

    private int GetFlags(int codePoint) => FastCharacters.GetFlags(_table, codePoint);

    /// <summary>Compares two strings: a negative number, 0 or a positive number.</summary>
    public int Compare(string x, string y)
    {
        if (ReferenceEquals(x, y))
            return 0;

        // The common prefix has the same collation elements in both strings when it ends at a boundary that no
        // contraction, prefix mapping, combining mark or number crosses (the secondary weights compared backwards
        // start at the end of the strings)
        var start = 0;
        if (!_backwards)
        {
            start = x.AsSpan().CommonPrefixLength(y);
            if (start == x.Length && start == y.Length)
                return 0;

            while (start > 0 && !(IsSafeBoundary(x, start) && IsSafeBoundary(y, start)))
            {
                start--;
            }
        }

        var xs = x.AsSpan(start);
        var ys = y.AsSpan(start);
        if (IsFast(xs) && IsFast(ys))
            return CompareFast(xs, ys);

        return CompareGeneral(xs, ys);
    }

    /// <summary>Compares two strings without the fast path and the common prefix (for tests).</summary>
    internal int CompareWithoutFastPath(string x, string y) => CompareGeneral(x, y);

    private bool IsSafeBoundary(string text, int position)
    {
        var previous = text[position - 1];
        if (char.IsHighSurrogate(previous))
            return false;

        if (position < text.Length)
        {
            var next = text[position];
            if (next < FastLimit)
            {
                if (_settings.Shifted && _fastEntries[next] == ComplexEntry)
                    return false;

                if (_table.Tailored is not null && (_table.Tailored.GetEntry(next) & CollationMap.PrefixFlag) != 0)
                    return false;
            }
            else
            {
                if (_settings.Shifted || char.IsLowSurrogate(next) || UnicodeProperties.GetCanonicalCombiningClass(next) != 0)
                    return false;

                if ((GetFlags(next) & CollationMap.PrefixFlag) != 0)
                    return false;
            }
        }

        if (_settings.Numeric && _root.GetDigitValue(previous) >= 0)
            return false;

        // No contraction that starts before the boundary can continue after it: a contraction continues with one of
        // the possible second characters
        for (var k = 1; k < _maxContractionLength && k <= position; k++)
        {
            var second = position - k + 1;
            if (second >= text.Length || !StartsContraction(text[position - k]))
                continue;

            var c = text[second];
            if (c >= FastLimit || _contractionSecond[c])
                return false;
        }

        return true;
    }

    private bool StartsContraction(char c)
    {
        if (c < FastLimit)
            return _startsContraction[c] || _fastEntries[c] == ComplexEntry;

        if (_root.IsSimple(c))
            return (GetFlags(c) & CollationMap.ContractionFlag) != 0;

        // A decomposed character (or a surrogate): any of its code points
        if (char.IsSurrogate(c))
            return true;

        Span<int> decomposition = stackalloc int[UnicodeNormalization.MaxCanonicalDecompositionLength];
        var count = UnicodeNormalization.GetCanonicalDecomposition(c, decomposition);
        foreach (var codePoint in decomposition[..count])
        {
            if ((GetFlags(codePoint) & CollationMap.ContractionFlag) != 0)
                return true;
        }

        return false;
    }

    private bool IsFast(ReadOnlySpan<char> text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c >= FastLimit || _fastEntries[c] == ComplexEntry)
                return false;

            // A contraction start followed by a possible second character of a contraction
            if (_startsContraction[c] && i + 1 < text.Length && text[i + 1] < FastLimit && _contractionSecond[text[i + 1]])
                return false;
        }

        return true;
    }

    private int CompareFast(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        // Primary level, with an early exit on the first difference
        var fastEntries = _fastEntries;
        var fastElements = _fastElements;
        int i = 0, j = 0, xOffset = 0, xEnd = 0, yOffset = 0, yEnd = 0;
        while (true)
        {
            uint px = 0, py = 0;
            while (true)
            {
                if (xOffset < xEnd)
                {
                    px = CollationWeights.Primary(fastElements[xOffset++]);
                    if (px != 0)
                        break;

                    continue;
                }

                if (i >= x.Length)
                    break;

                var entry = fastEntries[x[i++]];
                xOffset = entry >> 5;
                xEnd = xOffset + (entry & 0x1F);
            }

            while (true)
            {
                if (yOffset < yEnd)
                {
                    py = CollationWeights.Primary(fastElements[yOffset++]);
                    if (py != 0)
                        break;

                    continue;
                }

                if (j >= y.Length)
                    break;

                var entry = fastEntries[y[j++]];
                yOffset = entry >> 5;
                yEnd = yOffset + (entry & 0x1F);
            }

            if (px != py)
                return px < py ? -1 : 1;

            if (px == 0)
                break;
        }

        if (_settings.Strength == 1 && !_settings.CaseLevel)
            return 0;

        // The other levels
        var xCount = CountFastElements(x);
        var yCount = CountFastElements(y);
        if (xCount + yCount <= 512)
        {
            Span<ulong> xElements = stackalloc ulong[xCount];
            Span<ulong> yElements = stackalloc ulong[yCount];
            FillFastElements(x, xElements);
            FillFastElements(y, yElements);
            return CompareElements(xElements, yElements, comparePrimaries: false);
        }
        else
        {
            var xElements = new ulong[xCount];
            var yElements = new ulong[yCount];
            FillFastElements(x, xElements);
            FillFastElements(y, yElements);
            return CompareElements(xElements, yElements, comparePrimaries: false);
        }
    }

    private int CountFastElements(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var c in text)
        {
            count += _fastEntries[c] & 0x1F;
        }

        return count;
    }

    private void FillFastElements(ReadOnlySpan<char> text, Span<ulong> output)
    {
        var count = 0;
        foreach (var c in text)
        {
            var entry = _fastEntries[c];
            var length = entry & 0x1F;
            if (length == 1)
            {
                output[count++] = _fastElements[entry >> 5];
            }
            else if (length > 1)
            {
                _fastElements.AsSpan(entry >> 5, length).CopyTo(output[count..]);
                count += length;
            }
        }
    }

    private int CompareGeneral(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var buffers = s_buffers ??= new Buffers();
        var xCount = GetProcessedElements(x, ref buffers.XCodePoints, ref buffers.XElements);
        var yCount = GetProcessedElements(y, ref buffers.YCodePoints, ref buffers.YElements);
        return CompareElements(buffers.XElements.AsSpan(0, xCount), buffers.YElements.AsSpan(0, yCount), comparePrimaries: true);
    }

    /// <summary>Gets the processed collation elements of a string (for tests and sort keys).</summary>
    internal ulong[] GetElements(string text)
    {
        var codePoints = new int[16];
        var elements = new ulong[16];
        var count = GetProcessedElements(text, ref codePoints, ref elements);
        return elements.AsSpan(0, count).ToArray();
    }

    private int GetProcessedElements(ReadOnlySpan<char> text, ref int[] codePoints, ref ulong[] elements)
    {
        var length = _root.Decompose(text, ref codePoints);
        var count = _table.GetElements(codePoints.AsSpan(0, length), ref elements, 0, _settings.Numeric);
        return Process(elements.AsSpan(0, count));
    }

    // Applies the variable weighting (shifted: the variable CEs and the primary-ignorable CEs that follow them are
    // ignored) and the reordering of the primaries; removes the completely ignorable CEs
    private int Process(Span<ulong> elements)
    {
        var count = 0;
        var afterVariable = false;
        var shifted = _settings.Shifted;
        var reorder = _reorder;
        foreach (var element in elements)
        {
            var ce = element;
            var primary = CollationWeights.Primary(ce);
            if (shifted)
            {
                if (primary == 0)
                {
                    if (afterVariable)
                        continue;
                }
                else if (primary <= _variableTop && primary >= _firstVariable)
                {
                    afterVariable = true;
                    continue;
                }
                else
                {
                    afterVariable = false;
                }
            }

            if (ce == 0)
                continue;

            if (reorder is not null && primary != 0)
            {
                ce = (ce & 0x0000FFFF_FFFFFFFF) | ((ulong)reorder[primary >> 16] << 48);
            }

            elements[count++] = ce;
        }

        return count;
    }

    private int CompareElements(ReadOnlySpan<ulong> x, ReadOnlySpan<ulong> y, bool comparePrimaries)
    {
        // Primary level
        int i = 0, j = 0;
        while (comparePrimaries)
        {
            uint px = 0, py = 0;
            while (i < x.Length && (px = CollationWeights.Primary(x[i++])) == 0)
            {
            }

            while (j < y.Length && (py = CollationWeights.Primary(y[j++])) == 0)
            {
            }

            if (px != py)
                return px < py ? -1 : 1;

            if (px == 0)
                break;
        }

        // Secondary level
        if (_settings.Strength >= 2)
        {
            var result = _backwards ? CompareSecondariesBackwards(x, y) : CompareSecondaries(x, y);
            if (result != 0)
                return result;
        }

        // Case level: the case bits of the CEs with a primary weight
        if (_settings.CaseLevel)
        {
            i = 0;
            j = 0;
            var upperFirst = _settings.CaseFirst == CollationCaseFirst.Upper;
            while (true)
            {
                int cx = -1, cy = -1;
                while (i < x.Length)
                {
                    var ce = x[i++];
                    if (CollationWeights.Primary(ce) != 0)
                    {
                        cx = CollationWeights.Case(ce);
                        break;
                    }
                }

                while (j < y.Length)
                {
                    var ce = y[j++];
                    if (CollationWeights.Primary(ce) != 0)
                    {
                        cy = CollationWeights.Case(ce);
                        break;
                    }
                }

                if (cx != cy)
                {
                    if (cx < 0 || cy < 0)
                        return cx < cy ? -1 : 1;

                    return (cx < cy) ^ upperFirst ? -1 : 1;
                }

                if (cx < 0)
                    break;
            }
        }

        // Tertiary level
        if (_settings.Strength >= 3)
        {
            i = 0;
            j = 0;
            while (true)
            {
                uint tx = 0, ty = 0;
                while (i < x.Length && (tx = GetTertiary(x[i++])) == 0)
                {
                }

                while (j < y.Length && (ty = GetTertiary(y[j++])) == 0)
                {
                }

                if (tx != ty)
                    return tx < ty ? -1 : 1;

                if (tx == 0)
                    break;
            }
        }

        return 0;
    }

    private uint GetTertiary(ulong ce)
    {
        var tertiary = (uint)ce & CollationWeights.TertiaryMask;
        if (tertiary == 0 || _settings.CaseLevel || _settings.CaseFirst == CollationCaseFirst.Off)
            return tertiary;

        if (CollationWeights.Primary(ce) == 0)
            return tertiary;

        var withCase = (uint)ce & 0xFFFF;
        return _settings.CaseFirst == CollationCaseFirst.Upper ? withCase ^ 0xC000 : withCase;
    }

    private static int CompareSecondaries(ReadOnlySpan<ulong> x, ReadOnlySpan<ulong> y)
    {
        int i = 0, j = 0;
        while (true)
        {
            uint sx = 0, sy = 0;
            while (i < x.Length && (sx = CollationWeights.Secondary(x[i++])) == 0)
            {
            }

            while (j < y.Length && (sy = CollationWeights.Secondary(y[j++])) == 0)
            {
            }

            if (sx != sy)
                return sx < sy ? -1 : 1;

            if (sx == 0)
                return 0;
        }
    }

    private static int CompareSecondariesBackwards(ReadOnlySpan<ulong> x, ReadOnlySpan<ulong> y)
    {
        int i = x.Length, j = y.Length;
        while (true)
        {
            uint sx = 0, sy = 0;
            while (i > 0 && (sx = CollationWeights.Secondary(x[--i])) == 0)
            {
            }

            while (j > 0 && (sy = CollationWeights.Secondary(y[--j])) == 0)
            {
            }

            if (sx != sy)
                return sx < sy ? -1 : 1;

            if (sx == 0)
                return 0;
        }
    }

    private sealed class Buffers
    {
        public int[] XCodePoints = new int[64];
        public int[] YCodePoints = new int[64];
        public ulong[] XElements = new ulong[64];
        public ulong[] YElements = new ulong[64];
    }
}
