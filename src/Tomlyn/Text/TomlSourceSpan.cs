namespace Tomlyn.Text;

/// <summary>
/// Represents a textual source span within a TOML payload.
/// </summary>
public readonly struct TomlSourceSpan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlSourceSpan"/> struct.
    /// </summary>
    /// <param name="sourceName">An optional source name (for example a file path).</param>
    /// <param name="start">The start position.</param>
    /// <param name="end">The end position.</param>
    public TomlSourceSpan(string sourceName, TomlTextPosition start, TomlTextPosition end)
    {
        SourceName = sourceName ?? string.Empty;
        Start = start;
        End = end;
    }

    /// <summary>
    /// Gets the source name associated with this span (for example a file path).
    /// </summary>
    public string SourceName { get; }

    /// <summary>
    /// Gets the starting offset of this span.
    /// </summary>
    public int Offset => Start.Offset;

    /// <summary>
    /// Gets the length of this span (inclusive).
    /// </summary>
    public int Length => End.Offset - Start.Offset + 1;

    /// <summary>
    /// Gets the starting position.
    /// </summary>
    public TomlTextPosition Start { get; }

    /// <summary>
    /// Gets the ending position.
    /// </summary>
    public TomlTextPosition End { get; }

    /// <inheritdoc />
    public override string ToString() => $"{SourceName}{Start}-{End}";

    /// <summary>
    /// Returns a string representation of this source span not including the end position.
    /// </summary>
    public string ToStringSimple() => $"{SourceName}{Start}";
}

