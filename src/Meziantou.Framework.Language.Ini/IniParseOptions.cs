namespace Meziantou.Framework.Language.Ini;

/// <summary>Configures how <see cref="IniSyntaxTree"/> parses text.</summary>
/// <remarks>INI has no specification, so these options select between the behaviors of the common dialects.</remarks>
public sealed record IniParseOptions
{
    /// <summary>Gets the options used when none are given.</summary>
    public static IniParseOptions Default { get; } = new();

    /// <summary>Gets where <c>;</c> and <c>#</c> start a comment other than at the beginning of a line.</summary>
    /// <remarks>A value in quotes, such as <c>"a;b"</c>, never contains a comment, whatever this says.</remarks>
    public IniInlineCommentMode InlineComments { get; init; } = IniInlineCommentMode.AfterWhitespace;

    /// <summary>
    /// Gets a value indicating whether a line holding only a key, such as <c>skip-networking</c> in a MySQL option file,
    /// is a property. When it is not allowed, the line is still read as a property, and <c>INI0004</c> is reported.
    /// </summary>
    public bool AllowKeysWithoutValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether a value continues on the lines that follow it when they are indented more than its
    /// key, as Python's <c>configparser</c> reads them. A blank line or a comment line ends the value.
    /// </summary>
    public bool AllowMultilineValues { get; init; }

    /// <summary>
    /// Gets a value indicating whether a section name used twice (<c>INI0006</c>) and a key used twice in the same
    /// section (<c>INI0007</c>) are reported as warnings. Names are compared with <see cref="NameComparer"/>.
    /// </summary>
    public bool ReportDuplicates { get; init; }

    /// <summary>Gets how section names and keys are compared to find duplicates. Defaults to <see cref="StringComparer.OrdinalIgnoreCase"/>.</summary>
    public StringComparer NameComparer { get; init; } = StringComparer.OrdinalIgnoreCase;
}
