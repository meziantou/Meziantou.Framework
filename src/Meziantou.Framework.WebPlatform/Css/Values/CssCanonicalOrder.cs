using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Css.Syntax;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// Orders the components of a specified value in the canonical order of its grammar
/// (https://drafts.csswg.org/cssom/#serialize-a-css-value): the components of the || and &amp;&amp; groups are ordered as
/// in the grammar, e.g. text-decoration-line: overline underline is underline overline, and box-shadow: 1px 2px red inset
/// is red 1px 2px inset.
/// </summary>
internal static class CssCanonicalOrder
{
    private const int MaxDepth = 32;

    private static readonly ConcurrentDictionary<GrammarNode, bool> UnorderedGroups = new(ReferenceEqualityComparer.Instance);

    /// <summary>The properties whose values keep the specified order, as in the implementations (e.g. hanging-punctuation: last first).</summary>
    private static readonly HashSet<string> SpecifiedOrderProperties = new(StringComparer.Ordinal) { "hanging-punctuation", "image-resolution", "paint-order", "word-space-transform", "grid-lanes-direction" };

    /// <summary>The types whose order is significant (https://drafts.csswg.org/css-anchor-position/#typedef-position-try-fallbacks-try-tactic: the tactics are applied in order).</summary>
    private static readonly HashSet<string> SpecifiedOrderTypes = new(StringComparer.Ordinal) { "try-tactic" };

    /// <summary>Returns the values (whitespace excluded) of the property in the canonical order, or null when the order does not change.</summary>
    public static List<CssComponentValue>? Reorder(CssPropertyDefinition property, GrammarNode grammar, List<CssComponentValue> values)
    {
        if (SpecifiedOrderProperties.Contains(property.Name) || !ContainsUnorderedGroup(grammar))
            return null;

        var result = Canonicalize(grammar, values, type: null, depth: 0);
        if (result.Count != values.Count)
            return null;

        for (var i = 0; i < values.Count; i++)
        {
            if (!ReferenceEquals(result[i], values[i]))
                return result;
        }

        return null;
    }

    /// <summary>Returns whether the grammar contains a || or &amp;&amp; group, in which case the order of the components can change.</summary>
    private static bool ContainsUnorderedGroup(GrammarNode grammar) => UnorderedGroups.GetOrAdd(grammar, static node => ContainsUnorderedGroup(node, new HashSet<string>(StringComparer.Ordinal)));

    private static bool ContainsUnorderedGroup(GrammarNode node, HashSet<string> visited)
    {
        switch (node)
        {
            case GroupNode group:
                return group.Combinator is GrammarCombinator.AnyInAnyOrder or GrammarCombinator.AllInAnyOrder || group.Children.Exists(c => ContainsUnorderedGroup(c, visited));

            case TypeNode type when visited.Add((type.IsProperty ? "'" : "") + type.Name) && GetGrammar(type) is { } grammar:
                return ContainsUnorderedGroup(grammar, visited);

            default:
                return false;
        }
    }

    private static GrammarNode? GetGrammar(TypeNode type)
    {
        if (type.IsProperty)
            return CssPropertyRegistry.Get(type.Name) is { } property ? CssPropertyRegistry.GetGrammar(property) : null;

        return CssPropertyRegistry.GetTypeGrammar(type.Name.EndsWith("()", StringComparison.Ordinal) ? type.Name : "<" + type.Name + ">");
    }

    /// <param name="type">The type whose grammar contains the node (it may exclude keywords from &lt;custom-ident&gt;).</param>
    private static List<CssComponentValue> Canonicalize(GrammarNode node, List<CssComponentValue> values, string? type, int depth)
    {
        if (depth > MaxDepth || values.Count == 0)
            return values;

        // An optional component that is present
        if (node.Min == 0 && node.Max == 1)
            return Single(node) is { } present ? Canonicalize(present, values, type, depth + 1) : values;

        // Repetitions: each comma-separated repetition is ordered (the other repetitions cannot be split reliably)
        if (node.Min != 1 || node.Max != 1)
        {
            if (!node.CommaSeparated || Single(node) is not { } single)
                return values;

            var result = new List<CssComponentValue>(values.Count);
            var start = 0;
            for (var i = 0; i <= values.Count; i++)
            {
                if (i < values.Count && values[i] is not CssPreservedToken { Type: CssTokenType.Comma })
                    continue;

                if (start > 0)
                {
                    result.Add(values[start - 1]);
                }

                result.AddRange(Canonicalize(single, values[start..i], type, depth + 1));
                start = i + 1;
            }

            return result;
        }

        switch (node)
        {
            case TypeNode typeNode when !SpecifiedOrderTypes.Contains(typeNode.Name) && GetGrammar(typeNode) is { } grammar && ContainsUnorderedGroup(grammar):
                return Canonicalize(grammar, values, typeNode.IsProperty ? null : typeNode.Name, depth + 1);

            case GroupNode group:
            {
                var captures = new List<GrammarCapture>();
                if (!ValueMatcher.MatchAll(group, values, captures, type))
                    return values;

                // The components of the group (the property references at any depth have no index)
                var parts = captures.Where(c => c.ChildIndex >= 0).ToList();
                if (parts.Sum(p => p.End - p.Start) != values.Count)
                    return values;

                if (group.Combinator is GrammarCombinator.AnyInAnyOrder or GrammarCombinator.AllInAnyOrder)
                {
                    parts.Sort((a, b) => a.ChildIndex.CompareTo(b.ChildIndex));
                }

                var result = new List<CssComponentValue>(values.Count);
                foreach (var part in parts)
                {
                    result.AddRange(Canonicalize(group.Children[part.ChildIndex], values[part.Start..part.End], type, depth + 1));
                }

                return result;
            }

            default:
                return values;
        }
    }

    /// <summary>A node that matches a single repetition of the node.</summary>
    private static GrammarNode? Single(GrammarNode node) => node switch
    {
        TypeNode type => new TypeNode(type.Name, type.IsProperty) { RangeMin = type.RangeMin, RangeMax = type.RangeMax },
        GroupNode group => new GroupNode(group.Combinator, group.Children),
        _ => null,
    };
}
