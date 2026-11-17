using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>
/// Matches a text as Playwright does: a string matches a substring, ignoring the case, once the white space of both texts
/// is collapsed; an exact string matches the whole text, with the case; a regular expression is evaluated on the text.
/// </summary>
internal sealed class TextMatcher
{
    private readonly string? _text;
    private readonly Regex? _regex;
    private readonly TextMatchKind _kind;

    private TextMatcher(string? text, Regex? regex, TextMatchKind kind)
    {
        _text = text;
        _regex = regex;
        _kind = kind;
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
        TextMatchKind.Exact => string.Equals(NormalizeWhiteSpace(value), _text, StringComparison.Ordinal),
        _ => NormalizeWhiteSpace(value).Contains(_text!, StringComparison.OrdinalIgnoreCase),
    };

    /// <summary>Removes the leading and trailing white space and the zero-width spaces, and replaces the other runs of white space with a space.</summary>
    public static string NormalizeWhiteSpace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (c == '\u200b')
                continue;

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
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
