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
    public TomlVersion Version { get; init; } = TomlVersion.Latest;

    /// <summary>
    /// Gets how deeply arrays and inline tables may nest before the parser reports <c>TOML0012</c> and keeps the rest
    /// as skipped text. Guards against a stack overflow on deeply nested input.
    /// </summary>
    public int MaxDepth { get; init; } = DefaultMaxDepth;
}
