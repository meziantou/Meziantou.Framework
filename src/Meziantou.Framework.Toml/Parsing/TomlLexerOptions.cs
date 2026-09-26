namespace Tomlyn.Parsing;

/// <summary>
/// Configures the behavior of <see cref="TomlLexer"/>.
/// </summary>
public sealed record TomlLexerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the lexer should decode scalar values (for example, string escape sequences).
    /// </summary>
    /// <remarks>
    /// When <c>false</c> (default), string tokens carry only their span; higher layers can materialize values on demand.
    /// </remarks>
    public bool DecodeScalars { get; init; }
}

