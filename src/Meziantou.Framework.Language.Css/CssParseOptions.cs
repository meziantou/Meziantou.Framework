namespace Meziantou.Framework.Language.Css;

/// <summary>Configures how <see cref="CssSyntaxTree"/> parses text.</summary>
public sealed record CssParseOptions
{
    /// <summary>The default value of <see cref="MaxDepth"/>.</summary>
    public const int DefaultMaxDepth = 128;

    /// <summary>Gets the options used when none are given: a style sheet, with the default depth.</summary>
    public static CssParseOptions Default { get; } = new();

    /// <summary>Gets what the text holds.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of the kinds of <see cref="CssSourceKind"/>.</exception>
    public CssSourceKind SourceKind
    {
        get => field;
        init
        {
            if (value is not (CssSourceKind.StyleSheet or CssSourceKind.DeclarationList))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a known kind of CSS source.");

            field = value;
        }
    }

    /// <summary>
    /// Gets how deeply blocks, functions, selectors, and conditions may nest before the parser reports <c>CSS0016</c>
    /// and keeps the rest as skipped text. Guards against a stack overflow on deeply nested input.
    /// </summary>
    /// <remarks>
    /// Raising it is safe: nesting that would not fit on the stack of the thread doing the parsing is reported as
    /// <c>CSS0016</c> as well, whatever the limit says.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int MaxDepth
    {
        get => field;
        init
        {
            // A depth below one makes every block skipped text, which looks like a parser bug rather than like the
            // misconfiguration it is. Better to fail where the mistake was made.
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultMaxDepth;
}
