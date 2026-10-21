using System.Collections.Concurrent;
using System.Globalization;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;
using Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// The CLDR root collation converted to the weights of <see cref="CollationWeights"/>: the mappings, the sorted root
/// weights used to build the tailorings (the "root elements" of ICU), the variable range, the script groups of the
/// primary weights (for <c>[reorder]</c>), and a fast NFD.
/// </summary>
internal sealed class RootCollation
{
    /// <summary>The first primary of the Han group: <c>&amp;[last regular]</c> tailors after it, before every implicit weight of Han.</summary>
    public const uint HanStartPrimary = 0xFB100000;

    /// <summary>The first lead (high 16 bits of a primary) of the unassigned implicit weights and the trailing weights, which are never reordered.</summary>
    private const int FirstFixedHighLead = 0xFBC0;

    private static RootCollation? s_instance;
    private static readonly ConcurrentDictionary<string, ushort[]?> ReorderTables = new(StringComparer.Ordinal);

    private readonly int _firstVariableLead;
    private readonly int _maxVariableLead;

    // Computed on first use: the sorted root weights (for the builder), the script groups (for [reorder]), and whether
    // the BMP characters are "simple" (no decomposition, combining class 0), by blocks of 64 characters
    private RootElements? _elements;
    private (int StartLead, string Group)[]? _runs;
    private readonly ulong[] _simpleBmp = new ulong[0x10000 / 64];
    private readonly ulong[] _simpleBmpComputed = new ulong[0x10000 / 64 / 64];

    private RootCollation(CollationRootTable root)
    {
        Table = root;
        Map = new CollationMap();
        var maxVariable = 0;
        var firstVariable = int.MaxValue;
        var buffer = new List<ulong>();
        foreach (var (codePoints, elements) in root.GetMappings())
        {
            buffer.Clear();
            for (var i = 0; i < elements.Length; i++)
            {
                var element = elements[i];
                if (element.IsVariable)
                {
                    maxVariable = Math.Max(maxVariable, element.Primary);
                    firstVariable = Math.Min(firstVariable, element.Primary);
                }

                var primary = (uint)element.Primary << 16;
                if (element.Primary is >= 0xFB00 and <= 0xFBFF && i + 1 < elements.Length && elements[i + 1] is { Primary: not 0, Secondary: 0, Tertiary: 0 } next)
                {
                    primary |= (uint)next.Primary;
                    i++;
                }

                buffer.Add(CollationWeights.MakeCE(primary, CollationWeights.ScaleSecondary(element.Secondary), CollationWeights.ScaleTertiary(element.Tertiary) | (CollationWeights.IsUppercaseTertiary(element.Tertiary) && element.Primary != 0 ? (uint)CollationWeights.UpperCase : 0)));
            }

            var ces = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(buffer);
            if (codePoints.Length == 1)
            {
                Map.SetMapping(codePoints[0], ces);
            }
            else
            {
                Map.SetContraction(codePoints, ces);
            }
        }

        Map.Trim();
        _firstVariableLead = firstVariable;
        _maxVariableLead = maxVariable;
        VariableTop = ((uint)maxVariable << 16) | 0xFFFF;
        FirstVariablePrimary = (uint)firstVariable << 16;
        DigitZeroPrimary = CollationWeights.Primary(Map.GetElements(Map.GetEntry('0'))[0]);
    }

    /// <summary>The sorted weights of the root collation (the "root elements" of ICU).</summary>
    private sealed class RootElements
    {
        public RootElements(ReadOnlySpan<ulong> rootElements)
        {
            // The distinct (primary, secondary, tertiary) of the root CEs, sorted
            var keys = new ulong[rootElements.Length + 1];
            for (var i = 0; i < rootElements.Length; i++)
            {
                keys[i] = rootElements[i] & ~CollationWeights.CaseMask;
            }

            keys[^1] = CollationWeights.MakeCE(HanStartPrimary);
            Array.Sort(keys);
            var primaries = new List<uint>();
            var starts = new List<int>();
            var secTers = new List<uint>();
            var ignorable = new List<uint>();
            ulong previous = ulong.MaxValue;
            foreach (var key in keys)
            {
                if (key == previous)
                    continue;

                previous = key;
                var primary = CollationWeights.Primary(key);
                var secTer = (uint)key;
                if (primary == 0)
                {
                    if (secTer != 0)
                    {
                        ignorable.Add(secTer);
                    }

                    continue;
                }

                if (primaries.Count == 0 || primaries[^1] != primary)
                {
                    primaries.Add(primary);
                    starts.Add(secTers.Count);
                }

                secTers.Add(secTer);
            }

            starts.Add(secTers.Count);
            Primaries = [.. primaries];
            SecTerStarts = [.. starts];
            SecTers = [.. secTers];
            IgnorableSecTers = [.. ignorable];
        }

        public uint[] Primaries { get; }

        public int[] SecTerStarts { get; }

        public uint[] SecTers { get; }

        public uint[] IgnorableSecTers { get; }
    }

    private RootElements Elements => Volatile.Read(ref _elements) ?? CldrData.InitializeOnce(ref _elements, new RootElements(Map.GetElements(0, Map.ElementCount)));

    private (int StartLead, string Group)[] Runs => Volatile.Read(ref _runs) ?? CldrData.InitializeOnce(ref _runs, ComputeRuns());

    private (int StartLead, string Group)[] ComputeRuns()
    {
        // The script groups: each lead of the reorderable range belongs to the group of its first character; the
        // implicit weights belong to their scripts (Han, Tangut, Nushu, Khitan)
        var leadGroups = new SortedDictionary<int, string?>();
        foreach (var (codePoints, elements) in Table.GetMappings())
        {
            var first = elements[0];
            if (codePoints.Length == 1 && first.Primary != 0 && first.Primary < 0xFB00 && !leadGroups.ContainsKey(first.Primary))
            {
                leadGroups[first.Primary] = GetGroup(codePoints[0], first.IsVariable);
            }
        }

        leadGroups[(int)(HanStartPrimary >> 16)] = "Hani";
        foreach (var (start, @base) in GetSiniformLeads(Table))
        {
            leadGroups[@base] = NormalizeGroup(UnicodeScripts.GetIso15924Code(UnicodeScripts.GetScript(start)));
        }

        // The special groups are ranges at the start of the reorderable leads, in the order space, punct, symbol,
        // currency, digit (the characters of a script in these ranges, such as U+0950 OM, belong to the range); the
        // letters of the scripts come after them (the special characters of a script, such as its digits, belong to the script)
        var firstVariable = _firstVariableLead;
        var maxVariable = _maxVariableLead;
        var punctStart = FirstLead(leadGroups, firstVariable, "punct");
        var currencyStart = FirstLead(leadGroups, maxVariable + 1, "currency");
        var digitStart = FirstLead(leadGroups, currencyStart, "digit");
        var scriptStart = digitStart;
        foreach (var (lead, group) in leadGroups)
        {
            if (lead > digitStart && group is not (null or "digit" or "symbol" or "currency" or "punct" or "space"))
            {
                scriptStart = lead;
                break;
            }
        }

        var runs = new List<(int, string)>();
        string? current = null;
        foreach (var (lead, rawGroup) in leadGroups)
        {
            if (lead < firstVariable || lead >= FirstFixedHighLead)
                continue;

            var group = lead switch
            {
                _ when lead < punctStart => "space",
                _ when lead <= maxVariable => "punct",
                _ when lead < currencyStart => "symbol",
                _ when lead < digitStart => "currency",
                _ when lead < scriptStart => "digit",
                _ => rawGroup is "digit" or "symbol" or "currency" or "punct" or "space" ? null : rawGroup,
            };

            var effective = group ?? current ?? "Zyyy";
            if (effective != current)
            {
                runs.Add((lead, effective));
                current = effective;
            }
        }

        return [.. runs];
    }

    public static RootCollation Instance => Volatile.Read(ref s_instance) ?? CldrData.InitializeOnce(ref s_instance, new RootCollation(CldrData.Default.Collation.Root));

    /// <summary>Gets the root table of the data (allkeys_CLDR.txt).</summary>
    public CollationRootTable Table { get; }

    /// <summary>Gets the mappings of the root collation.</summary>
    public CollationMap Map { get; }

    /// <summary>Gets the highest primary weight of the variable range (maxVariable punct), including the weights tailored after it.</summary>
    public uint VariableTop { get; }

    /// <summary>Gets the first primary weight of the variable range.</summary>
    public uint FirstVariablePrimary { get; }

    /// <summary>Gets the primary of the digit zero: the digits 0 to 9 have consecutive primaries, shared by the decimal digits of every script.</summary>
    public uint DigitZeroPrimary { get; }

    /// <summary>Gets the script groups of the primary leads (the first lead of each run of leads of a group), in the root order.</summary>
    internal IReadOnlyList<(int StartLead, string Group)> ScriptRuns => Runs;

    /// <summary>Gets the CE of a code point that the root table does not list (the implicit weights of UCA).</summary>
    public ulong GetImplicitCE(int codePoint)
    {
        if (codePoint is >= 0x4E00 and <= 0x9FFF)
        {
            // The core block of the unified ideographs (UCA 10.1.3): AAAA = FB40 + (cp >> 15), BBBB = (cp & 0x7FFF) | 0x8000
            return CollationWeights.MakeCE(((uint)(0xFB40 + (codePoint >> 15)) << 16) | (uint)((codePoint & 0x7FFF) | 0x8000));
        }

        var (first, second) = Table.GetImplicitElements(codePoint);
        return CollationWeights.MakeCE(((uint)first.Primary << 16) | (uint)second.Primary);
    }

    /// <summary>Gets the decimal value of a digit (gc=Nd), or -1.</summary>
    public int GetDigitValue(int codePoint)
    {
        if ((uint)(codePoint - '0') <= 9)
            return codePoint - '0';

        if (codePoint < 0x660)
            return -1;

        var entry = Map.GetEntry(codePoint);
        if ((entry & CollationMap.CountMask) != 1)
            return -1;

        var lead = (int)(CollationWeights.Primary(Map.GetElements(entry)[0]) >> 16) - (int)(DigitZeroPrimary >> 16);
        if ((uint)lead > 9 || CollationWeights.Primary(Map.GetElements(entry)[0]) << 16 != 0)
            return -1;

        return UnicodeProperties.GetGeneralCategory(codePoint) == UnicodeCategory.DecimalDigitNumber ? lead : -1;
    }

    // The root elements (sorted weights of the root collation), used by the collation builder

    /// <summary>Gets the index of a root primary in the sorted primaries, or a negative value.</summary>
    public int FindPrimary(uint primary) => Array.BinarySearch(Elements.Primaries, primary);

    /// <summary>Gets the first root primary after a primary, or the boundary.</summary>
    public uint GetPrimaryAfter(uint primary)
    {
        var primaries = Elements.Primaries;
        var index = Array.BinarySearch(primaries, primary);
        index = index < 0 ? ~index : index + 1;
        var next = index < primaries.Length ? primaries[index] : CollationWeights.PrimaryBoundary;
        if (IsImplicitPrimary(primary) && primary + 1 < next)
            return primary + 1;

        return next;
    }

    /// <summary>Gets the last root primary before a primary (0 for the first one).</summary>
    public uint GetPrimaryBefore(uint primary)
    {
        if (IsImplicitPrimary(primary) && FindPrimary(primary - 1) < 0 && !IsLeadOnly(primary - 1))
            return primary - 1;

        var primaries = Elements.Primaries;
        var index = Array.BinarySearch(primaries, primary);
        index = index < 0 ? ~index : index;
        return index > 0 ? primaries[index - 1] : 0;
    }

    /// <summary>Gets the secondary and tertiary weights (<c>s &lt;&lt; 16 | t</c>) of the root CEs of a primary (sorted).</summary>
    public ReadOnlySpan<uint> GetSecondariesAndTertiaries(uint primary)
    {
        var elements = Elements;
        if (primary == 0)
            return elements.IgnorableSecTers;

        var index = FindPrimary(primary);
        if (index < 0)
            return ImplicitSecTers;

        return elements.SecTers.AsSpan(elements.SecTerStarts[index], elements.SecTerStarts[index + 1] - elements.SecTerStarts[index]);
    }

    /// <summary>Gets the first root secondary weight after a secondary weight for a primary, or the boundary.</summary>
    public uint GetSecondaryAfter(uint primary, uint secondary)
    {
        foreach (var secTer in GetSecondariesAndTertiaries(primary))
        {
            if (secTer >> 16 > secondary)
                return secTer >> 16;
        }

        return CollationWeights.SecondaryBoundary;
    }

    /// <summary>Gets the first root tertiary weight after a tertiary weight for a primary and a secondary, or the boundary.</summary>
    public uint GetTertiaryAfter(uint primary, uint secondary, uint tertiary)
    {
        foreach (var secTer in GetSecondariesAndTertiaries(primary))
        {
            if (secTer >> 16 == secondary && (secTer & 0xFFFF) > tertiary)
                return secTer & 0xFFFF;
        }

        return CollationWeights.TertiaryBoundary;
    }

    /// <summary>Gets the root secondary weight before a secondary weight for a primary.</summary>
    public uint GetSecondaryBefore(uint primary, uint secondary)
    {
        var previous = primary == 0 ? 0 : CollationWeights.BeforeWeight16;
        foreach (var secTer in GetSecondariesAndTertiaries(primary))
        {
            var s = secTer >> 16;
            if (s >= secondary)
                break;

            previous = s;
        }

        return previous;
    }

    /// <summary>Gets the root tertiary weight before a tertiary weight for a primary and a secondary.</summary>
    public uint GetTertiaryBefore(uint primary, uint secondary, uint tertiary)
    {
        var previous = primary == 0 && secondary == 0 ? 0 : CollationWeights.BeforeWeight16;
        var target = (secondary << 16) | tertiary;
        foreach (var secTer in GetSecondariesAndTertiaries(primary))
        {
            if (secTer >= target)
                break;

            if (secTer >> 16 == secondary)
            {
                previous = secTer & 0xFFFF;
            }
        }

        return previous;
    }

    /// <summary>Gets the CE of the lowest secondary weight of the primary-ignorable CEs (<c>[first primary ignorable]</c>).</summary>
    public ulong FirstSecondaryCE => Elements.IgnorableSecTers is [var first, ..] && first >> 16 != 0 ? first : 0;

    /// <summary>Gets the CE of the highest secondary weight of the primary-ignorable CEs (<c>[last primary ignorable]</c>).</summary>
    public ulong LastSecondaryCE => Elements.IgnorableSecTers is [.., var last] ? last : 0;

    /// <summary>Gets the first CE of the regular range (after the variable range).</summary>
    public ulong FirstRegularCE
    {
        get
        {
            var primaries = Elements.Primaries;
            var index = Array.BinarySearch(primaries, VariableTop);
            index = index < 0 ? ~index : index + 1;
            return CollationWeights.MakeCE(primaries[index]);
        }
    }

    /// <summary>Gets the last CE of the variable range.</summary>
    public ulong LastVariableCE
    {
        get
        {
            var primaries = Elements.Primaries;
            var index = Array.BinarySearch(primaries, VariableTop);
            index = index < 0 ? ~index : index + 1;
            return CollationWeights.MakeCE(primaries[index - 1]);
        }
    }

    /// <summary>Gets the first primary of the script group of a code point (the CE of the special contractions <c>U+FDD1 x</c> of ICU), or 0.</summary>
    public uint GetGroupFirstPrimary(int codePoint)
    {
        var entry = Map.GetEntry(codePoint);
        var primary = (entry & CollationMap.CountMask) != 0 ? CollationWeights.Primary(Map.GetElements(entry)[0]) : CollationWeights.Primary(GetImplicitCE(codePoint));
        var runIndex = FindRun((int)(primary >> 16));
        if (runIndex < 0)
            return 0;

        var runs = Runs;
        var group = runs[runIndex].Group;
        foreach (var run in runs)
        {
            if (run.Group == group)
                return (uint)run.StartLead << 16;
        }

        return 0;
    }

    /// <summary>
    /// Gets the permutation of the primary leads (high 16 bits) of a <c>[reorder]</c> list of script codes and the
    /// special groups (space, punct, symbol, currency, digit, others), as ICU does; null when it changes nothing.
    /// </summary>
    public ushort[]? GetReorderTable(IReadOnlyList<string> codes)
    {
        if (codes.Count == 0)
            return null;

        return ReorderTables.GetOrAdd(string.Join(' ', codes), _ => ComputeReorderTable(codes));
    }

    private ushort[]? ComputeReorderTable(IReadOnlyList<string> codes)
    {
        // The groups in their default order
        var runs = Runs;
        var defaultOrder = new List<string>();
        foreach (var (_, group) in runs)
        {
            if (!defaultOrder.Contains(group))
            {
                defaultOrder.Add(group);
            }
        }

        string[] specials = ["space", "punct", "symbol", "currency", "digit"];
        var normalized = new List<string>();
        foreach (var code in codes)
        {
            var group = NormalizeGroup(code);
            if (!normalized.Contains(group))
            {
                normalized.Add(group);
            }
        }

        var low = new List<string>();
        var high = new List<string>();
        foreach (var special in specials)
        {
            if (!normalized.Contains(special))
            {
                low.Add(special);
            }
        }

        var afterOthers = false;
        foreach (var group in normalized)
        {
            if (group is "Zzzz")
            {
                afterOthers = true;
                continue;
            }

            if (!defaultOrder.Contains(group))
                continue;

            (afterOthers ? high : low).Add(group);
        }

        var order = new List<string>(low);
        foreach (var group in defaultOrder)
        {
            if (!low.Contains(group) && !high.Contains(group))
            {
                order.Add(group);
            }
        }

        order.AddRange(high);
        var table = new ushort[0x10000];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = (ushort)i;
        }

        var nextLead = runs[0].StartLead;
        var changed = false;
        foreach (var group in order)
        {
            for (var i = 0; i < runs.Length; i++)
            {
                if (runs[i].Group != group)
                    continue;

                var start = runs[i].StartLead;
                var end = i + 1 < runs.Length ? runs[i + 1].StartLead : FirstFixedHighLead;
                for (var lead = start; lead < end; lead++)
                {
                    changed |= nextLead != lead;
                    table[lead] = (ushort)nextLead++;
                }
            }
        }

        return changed ? table : null;
    }

    private int FindRun(int lead)
    {
        var runs = Runs;
        if (runs.Length == 0 || lead < runs[0].StartLead || lead >= FirstFixedHighLead)
            return -1;

        var low = 0;
        var high = runs.Length - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (runs[middle].StartLead <= lead)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private static int FirstLead(SortedDictionary<int, string?> leadGroups, int minimum, string group)
    {
        foreach (var (lead, value) in leadGroups)
        {
            if (lead >= minimum && value == group)
                return lead;
        }

        return minimum;
    }

    private static readonly uint[] ImplicitSecTers = [(uint)(CollationWeights.CommonSecondaryAndTertiary & 0xFFFFFFFF)];

    private static bool IsImplicitPrimary(uint primary) => primary >> 16 is >= 0xFB00 and < 0xFC00 && (primary & 0xFFFF) != 0;

    private static bool IsLeadOnly(uint primary) => (primary & 0xFFFF) == 0;

    private static string? GetGroup(int codePoint, bool isVariable)
    {
        var category = UnicodeProperties.GetGeneralCategory(codePoint);
        if (isVariable)
            return category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Control ? "space" : "punct";

        switch (category)
        {
            case UnicodeCategory.CurrencySymbol:
                return "currency";
            case UnicodeCategory.MathSymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol:
                return "symbol";
            case UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber:
                return "digit";
        }

        var script = UnicodeScripts.GetScript(codePoint);
        var code = UnicodeScripts.GetIso15924Code(script);
        return code is "Zyyy" or "Zinh" or "Zzzz" ? null : NormalizeGroup(code);
    }

    private static string NormalizeGroup(string code)
    {
        if (code.Length == 4)
        {
            code = string.Concat(char.ToUpperInvariant(code[0]).ToString(), code[1..].ToLowerInvariant());
        }
        else
        {
            code = code.ToLowerInvariant();
        }

        return code switch
        {
            "Hira" or "Kana" or "Hrkt" => "Hrkt",
            "Hans" or "Hant" => "Hani",
            "others" => "Zzzz",
            _ => code,
        };
    }

    private static List<(int Start, int Base)> GetSiniformLeads(CollationRootTable root)
    {
        var result = new List<(int, int)>();
        foreach (var start in (int[])[0x17000, 0x18800, 0x1B170, 0x18B00, 0x18D00])
        {
            var (first, _) = root.GetImplicitElements(start);
            if (first.Primary < 0xFB40)
            {
                result.Add((start, first.Primary));
            }
        }

        return result;
    }

    // NFD

    /// <summary>Gets whether a UTF-16 code unit is a BMP code point without decomposition and with the combining class 0.</summary>
    public bool IsSimple(char c)
    {
        var block = c >> 6;
        if ((Volatile.Read(ref _simpleBmpComputed[block >> 6]) & (1UL << (block & 63))) == 0)
        {
            ComputeSimpleBlock(block);
        }

        return (_simpleBmp[block] & (1UL << (c & 63))) != 0;
    }

    private void ComputeSimpleBlock(int block)
    {
        ulong bits = 0;
        Span<int> buffer = stackalloc int[UnicodeNormalization.MaxCanonicalDecompositionLength];
        for (var i = 0; i < 64; i++)
        {
            var c = (block << 6) | i;
            if (c is >= 0xD800 and <= 0xDFFF)
                continue;

            if (c < 0xC0 || (UnicodeProperties.GetCanonicalCombiningClass(c) == 0 && UnicodeNormalization.GetCanonicalDecomposition(c, buffer) == 1))
            {
                bits |= 1UL << i;
            }
        }

        Volatile.Write(ref _simpleBmp[block], bits);
        Interlocked.Or(ref _simpleBmpComputed[block >> 6], 1UL << (block & 63));
    }

    /// <summary>
    /// Writes the canonical decomposition (NFD) of a string to a buffer of code points (grown when needed); a lone
    /// surrogate is kept as a code point.
    /// </summary>
    public int Decompose(ReadOnlySpan<char> text, ref int[] buffer)
    {
        var count = 0;
        Span<int> decomposition = stackalloc int[UnicodeNormalization.MaxCanonicalDecompositionLength];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (count + UnicodeNormalization.MaxCanonicalDecompositionLength > buffer.Length)
            {
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, count + 16));
            }

            if (IsSimple(c))
            {
                buffer[count++] = c;
                continue;
            }

            int codePoint = c;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(c, text[i + 1]);
                i++;
            }

            int length;
            if (codePoint <= 0xFFFF && char.IsSurrogate((char)codePoint))
            {
                decomposition[0] = codePoint;
                length = 1;
            }
            else
            {
                length = UnicodeNormalization.GetCanonicalDecomposition(codePoint, decomposition);
            }

            foreach (var item in decomposition[..length])
            {
                var combiningClass = item < 0x300 ? 0 : UnicodeProperties.GetCanonicalCombiningClass(item);
                var position = count;
                if (combiningClass != 0)
                {
                    // Canonical ordering: a non-starter moves before the preceding non-starters of a higher class
                    while (position > 0 && buffer[position - 1] >= 0x300 && UnicodeProperties.GetCanonicalCombiningClass(buffer[position - 1]) > combiningClass)
                    {
                        buffer[position] = buffer[position - 1];
                        position--;
                    }
                }

                buffer[position] = item;
                count++;
            }
        }

        return count;
    }

    /// <summary>Gets the canonical decomposition of a string, as an array of code points.</summary>
    public int[] Decompose(string text)
    {
        var buffer = new int[text.Length + 8];
        var count = Decompose(text, ref buffer);
        return buffer.AsSpan(0, count).ToArray();
    }
}
