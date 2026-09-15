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
    [GeneratedRegex(@"^\s*=", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LeadingEquals();

    [GeneratedRegex(@"^\s+extends\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LeadingExtends();

    public static bool Accept(string name, int index, int length, string input, ref ClosingTagIndex? closingTags) => name switch
    {
        "jsxTag" => IsTrulyOpeningTag(index, length, input, ref closingTags),
        _ => true,
    };

    /// <summary>
    /// Port of highlight.js's <c>XML_TAG.isTrulyOpeningTag</c> from javascript.js:
    /// distinguishes a real JSX/XML opening tag from TypeScript generics like
    /// <c>&lt;T&gt;(x)</c> or template typing fragments.
    /// </summary>
    private static bool IsTrulyOpeningTag(int index, int length, string input, ref ClosingTagIndex? closingTags)
    {
        var afterIndex = index + length;
        if (afterIndex >= input.Length)
            return true;

        var nextChar = input[afterIndex];
        // `<Array<X>>` (nested type) or `<T, U>` (type list) — not a tag.
        if (nextChar is '<' or ',')
            return false;

        if (nextChar == '>')
        {
            // `<Tag>` is only a real tag if a matching `</Tag>` follows.
            closingTags ??= new ClosingTagIndex(input);
            if (!closingTags.HasClosingTagAtOrAfter(input.AsSpan(index + 1, length - 1), afterIndex))
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

    /// <summary>
    /// Answers "does <c>&lt;/name</c> occur at or after a position" in O(name length).
    /// </summary>
    /// <remarks>
    /// highlight.js checks <c>input.indexOf("&lt;/" + name, after) !== -1</c>, a prefix test: <c>&lt;a&gt;</c>
    /// is closed by <c>&lt;/abbr&gt;</c>. Scanning the rest of the input for every candidate tag is
    /// quadratic, so all closing tags are indexed once per input into a trie of the tag-name
    /// characters that follow <c>&lt;/</c>, where each node records the last position its prefix
    /// occurs at. The depth is capped; longer names fall back to a direct scan.
    /// </remarks>
    internal sealed class ClosingTagIndex
    {
        private const int MaxDepth = 128;

        private readonly string _input;
        private readonly Dictionary<(int Node, char Char), int> _children = [];
        private readonly List<int> _lastOccurrence = [-1];

        public ClosingTagIndex(string input)
        {
            _input = input;

            var cursor = 0;
            while (true)
            {
                var start = input.IndexOf("</", cursor, StringComparison.Ordinal);
                if (start < 0)
                    break;

                var node = 0;
                for (var i = start + 2; i < input.Length && i - start - 2 < MaxDepth && IsTagNameChar(input[i]); i++)
                {
                    if (!_children.TryGetValue((node, input[i]), out var child))
                    {
                        child = _lastOccurrence.Count;
                        _lastOccurrence.Add(-1);
                        _children.Add((node, input[i]), child);
                    }

                    node = child;
                    _lastOccurrence[node] = start;
                }

                cursor = start + 2;
            }
        }

        public bool HasClosingTagAtOrAfter(ReadOnlySpan<char> name, int position)
        {
            if (name.Length > MaxDepth)
                return ScanForClosingTag(name, position);

            var node = 0;
            foreach (var c in name)
            {
                if (!_children.TryGetValue((node, c), out node))
                    return false;
            }

            return _lastOccurrence[node] >= position;
        }

        private bool ScanForClosingTag(ReadOnlySpan<char> name, int position)
        {
            var haystack = _input.AsSpan(position);
            var cursor = 0;
            while (cursor < haystack.Length)
            {
                var idx = haystack[cursor..].IndexOf("</", StringComparison.Ordinal);
                if (idx < 0)
                    return false;

                var after = cursor + idx + 2;
                if (haystack[after..].StartsWith(name, StringComparison.Ordinal))
                    return true;

                cursor = after;
            }

            return false;
        }

        // The characters of the jsxTag begin pattern `<[A-Za-z0-9\\._:-]+`.
        private static bool IsTagNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '\\' or '.' or '_' or ':' or '-';
    }
}
