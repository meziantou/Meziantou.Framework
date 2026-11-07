namespace Meziantou.Framework.Language.Ini;

/// <summary>Configures how <see cref="IniSyntaxTree"/> parses text.</summary>
/// <remarks>INI has no specification, so these options select between the behaviors of the common dialects.</remarks>
public sealed record IniParseOptions
{
    /// <summary>Gets the options used when none are given.</summary>
    public static IniParseOptions Default { get; } = new();

    /// <summary>Gets where <c>;</c> and <c>#</c> start a comment other than at the beginning of a line.</summary>
    /// <remarks>A value in quotes, such as <c>"a;b"</c>, never contains a comment, whatever this says.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of <see cref="IniInlineCommentMode"/>.</exception>
    public IniInlineCommentMode InlineComments
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not an inline comment mode.");
    } = IniInlineCommentMode.AfterWhitespace;

    /// <summary>
    /// Gets a value indicating whether a line holding only a key, such as <c>skip-networking</c> in a MySQL option file,
    /// is a property. When it is not allowed, the line is still read as a property, and <c>INI0004</c> is reported.
    /// </summary>
    public bool AllowKeysWithoutValue { get; init; }

    /// <summary>
    /// Gets a value indicating whether a value continues on the lines that follow it when they are indented more than its
    /// key, as Python's <c>configparser</c> reads them.
    /// </summary>
    /// <remarks>
    /// As in <c>configparser</c>, a comment line between the lines of a value is skipped, and a blank line between them
    /// is an empty line of the value. Each line is read like a one-line value, so its quotes and its comment are not
    /// part of it.
    /// </remarks>
    public bool AllowMultilineValues { get; init; }

    /// <summary>
    /// Gets a value indicating whether a value wholly in double or single quotes, such as <c>"a;b"</c>, is read without
    /// them, so that <c>;</c> and <c>#</c> inside them never start a comment. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Set it to <see langword="false"/> for dialects where quotes are ordinary characters, such as Python's
    /// <c>configparser</c>: the value of <c>key="a"</c> is then <c>"a"</c>, quotes included, and an edit never adds
    /// quotes to a value.
    /// </remarks>
    public bool AllowQuotedValues { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether a section name used twice (<c>INI0006</c>) and a key used twice in the same
    /// section (<c>INI0007</c>) are reported as warnings. Names are compared with <see cref="NameComparer"/>.
    /// </summary>
    public bool ReportDuplicates { get; init; }

    /// <summary>Gets how section names and keys are compared to find duplicates. Defaults to <see cref="StringComparer.OrdinalIgnoreCase"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public StringComparer NameComparer
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    } = StringComparer.OrdinalIgnoreCase;
}
