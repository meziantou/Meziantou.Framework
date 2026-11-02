using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// What a match must start with, to skip the positions where it cannot: a literal prefix, the possible first code
/// units, or the start of the input.
/// </summary>
internal sealed class RegExpSearchInfo
{
    public RegExpSearchInfo(string? prefix, RegExpCharClass? firstCharacters, bool anchoredAtStart, bool prefixIgnoreCase = false)
    {
        Prefix = prefix;
        PrefixIgnoreCase = prefixIgnoreCase;
        FirstCharacters = firstCharacters;
        AnchoredAtStart = anchoredAtStart;
    }

    /// <summary>Gets the literal every match starts with, if any.</summary>
    public string? Prefix { get; }

    /// <summary>
    /// Gets whether <see cref="Prefix"/> is searched with <see cref="StringComparison.OrdinalIgnoreCase"/>: it is ASCII,
    /// and its case-insensitive characters have no non-ASCII case equivalent. The search finds a superset of the
    /// positions where a match can start, and the matcher checks each of them.
    /// </summary>
    public bool PrefixIgnoreCase { get; }

    /// <summary>Gets the code units a match can start with (the first code unit of a character), or null when any.</summary>
    public RegExpCharClass? FirstCharacters { get; }

    /// <summary>Gets whether a match can only start at the start of the input (<c>^</c> without the multiline flag).</summary>
    public bool AnchoredAtStart { get; }

    /// <summary>
    /// Gets whether the program starts with an unbounded greedy loop of one character, and has no backreference: when the
    /// match fails at a position, it fails at every position of the run of characters the loop consumed, and at the end
    /// of that run. From a later start in the run, the loop ends at the same position, and gives characters back down to
    /// a later position, so what follows the loop is tried at a subset of the positions already tried, in the same state.
    /// </summary>
    public bool SkipLeadingRun { get; init; }

    public static RegExpSearchInfo Create(RegExpNode root, bool unicode)
    {
        var anchored = IsAnchored(root);
        var (prefix, prefixIgnoreCase) = GetPrefix(root, unicode);
        RegExpCharClass? first = null;
        if (prefix is null && !anchored)
        {
            var (set, canBeEmpty) = GetFirstCharacters(root, unicode, depth: 0);
            if (!canBeEmpty && set is not null)
            {
                first = new RegExpCharClass(set);
            }
        }

        return new RegExpSearchInfo(prefix, first, anchored, prefixIgnoreCase);
    }

    private static bool IsAnchored(RegExpNode node)
    {
        return node switch
        {
            RegExpAssertionNode { Kind: RegExpAssertionKind.Start, Multiline: false } => true,
            RegExpSequenceNode sequence => sequence.Items.Length > 0 && IsAnchored(sequence.Items[0]),
            RegExpAlternationNode alternation => Array.TrueForAll(alternation.Alternatives, IsAnchored),
            RegExpGroupNode group => IsAnchored(group.Body),
            _ => false,
        };
    }

    private static (string? Prefix, bool IgnoreCase) GetPrefix(RegExpNode root, bool unicode)
    {
        var items = root is RegExpSequenceNode sequence ? sequence.Items : [root];
        var builder = new StringBuilder();
        var ignoreCase = false;
        var ascii = true;
        foreach (var item in items)
        {
            if (item is not RegExpCharNode character)
                break;

            var value = character.Value;
            if (unicode && value is >= 0xD800 and <= 0xDFFF)
                break;

            if (character.IgnoreCase)
            {
                // A case-insensitive prefix is searched with OrdinalIgnoreCase, which matches exactly the ASCII case
                // equivalents (K and s also match U+212A and U+017F under "u" or "v": they end the prefix)
                if (!ascii || !HasOnlyAsciiCaseEquivalents(value, unicode))
                    break;

                ignoreCase = true;
            }
            else if (value > 0x7F)
            {
                if (ignoreCase)
                    break;

                ascii = false;
            }

            if (value > 0xFFFF)
            {
                builder.Append(char.ConvertFromUtf32(value));
            }
            else
            {
                builder.Append((char)value);
            }
        }

        // One case-insensitive character is searched as well by the first characters
        if (builder.Length == 0 || (ignoreCase && builder.Length < 2))
            return (null, false);

        return (builder.ToString(), ignoreCase);
    }

    private static bool HasOnlyAsciiCaseEquivalents(int value, bool unicode)
    {
        if (value > 0x7F)
            return false;

        var equivalents = RegExpCaseFolding.AddCaseEquivalents(CodePointSet.Single(value), unicode);
        for (var i = 0; i < equivalents.RangeCount; i++)
        {
            for (var c = equivalents.GetRangeStart(i); c <= equivalents.GetRangeEnd(i); c++)
            {
                if (c > 0x7F || char.ToUpperInvariant((char)c) != char.ToUpperInvariant((char)value))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Gets the first code units of the matches of a node (null for any), and whether the node can match the empty string.</summary>
    public static (CodePointSet? Set, bool CanBeEmpty) GetFirstCharacters(RegExpNode node, bool unicode, int depth)
    {
        if (depth > 64)
            return (null, true);

        switch (node)
        {
            case RegExpEmptyNode or RegExpAssertionNode or RegExpLookaroundNode:
                return (new CodePointSet(), true);

            case RegExpCharNode character:
                {
                    var set = CodePointSet.Single(character.Value);
                    if (character.IgnoreCase)
                    {
                        set = RegExpCaseFolding.AddCaseEquivalents(set, unicode);
                    }

                    return (ToCodeUnits(set), false);
                }

            case RegExpSetNode setNode:
                {
                    var set = setNode.IgnoreCase ? RegExpCaseFolding.AddCaseEquivalents(setNode.Set, unicode) : setNode.Set;
                    if (setNode.Invert)
                    {
                        set = set.Complement(unicode ? CodePointSet.MaxCodePoint : 0xFFFF);
                    }

                    return (ToCodeUnits(set), false);
                }

            case RegExpGroupNode group:
                return GetFirstCharacters(group.Body, unicode, depth + 1);

            case RegExpQuantifierNode quantifier:
                {
                    if (quantifier.Max == 0)
                        return (new CodePointSet(), true);

                    var (set, canBeEmpty) = GetFirstCharacters(quantifier.Body, unicode, depth + 1);
                    return (set, canBeEmpty || quantifier.Min == 0);
                }

            case RegExpSequenceNode sequence:
                {
                    var result = new CodePointSet();
                    foreach (var item in sequence.Items)
                    {
                        var (set, canBeEmpty) = GetFirstCharacters(item, unicode, depth + 1);
                        if (set is null)
                            return (null, false);

                        result.AddSet(set);
                        if (!canBeEmpty)
                            return (result, false);
                    }

                    return (result, true);
                }

            case RegExpAlternationNode alternation:
                {
                    var result = new CodePointSet();
                    var canBeEmpty = false;
                    foreach (var alternative in alternation.Alternatives)
                    {
                        var (set, empty) = GetFirstCharacters(alternative, unicode, depth + 1);
                        if (set is null)
                            return (null, false);

                        result.AddSet(set);
                        canBeEmpty |= empty;
                    }

                    return (result, canBeEmpty);
                }

            default:
                // A backreference or the dot: anything
                return (null, false);
        }
    }

    /// <summary>Replaces the supplementary code points of a set by their high surrogates.</summary>
    public static CodePointSet ToCodeUnits(CodePointSet set)
    {
        var count = set.RangeCount;
        if (count == 0 || set.GetRangeEnd(count - 1) <= 0xFFFF)
            return set;

        var result = new CodePointSet();
        for (var i = 0; i < count; i++)
        {
            var start = set.GetRangeStart(i);
            var end = set.GetRangeEnd(i);
            if (start <= 0xFFFF)
            {
                result.AddRange(start, Math.Min(end, 0xFFFF));
            }

            if (end > 0xFFFF)
            {
                var low = Math.Max(start, 0x10000);
                result.AddRange(0xD800 + ((low - 0x10000) >> 10), 0xD800 + ((end - 0x10000) >> 10));
            }
        }

        return result;
    }
}
