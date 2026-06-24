namespace Meziantou.Framework.RobotsTxt;

/// <summary>
/// Describes a single non-fatal parse error encountered while reading a <c>robots.txt</c> file.
/// </summary>
/// <remarks>
/// The parser is lenient and always skips lines it cannot interpret, but each skipped or
/// unrecognised line is recorded as a <see cref="RobotsParseError"/> in
/// <see cref="RobotsFile.ParseErrors"/> so callers can audit file quality.
/// </remarks>
public sealed class RobotsParseError
{
    public RobotsParseError(int lineNumber, string line, RobotsParseErrorKind kind)
    {
        LineNumber = lineNumber;
        Line = line;
        Kind = kind;
    }

    /// <summary>Gets the 1-based line number where the error occurred.</summary>
    public int LineNumber { get; }

    /// <summary>Gets the raw text of the offending line (before comment stripping and whitespace trimming).</summary>
    public string Line { get; }

    /// <summary>Gets the kind of error that was detected.</summary>
    public RobotsParseErrorKind Kind { get; }

    /// <inheritdoc/>
    public override string ToString() => $"Line {LineNumber} ({Kind}): {Line}";
}
