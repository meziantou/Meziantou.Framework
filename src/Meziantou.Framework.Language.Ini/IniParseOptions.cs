using Meziantou.Framework.Language.Ini.Internals;

namespace Meziantou.Framework.Language.Ini;

/// <summary>Configures how <see cref="IniSyntaxTree"/> parses text.</summary>
/// <remarks>INI has no specification, so these options select between the behaviors of the common dialects.</remarks>
public sealed record IniParseOptions
{
    /// <summary>Gets the options used when none are given.</summary>
    public static IniParseOptions Default { get; } = new();

    /// <summary>Gets the options that read git configuration files, such as <c>.gitconfig</c> or <c>.git/config</c>.</summary>
    /// <remarks>See <see cref="IniDialect.Git"/>.</remarks>
    public static IniParseOptions Git { get; } = new() { Dialect = IniDialect.Git };

    /// <summary>Gets the family of syntax the document is read with. Defaults to <see cref="IniDialect.Generic"/>.</summary>
    /// <remarks>
    /// <see cref="IniDialect.Git"/> reads comments, quotes, escapes, and keys without a value as git does, whatever
    /// <see cref="InlineComments"/>, <see cref="AllowQuotedValues"/>, <see cref="AllowEscapes"/>,
    /// <see cref="AllowMultilineValues"/>, and <see cref="AllowKeysWithoutValue"/> say.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not one of <see cref="IniDialect"/>.</exception>
    public IniDialect Dialect
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The value is not an INI dialect.");
    }

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
    /// Gets a value indicating whether a backslash escapes the character after it in a value, quoted or not:
    /// <c>\\</c>, <c>\'</c>, <c>\"</c>, <c>\;</c>, <c>\#</c>, <c>\=</c>, <c>\:</c>, <c>\0</c>, <c>\a</c>, <c>\b</c>,
    /// <c>\t</c>, <c>\r</c>, and <c>\n</c>.
    /// </summary>
    /// <remarks>
    /// An escaped <c>;</c> or <c>#</c> never starts a comment, and an escaped quote never closes a quoted value. A backslash
    /// in front of any other character, or at the end of the line, is an ordinary character. Keys and section names are
    /// not escaped. An edit writes a value with line breaks as <c>\r</c> and <c>\n</c> on one line, so that it reads back
    /// exactly as it was, even when <see cref="AllowMultilineValues"/> is set.
    /// </remarks>
    public bool AllowEscapes { get; init; }

    /// <summary>
    /// Gets a value indicating whether a section name used twice (<c>INI0006</c>) and a key used twice in the same
    /// section (<c>INI0007</c>) are reported as warnings. Names are compared with <see cref="NameComparer"/>.
    /// </summary>
    public bool ReportDuplicates { get; init; }

    /// <summary>
    /// Gets how section names and keys are compared to find duplicates. Defaults to <see cref="StringComparer.OrdinalIgnoreCase"/>,
    /// or, with <see cref="IniDialect.Git"/>, to a comparer that ignores the case of section names and keys but not of
    /// subsections, as git does: <c>Remote.origin</c> is <c>remote.origin</c>, not <c>remote.Origin</c>.
    /// </summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public StringComparer NameComparer
    {
        get => field ?? (Dialect is IniDialect.Git ? GitNameComparer.Instance : StringComparer.OrdinalIgnoreCase);
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    internal bool IsGit => Dialect is IniDialect.Git;

    /// <summary>Gets where a comment starts in the middle of a line, as the dialect reads it.</summary>
    internal IniInlineCommentMode EffectiveInlineComments => IsGit ? IniInlineCommentMode.Anywhere : InlineComments;

    internal bool EffectiveAllowKeysWithoutValue => IsGit || AllowKeysWithoutValue;

    internal bool EffectiveAllowMultilineValues => !IsGit && AllowMultilineValues;

    internal bool EffectiveAllowQuotedValues => !IsGit && AllowQuotedValues;

    internal bool EffectiveAllowEscapes => !IsGit && AllowEscapes;

    /// <summary>Gets whether a value with line breaks is written on one line, with escapes, rather than on lines of its own.</summary>
    internal bool WritesLineBreaksAsEscapes => IsGit || AllowEscapes;
}
