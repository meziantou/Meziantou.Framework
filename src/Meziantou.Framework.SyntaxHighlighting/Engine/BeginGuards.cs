using System.Text.RegularExpressions;

namespace Meziantou.Framework.SyntaxHighlighting.Engine;

/// <summary>
/// Named guards executed against a candidate begin match. Returning false causes
/// the engine to discard that candidate and search for the next match of the same
/// mode further in the input. Mirrors highlight.js's `on:begin` callback when it
/// calls `response.ignoreMatch()`.
/// </summary>
internal static partial class BeginGuards
{
    [GeneratedRegex(@"^\s*=", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: -1)]
    private static partial Regex LeadingEquals();

    [GeneratedRegex(@"^\s+extends\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: -1)]
    private static partial Regex LeadingExtends();

    public static bool Accept(string name, Match match, string input) => name switch
    {
        "jsxTag" => IsTrulyOpeningTag(match, input),
        _ => true,
    };

    /// <summary>
    /// Port of highlight.js's <c>XML_TAG.isTrulyOpeningTag</c> from javascript.js:
    /// distinguishes a real JSX/XML opening tag from TypeScript generics like
    /// <c>&lt;T&gt;(x)</c> or template typing fragments.
    /// </summary>
    private static bool IsTrulyOpeningTag(Match match, string input)
    {
        var afterIndex = match.Index + match.Length;
        if (afterIndex >= input.Length)
            return true;

        var nextChar = input[afterIndex];
        // `<Array<X>>` (nested type) or `<T, U>` (type list) — not a tag.
        if (nextChar is '<' or ',')
            return false;

        if (nextChar == '>')
        {
            // `<Tag>` is only a real tag if a matching `</Tag>` follows.
            var tag = match.ValueSpan[1..];
            var rest = input.AsSpan(afterIndex);
            if (!ContainsClosingTag(rest, tag))
                return false;
        }

        var tail = input.AsSpan(afterIndex);
        // `<T = any>` — generic with default.
        if (LeadingEquals().IsMatch(tail))
            return false;
        // `<From extends string>` — type parameter with constraint.
        if (LeadingExtends().IsMatch(tail))
            return false;

        return true;
    }

    private static bool ContainsClosingTag(ReadOnlySpan<char> haystack, ReadOnlySpan<char> tag)
    {
        var cursor = 0;
        while (cursor < haystack.Length)
        {
            var idx = haystack[cursor..].IndexOf("</", StringComparison.Ordinal);
            if (idx < 0)
                return false;
            var after = cursor + idx + 2;
            if (after + tag.Length <= haystack.Length && haystack.Slice(after, tag.Length).SequenceEqual(tag))
                return true;
            cursor = after;
        }
        return false;
    }
}
