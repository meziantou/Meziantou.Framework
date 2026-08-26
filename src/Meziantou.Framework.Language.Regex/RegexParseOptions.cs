namespace Meziantou.Framework.Language.Regex;

/// <summary>Configures how <see cref="RegexSyntaxTree"/> parses a pattern.</summary>
public sealed record RegexParseOptions
{
    /// <summary>The default value of <see cref="MaxRecursionDepth"/>.</summary>
    public const int DefaultMaxRecursionDepth = 128;

    public RegexParseOptions(RegexFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(flavor);
        Flavor = flavor;
    }

    /// <summary>The flavor to parse the pattern as.</summary>
    public RegexFlavor Flavor { get; init; }

    /// <summary>
    /// The maximum number of nested groups and character classes the parser descends into before reporting
    /// <c>REGEX0200</c> and treating the remaining text as skipped text. Guards against stack overflow on deeply
    /// nested input.
    /// </summary>
    public int MaxRecursionDepth { get; init; } = DefaultMaxRecursionDepth;

    /// <summary>
    /// The options in effect at the start of the pattern, as if the caller had passed them to the engine. An inline
    /// construct such as <c>(?x)</c> changes them from that point on.
    /// </summary>
    public RegexPatternOptions PatternOptions { get; init; } = RegexPatternOptions.None;
}
