namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Describes a filter list line that did not produce a rule.
/// </summary>
public sealed class DnsFilterParseDiagnostic
{
    internal DnsFilterParseDiagnostic(int lineNumber, string line, DnsFilterParseError error, string? detail)
    {
        LineNumber = lineNumber;
        Line = line;
        Error = error;
        Detail = detail;
    }

    /// <summary>
    /// Gets the 1-based line number within the parsed list.
    /// </summary>
    public int LineNumber { get; }

    /// <summary>
    /// Gets the text of the offending line.
    /// </summary>
    public string Line { get; }

    /// <summary>
    /// Gets the reason the line was skipped.
    /// </summary>
    public DnsFilterParseError Error { get; }

    /// <summary>
    /// Gets additional context, such as the offending modifier name. May be <see langword="null"/>.
    /// </summary>
    public string? Detail { get; }

    /// <inheritdoc />
    public override string ToString() => Detail is null
        ? $"Line {LineNumber}: {Error} ({Line})"
        : $"Line {LineNumber}: {Error} '{Detail}' ({Line})";
}
