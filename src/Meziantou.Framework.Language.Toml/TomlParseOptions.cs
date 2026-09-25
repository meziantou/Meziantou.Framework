namespace Meziantou.Framework.Language.Toml;

/// <summary>Configures how <see cref="TomlSyntaxTree"/> parses text.</summary>
public sealed record TomlParseOptions
{
    /// <summary>The default value of <see cref="MaxDepth"/>.</summary>
    public const int DefaultMaxDepth = 128;

    /// <summary>Gets the options used when none are given: the latest version and the default depth.</summary>
    public static TomlParseOptions Default { get; } = new();

    /// <summary>Gets the version of the specification the text is read as.</summary>
    /// <remarks>What a later version allows is reported as an error when parsing as an earlier one.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of the versions of <see cref="TomlVersion"/>.</exception>
    public TomlVersion Version
    {
        get => field;
        init
        {
            if (value is not (TomlVersion.V1_0 or TomlVersion.V1_1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not a known version of TOML.");

            field = value;
        }
    } = TomlVersion.Latest;

    /// <summary>
    /// Gets how deeply arrays and inline tables may nest before the parser reports <c>TOML0012</c> and keeps the rest
    /// as skipped text. Guards against a stack overflow on deeply nested input.
    /// </summary>
    /// <remarks>
    /// Raising it is safe: nesting that would not fit on the stack of the thread doing the parsing is reported as
    /// <c>TOML0012</c> as well, whatever the limit says.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than one.</exception>
    public int MaxDepth
    {
        get => field;
        init
        {
            // A depth below one makes every array skipped text, which looks like a parser bug rather than like the
            // misconfiguration it is. Better to fail where the mistake was made.
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = DefaultMaxDepth;
}
