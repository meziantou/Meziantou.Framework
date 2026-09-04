namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Represents the outcome of evaluating a DNS query against the filter engine.
/// </summary>
public sealed class DnsFilterResult
{
    private DnsFilterResult()
    {
    }

    /// <summary>
    /// Gets a result indicating the query did not match any filter rule.
    /// Its <see cref="Action"/> is <see cref="DnsFilterAction.None"/>.
    /// </summary>
    public static DnsFilterResult NotMatched { get; } = new() { Action = DnsFilterAction.None };

    /// <summary>
    /// Gets a value indicating whether the query matched a filter rule.
    /// </summary>
    public bool IsMatched => Action is not DnsFilterAction.None;

    /// <summary>
    /// Gets the action determined by the matching rule, or <see cref="DnsFilterAction.None"/>
    /// when no rule matched.
    /// </summary>
    public DnsFilterAction Action { get; private init; }

    /// <summary>
    /// Gets the matching rule, if any.
    /// </summary>
    public DnsFilterRule? MatchingRule { get; private init; }

    /// <summary>
    /// Gets the rewrite directive from the matching rule. Non-<see langword="null"/> if and only if
    /// <see cref="Action"/> is <see cref="DnsFilterAction.Rewrite"/>.
    /// </summary>
    public DnsFilterRewriteRule? Rewrite { get; private init; }

    internal static DnsFilterResult Blocked(DnsFilterRule rule)
    {
        return new DnsFilterResult
        {
            Action = rule.Rewrite is null ? DnsFilterAction.Block : DnsFilterAction.Rewrite,
            MatchingRule = rule,
            Rewrite = rule.Rewrite,
        };
    }

    internal static DnsFilterResult Allowed(DnsFilterRule rule)
    {
        return new DnsFilterResult
        {
            Action = DnsFilterAction.Allow,
            MatchingRule = rule,
        };
    }
}
