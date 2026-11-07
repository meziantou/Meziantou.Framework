namespace Meziantou.Framework.SyntaxHighlighting;

public sealed class HighlightOptions
{
    /// <summary>
    /// Gets the prefix of the CSS class names in the generated markup. The default is <c>hljs-</c>,
    /// which matches highlight.js themes.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value contains a character other than an ASCII letter, a digit, <c>-</c> or <c>_</c>.</exception>
    public string ClassPrefix
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var c in value)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')
                    throw new ArgumentException($"The class prefix '{value}' contains the character '{c}'. Only ASCII letters, digits, '-' and '_' are allowed.", nameof(value));
            }

            field = value;
        }
    } = "hljs-";

    /// <summary>
    /// Gets a value indicating whether a lexeme that the grammar considers illegal is emitted as
    /// plain text while the rest of the code is still highlighted. When <see langword="false"/>, a
    /// single illegal lexeme makes the whole result plain text. The default is <see langword="true"/>,
    /// which matches how highlight.js highlights code blocks in a page.
    /// </summary>
    /// <remarks>Embedded languages (for example CSS inside HTML) always ignore illegal lexemes.</remarks>
    public bool IgnoreIllegals { get; init; } = true;

    /// <summary>
    /// Gets the maximum time a single regular expression match may take. When a match exceeds it, the whole result is
    /// returned as plain (HTML-encoded) text. The default is <see cref="Timeout.InfiniteTimeSpan"/>. Set a timeout when
    /// highlighting untrusted code, to bound the work a hostile input can cause; it makes matching about twice as slow.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not <see cref="Timeout.InfiniteTimeSpan"/> and is not positive or is 24 days or longer.</exception>
    public TimeSpan MatchTimeout
    {
        get;
        init
        {
            // Regex accepts up to int.MaxValue - 1 milliseconds.
            if (value != Timeout.InfiniteTimeSpan && (value <= TimeSpan.Zero || value.TotalMilliseconds >= int.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The match timeout must be positive and less than 24 days, or Timeout.InfiniteTimeSpan.");

            field = value;
        }
    } = Engine.Compiler.DefaultMatchTimeout;

    internal static HighlightOptions Default { get; } = new();
}
