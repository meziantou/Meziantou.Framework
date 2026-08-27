using System.Buffers;

namespace Meziantou.Framework.SyntaxHighlighting;

public sealed class HighlightOptions
{
    private static readonly SearchValues<char> ValidClassPrefixChars =
        SearchValues.Create("-_0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

    private readonly string _classPrefix = "hljs-";

    /// <summary>
    /// The prefix prepended to every generated CSS class name. The value is written directly
    /// into the <c>class</c> attribute of the generated markup, so it is restricted to
    /// characters that are valid in a CSS identifier: letters, digits, <c>-</c> and <c>_</c>.
    /// It cannot start with a digit.
    /// </summary>
    /// <exception cref="ArgumentException">The value contains a character that is not valid in a CSS identifier, or it starts with a digit.</exception>
    public string ClassPrefix
    {
        get => _classPrefix;
        init
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.AsSpan().ContainsAnyExcept(ValidClassPrefixChars))
                throw new ArgumentException($"'{value}' is not a valid CSS class prefix: only letters, digits, '-' and '_' are allowed.", nameof(value));

            if (value.Length > 0 && char.IsAsciiDigit(value[0]))
                throw new ArgumentException($"'{value}' is not a valid CSS class prefix: it cannot start with a digit.", nameof(value));

            _classPrefix = value;
        }
    }

    internal static HighlightOptions Default { get; } = new();
}
