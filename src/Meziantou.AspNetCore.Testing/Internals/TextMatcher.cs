using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>
/// Matches a text as Playwright does: a string matches a substring, ignoring the case, once the white space of both texts
/// is collapsed; an exact string matches the whole text, with the case; a regular expression is evaluated on the text.
/// </summary>
internal sealed class TextMatcher
{
    // The characters that NormalizeWhiteSpace removes or replaces wherever they are: the white space other than a space, and the zero-width space
    private static readonly SearchValues<char> NonSpaceWhiteSpaceOrZeroWidthSpace = CreateNonSpaceWhiteSpaceOrZeroWidthSpace();

    private readonly string? _text;
    private readonly string? _longestWord;
    private readonly Regex? _regex;
    private readonly TextMatchKind _kind;

    private TextMatcher(string? text, Regex? regex, TextMatchKind kind)
    {
        _text = text;
        _regex = regex;
        _kind = kind;
        if (kind is TextMatchKind.Exact or TextMatchKind.Substring)
        {
            _longestWord = GetLongestWord(text!);
        }
    }

    public static TextMatcher Create(string text, bool exact)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(NormalizeWhiteSpace(text), regex: null, exact ? TextMatchKind.Exact : TextMatchKind.Substring);
    }

    public static TextMatcher Create(Regex regex)
    {
        ArgumentNullException.ThrowIfNull(regex);
        return new(text: null, regex, TextMatchKind.Regex);
    }

    /// <summary>Creates a matcher that compares the whole text, without collapsing the white space (the test identifiers).</summary>
    public static TextMatcher CreateVerbatim(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(text, regex: null, TextMatchKind.Verbatim);
    }

    public bool IsMatch(string value) => _kind switch
    {
        TextMatchKind.Regex => _regex!.IsMatch(value),
        TextMatchKind.Verbatim => string.Equals(value, _text, StringComparison.Ordinal),
        _ => IsNormalizedMatch(value),
    };

    /// <summary>Gets whether the text of an element matches (see <see cref="LocatorEvaluator.GetText"/>).</summary>
    public bool IsMatch(LocatorEvaluator evaluator, Element element) => _kind is TextMatchKind.Regex or TextMatchKind.Verbatim
        ? IsMatch(evaluator.GetText(element))
        : IsNormalizedMatch(evaluator.GetTextSpan(element));

    /// <summary>Compares the normalized value with the expected text, without creating a string.</summary>
    private bool IsNormalizedMatch(ReadOnlySpan<char> value)
    {
        // Most values are rejected without being normalized
        var exact = _kind is TextMatchKind.Exact;
        var comparison = exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if ((exact && value.Length < _text!.Length) || !MayContainLongestWord(value, comparison))
            return false;

        var buffer = ArrayPool<char>.Shared.Rent(value.Length);
        try
        {
            var normalized = buffer.AsSpan(0, NormalizeWhiteSpace(value, buffer));
            return exact ? normalized.SequenceEqual(_text) : normalized.Contains(_text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Gets whether the normalized value may contain the longest word of the expected text, without normalizing the value.
    /// The normalization changes the white space and removes the zero-width spaces: a word, which has no white space, is in
    /// the normalized value only when it is in the value, or when the value has zero-width spaces that join its parts.
    /// </summary>
    private bool MayContainLongestWord(ReadOnlySpan<char> value, StringComparison comparison) => value.Contains(_longestWord, comparison) || value.Contains('\u200b');

    // The expected text is normalized: its words are separated by one space
    private static string GetLongestWord(string text)
    {
        var longest = ReadOnlySpan<char>.Empty;
        foreach (var range in text.AsSpan().Split(' '))
        {
            var word = text.AsSpan(range);
            if (word.Length > longest.Length)
            {
                longest = word;
            }
        }

        return longest.ToString();
    }

    /// <summary>Removes the leading and trailing white space and the zero-width spaces, and replaces the other runs of white space with a space.</summary>
    public static string NormalizeWhiteSpace(string value)
    {
        // Most texts are already normalized (a word, a sentence): they are returned as is, without a copy
        if (IsNormalized(value))
            return value;

        var buffer = ArrayPool<char>.Shared.Rent(value.Length);
        try
        {
            return new string(buffer, 0, NormalizeWhiteSpace(value, buffer));
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    // The normalized text is not longer than the value: the destination has the length of the value
    private static int NormalizeWhiteSpace(ReadOnlySpan<char> value, Span<char> destination)
    {
        var length = 0;
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (c == '\u200b')
                continue;

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = length > 0;
                continue;
            }

            if (pendingSpace)
            {
                destination[length++] = ' ';
                pendingSpace = false;
            }

            destination[length++] = c;
        }

        return length;
    }

    private static bool IsNormalized(string value)
    {
        if (value.Length == 0)
            return true;

        if (value[0] == ' ' || value[^1] == ' ')
            return false;

        return !value.AsSpan().ContainsAny(NonSpaceWhiteSpaceOrZeroWidthSpace) && !value.Contains("  ", StringComparison.Ordinal);
    }

    private static SearchValues<char> CreateNonSpaceWhiteSpaceOrZeroWidthSpace()
    {
        var chars = new List<char> { '\u200b' };
        for (var c = 0; c <= char.MaxValue; c++)
        {
            if (c != ' ' && char.IsWhiteSpace((char)c))
            {
                chars.Add((char)c);
            }
        }

        return SearchValues.Create([.. chars]);
    }

    public override string ToString() => _kind switch
    {
        TextMatchKind.Regex => "/" + _regex + "/" + (_regex!.Options.HasFlag(RegexOptions.IgnoreCase) ? "i" : ""),
        TextMatchKind.Exact => Quote(_text!) + ", exact: true",
        _ => Quote(_text!),
    };

    public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);

    private enum TextMatchKind
    {
        Substring,
        Exact,
        Verbatim,
        Regex,
    }
}
