using System.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// What a match must start with, to skip the positions where it cannot: a literal prefix, the possible first code
/// units, or the start of the input.
/// </summary>
internal sealed class RegExpSearchInfo
{
    private RegExpSearchInfo(string? prefix, RegExpCharClass? firstCharacters, bool anchoredAtStart)
    {
        Prefix = prefix;
        FirstCharacters = firstCharacters;
        AnchoredAtStart = anchoredAtStart;
    }

    /// <summary>Gets the case-sensitive literal every match starts with, if any.</summary>
    public string? Prefix { get; }

    /// <summary>Gets the code units a match can start with (the first code unit of a character), or null when any.</summary>
    public RegExpCharClass? FirstCharacters { get; }

    /// <summary>Gets whether a match can only start at the start of the input (<c>^</c> without the multiline flag).</summary>
    public bool AnchoredAtStart { get; }

    public static RegExpSearchInfo Create(RegExpNode root, bool unicode)
    {
        var anchored = IsAnchored(root);
        var prefix = GetPrefix(root, unicode);
        RegExpCharClass? first = null;
        if (prefix is null && !anchored)
        {
            var (set, canBeEmpty) = GetFirstCharacters(root, unicode, depth: 0);
            if (!canBeEmpty && set is not null)
            {
                first = new RegExpCharClass(set);
            }
        }

        return new RegExpSearchInfo(prefix, first, anchored);
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

    private static string? GetPrefix(RegExpNode root, bool unicode)
    {
        var items = root is RegExpSequenceNode sequence ? sequence.Items : [root];
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            if (item is not RegExpCharNode { IgnoreCase: false } character)
                break;

            var value = character.Value;
            if (unicode && value is >= 0xD800 and <= 0xDFFF)
                break;

            if (value > 0xFFFF)
            {
                builder.Append(char.ConvertFromUtf32(value));
            }
            else
            {
                builder.Append((char)value);
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    /// <summary>Gets the first code units of the matches of a node (null for any), and whether the node can match the empty string.</summary>
    private static (CodePointSet? Set, bool CanBeEmpty) GetFirstCharacters(RegExpNode node, bool unicode, int depth)
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
    private static CodePointSet ToCodeUnits(CodePointSet set)
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
