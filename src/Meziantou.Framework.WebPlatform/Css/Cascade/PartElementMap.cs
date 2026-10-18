using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The part element map of a shadow root (https://drafts.csswg.org/css-shadow-1/#part-element-map): the elements of the
/// shadow tree, and the elements of the nested shadow trees forwarded by the exportparts attribute, that ::part() selects,
/// by part name. A forwarded pseudo-element (exportparts="::before: name") is the pair of its originating element and its name.
/// </summary>
internal sealed class PartElementMap
{
    private readonly Dictionary<(Element Element, string? PseudoElement), List<string>> _names = [];

    public static PartElementMap Empty { get; } = new();

    public bool IsEmpty => _names.Count == 0;

    /// <summary>Returns the names the element (or one of its pseudo-elements) is exposed under, or null.</summary>
    public List<string>? GetNames(Element element, string? pseudoElement) => _names.GetValueOrDefault((element, pseudoElement));

    private void Add(string name, Element element, string? pseudoElement)
    {
        if (!_names.TryGetValue((element, pseudoElement), out var names))
        {
            names = [];
            _names[(element, pseudoElement)] = names;
        }

        if (!names.Contains(name, StringComparer.Ordinal))
        {
            names.Add(name);
        }
    }

    /// <summary>https://drafts.csswg.org/css-shadow-1/#calculate-the-part-element-map</summary>
    /// <param name="outerRoot">The shadow root.</param>
    /// <param name="getMap">Returns the part element map of a nested shadow root (memoized by the caller).</param>
    public static PartElementMap Calculate(ShadowRoot outerRoot, Func<ShadowRoot, PartElementMap> getMap)
    {
        var map = new PartElementMap();
        for (var node = outerRoot.FirstChild; node is not null; node = node.GetNextInTreeOrder(outerRoot))
        {
            if (node is not Element element)
                continue;

            foreach (var name in element.GetPartNames())
            {
                map.Add(name, element, pseudoElement: null);
            }

            if (element.GetAttributeValue("exportparts") is not { } exportParts)
                continue;

            var innerRoot = element.AttachedShadowRoot;
            var innerMap = innerRoot is null ? Empty : getMap(innerRoot);
            foreach (var (innerName, outerName) in ParsePartMappingList(exportParts))
            {
                if (innerName.StartsWith("::", StringComparison.Ordinal))
                {
                    // The pseudo-element of the element. The specification forwards the pseudo-elements of the shadow tree of a
                    // shadow host; the shadow trees of the user agent (e.g. the placeholder of an input) are not modeled, so the
                    // pseudo-elements of any element can be forwarded.
                    if (innerName.Length > 2)
                    {
                        map.Add(outerName, element, InfraAscii.ToAsciiLowercase(innerName[2..]));
                    }
                }
                else if (innerRoot is not null)
                {
                    foreach (var ((innerElement, innerPseudoElement), names) in innerMap._names)
                    {
                        if (names.Contains(innerName, StringComparer.Ordinal))
                        {
                            map.Add(outerName, innerElement, innerPseudoElement);
                        }
                    }
                }
            }
        }

        return map;
    }

    /// <summary>https://drafts.csswg.org/css-shadow-1/#parsing-mapping-list</summary>
    internal static List<(string InnerName, string OuterName)> ParsePartMappingList(string input)
    {
        var mappings = new List<(string, string)>();
        foreach (var unparsedMapping in input.Split(','))
        {
            if (InfraString.IsAsciiWhitespaceOnly(unparsedMapping))
                continue;

            // Unrecognized syntax is skipped
            if (ParsePartMapping(unparsedMapping) is { } mapping)
            {
                mappings.Add(mapping);
            }
        }

        return mappings;
    }

    /// <summary>https://drafts.csswg.org/css-shadow-1/#parsing-mapping</summary>
    private static (string, string)? ParsePartMapping(string input)
    {
        var position = 0;
        SkipWhitespace(input, ref position);
        var first = CollectToken(input, ref position);
        if (first.Length == 0)
            return null;

        SkipWhitespace(input, ref position);
        if (position >= input.Length)
            return (first, first);

        if (input[position] != ':')
            return null;

        position++;
        SkipWhitespace(input, ref position);
        var second = CollectToken(input, ref position);
        if (second.Length == 0 || second.StartsWith("::", StringComparison.Ordinal))
            return null;

        SkipWhitespace(input, ref position);
        if (position < input.Length)
            return null;

        return (first, second);

        static void SkipWhitespace(string input, ref int position)
        {
            while (position < input.Length && InfraAscii.IsAsciiWhitespace(input[position]))
            {
                position++;
            }
        }

        // A sequence of code points that are not ASCII whitespace or ":"; the leading "::" of a pseudo-element name is part of the token
        static string CollectToken(string input, ref int position)
        {
            var start = position;
            if (input.AsSpan(position).StartsWith("::", StringComparison.Ordinal))
            {
                position += 2;
            }

            while (position < input.Length && !InfraAscii.IsAsciiWhitespace(input[position]) && input[position] != ':')
            {
                position++;
            }

            return input[start..position];
        }
    }
}
