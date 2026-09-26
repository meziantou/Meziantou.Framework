using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Parsing;

/// <summary>
/// Represents a lexical token produced by <see cref="TomlLexer"/>.
/// </summary>
public readonly struct TomlToken
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlToken"/> struct.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position.</param>
    /// <param name="stringValue">Optional decoded string value.</param>
    /// <param name="data">Optional scalar payload.</param>
    public TomlToken(TokenKind kind, TomlTextPosition start, TomlTextPosition end, string? stringValue = null, ulong data = 0)
    {
        Kind = kind;
        Start = start;
        End = end;
        StringValue = stringValue;
        Data = data;
    }

    /// <summary>
    /// Gets the token kind.
    /// </summary>
    public TokenKind Kind { get; }

    /// <summary>
    /// Gets the start position of this token.
    /// </summary>
    public TomlTextPosition Start { get; }

    /// <summary>
    /// Gets the end position of this token.
    /// </summary>
    public TomlTextPosition End { get; }

    /// <summary>
    /// Gets the token length (inclusive).
    /// </summary>
    public int Length => End.Offset - Start.Offset + 1;

    /// <summary>
    /// Gets the optional decoded string value for tokens that carry a string payload.
    /// </summary>
    public string? StringValue { get; }

    /// <summary>
    /// Gets the optional scalar payload for tokens that carry a non-string value.
    /// </summary>
    public ulong Data { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}({Start}:{End})";
}

