using System.Runtime.CompilerServices;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// Canonicalize (https://tc39.es/ecma262/#sec-runtime-semantics-canonicalize-ch), and the sets of characters that
/// have the same canonical value, which make a character class match case-insensitively.
/// </summary>
internal static class RegExpCaseFolding
{
    private static char[]? s_nonUnicodeCanonical;
    private static EquivalenceClasses? s_nonUnicodeClasses;
    private static EquivalenceClasses? s_unicodeClasses;

    /// <summary>Canonicalize for a pattern without the "u" or "v" flag: the toUppercase of the code unit, when it is one code unit and does not map a non-ASCII character to an ASCII one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static char CanonicalizeNonUnicode(char value) => (s_nonUnicodeCanonical ?? CreateNonUnicodeTable())[value];

    /// <summary>Canonicalize for a pattern with the "u" or "v" flag: the simple case folding.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int CanonicalizeUnicode(int codePoint)
    {
        if (codePoint < 128)
            return codePoint is >= 'A' and <= 'Z' ? codePoint + 32 : codePoint;

        return UnicodeProperties.ToSimpleCaseFold(codePoint);
    }

    public static int Canonicalize(int value, bool unicode) => unicode ? CanonicalizeUnicode(value) : CanonicalizeNonUnicode((char)value);

    /// <summary>
    /// Gets the characters c such that Canonicalize(c) is Canonicalize(a) for some character a of the set: the
    /// characters CharacterSetMatcher matches when the pattern ignores case.
    /// </summary>
    public static CodePointSet AddCaseEquivalents(CodePointSet set, bool unicode)
    {
        var classes = unicode ? s_unicodeClasses ?? CreateUnicodeClasses() : s_nonUnicodeClasses ?? CreateNonUnicodeClasses();
        var members = classes.Members;
        var result = set.Clone();
        var marked = new bool[classes.Classes.Length];
        var any = false;

        // Both lists are sorted: walk the members and the ranges together
        var rangeIndex = 0;
        var rangeCount = set.RangeCount;
        for (var i = 0; i < members.Length && rangeIndex < rangeCount; i++)
        {
            var member = members[i];
            while (rangeIndex < rangeCount && set.GetRangeEnd(rangeIndex) < member)
            {
                rangeIndex++;
            }

            if (rangeIndex < rangeCount && set.GetRangeStart(rangeIndex) <= member)
            {
                marked[classes.ClassOfMember[i]] = true;
                any = true;
            }
        }

        if (!any)
            return result;

        for (var i = 0; i < marked.Length; i++)
        {
            if (marked[i])
            {
                foreach (var value in classes.Classes[i])
                {
                    result.Add(value);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the code points that have a simple case folding mapping (scf(c) is not c), and the code point each one
    /// folds to, sorted by code point.
    /// </summary>
    public static (int[] CodePoints, int[] Folded) GetSimpleCaseFoldings()
    {
        var classes = s_unicodeClasses ?? CreateUnicodeClasses();
        return (classes.FoldedCodePoints, classes.FoldedValues);
    }

    private static char[] CreateNonUnicodeTable()
    {
        var table = new char[0x10000];
        Span<int> mapping = stackalloc int[UnicodeProperties.MaxCaseMappingLength];
        for (var c = 0; c < table.Length; c++)
        {
            var result = c;
            if (c is < 0xD800 or > 0xDFFF)
            {
                var length = UnicodeProperties.GetUppercaseMapping(c, mapping);

                // The mapping must be a single code unit, and must not map a non-ASCII character to an ASCII one
                if (length == 1 && mapping[0] <= 0xFFFF && !(c >= 128 && mapping[0] < 128))
                {
                    result = mapping[0];
                }
            }

            table[c] = (char)result;
        }

        Volatile.Write(ref s_nonUnicodeCanonical, table);
        return table;
    }

    private static EquivalenceClasses CreateNonUnicodeClasses()
    {
        var groups = new Dictionary<int, List<int>>();
        for (var c = 0; c <= 0xFFFF; c++)
        {
            var canonical = CanonicalizeNonUnicode((char)c);
            if (canonical != c)
            {
                AddToGroup(groups, canonical, c, includeCanonical: CanonicalizeNonUnicode((char)canonical) == canonical);
            }
        }

        var result = new EquivalenceClasses(groups, [], []);
        Volatile.Write(ref s_nonUnicodeClasses, result);
        return result;
    }

    private static EquivalenceClasses CreateUnicodeClasses()
    {
        var groups = new Dictionary<int, List<int>>();
        var folded = new List<int>();
        var foldedValues = new List<int>();
        for (var c = 0; c <= CodePointSet.MaxCodePoint; c++)
        {
            var canonical = UnicodeProperties.ToSimpleCaseFold(c);
            if (canonical != c)
            {
                AddToGroup(groups, canonical, c, includeCanonical: UnicodeProperties.ToSimpleCaseFold(canonical) == canonical);
                folded.Add(c);
                foldedValues.Add(canonical);
            }
        }

        var result = new EquivalenceClasses(groups, [.. folded], [.. foldedValues]);
        Volatile.Write(ref s_unicodeClasses, result);
        return result;
    }

    private static void AddToGroup(Dictionary<int, List<int>> groups, int canonical, int value, bool includeCanonical)
    {
        if (!groups.TryGetValue(canonical, out var list))
        {
            list = includeCanonical ? [canonical] : [];
            groups.Add(canonical, list);
        }

        list.Add(value);
    }

    private sealed class EquivalenceClasses
    {
        public EquivalenceClasses(Dictionary<int, List<int>> groups, int[] foldedCodePoints, int[] foldedValues)
        {
            Classes = new int[groups.Count][];
            var members = new List<(int Value, int Class)>();
            var index = 0;
            foreach (var group in groups.Values)
            {
                Classes[index] = [.. group];
                foreach (var value in group)
                {
                    members.Add((value, index));
                }

                index++;
            }

            members.Sort((a, b) => a.Value.CompareTo(b.Value));
            Members = new int[members.Count];
            ClassOfMember = new int[members.Count];
            for (var i = 0; i < members.Count; i++)
            {
                Members[i] = members[i].Value;
                ClassOfMember[i] = members[i].Class;
            }

            FoldedCodePoints = foldedCodePoints;
            FoldedValues = foldedValues;
        }

        /// <summary>The sets of characters that have the same canonical value, when there are at least two of them.</summary>
        public int[][] Classes { get; }

        /// <summary>The characters of the classes, sorted.</summary>
        public int[] Members { get; }

        /// <summary>The index of the class of each member.</summary>
        public int[] ClassOfMember { get; }

        public int[] FoldedCodePoints { get; }

        public int[] FoldedValues { get; }
    }
}
