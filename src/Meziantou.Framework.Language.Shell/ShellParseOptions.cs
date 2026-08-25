namespace Meziantou.Framework.Language.Shell;

/// <summary>Configures how <see cref="ShellSyntaxTree"/> parses text.</summary>
public sealed record ShellParseOptions
{
    /// <summary>The default value of <see cref="MaxRecursionDepth"/>.</summary>
    public const int DefaultMaxRecursionDepth = 128;

    public ShellParseOptions(ShellDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        Dialect = dialect;
    }

    /// <summary>The dialect to parse the text as.</summary>
    public ShellDialect Dialect { get; init; }

    /// <summary>
    /// The maximum number of nested constructs the parser descends into before reporting <c>SHELL0100</c> and
    /// treating the remaining text as skipped text. Guards against stack overflow on deeply nested input.
    /// </summary>
    public int MaxRecursionDepth { get; init; } = DefaultMaxRecursionDepth;
}
