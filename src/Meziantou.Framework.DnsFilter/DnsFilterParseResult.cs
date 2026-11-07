namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// The outcome of parsing a filter list: the rules that were understood, and a diagnostic for
/// every line that was not.
/// </summary>
/// <remarks>
/// Filter lists are third-party input. A list that silently produces far fewer rules than it has
/// lines — because it was served as an HTML error page, or uses modifiers this library does not
/// implement — is indistinguishable from a healthy one if only the rule count is inspected.
/// Callers that load lists unattended should log or alarm on <see cref="Diagnostics"/>.
/// </remarks>
public sealed class DnsFilterParseResult
{
    internal DnsFilterParseResult(IReadOnlyList<DnsFilterRule> rules, IReadOnlyList<DnsFilterParseDiagnostic> diagnostics)
    {
        Rules = rules;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Gets the rules parsed from the list.
    /// </summary>
    public IReadOnlyList<DnsFilterRule> Rules { get; }

    /// <summary>
    /// Gets a diagnostic for each line that did not produce a rule. Empty when the whole list was understood.
    /// </summary>
    public IReadOnlyList<DnsFilterParseDiagnostic> Diagnostics { get; }

    /// <summary>
    /// Gets the format the list was parsed as. Useful when <see cref="DnsFilterListFormat.AutoDetect"/> was requested.
    /// </summary>
    public DnsFilterListFormat Format { get; internal init; }
}
