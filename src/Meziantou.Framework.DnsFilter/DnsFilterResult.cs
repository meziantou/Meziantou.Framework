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
    /// </summary>
    public static DnsFilterResult NotMatched { get; } = new() { IsMatched = false, Action = DnsFilterAction.Block };

    /// <summary>
    /// Gets a value indicating whether the query matched a filter rule.
    /// </summary>
    public bool IsMatched { get; private init; }

    /// <summary>
    /// Gets the action determined by the matching rule.
    /// Only meaningful when <see cref="IsMatched"/> is <see langword="true"/>.
    /// </summary>
    public DnsFilterAction Action { get; private init; }

    /// <summary>
    /// Gets the matching rule, if any.
    /// </summary>
    public DnsFilterRule? MatchingRule { get; private init; }

    /// <summary>
    /// Gets the rewrite directive from the matching rule, if any.
    /// </summary>
    public DnsFilterRewriteRule? Rewrite { get; private init; }

    internal static DnsFilterResult Blocked(DnsFilterRule rule)
    {
        return new DnsFilterResult
        {
            IsMatched = true,
            Action = DnsFilterAction.Block,
            MatchingRule = rule,
            Rewrite = rule.Rewrite,
        };
    }

    internal static DnsFilterResult Allowed(DnsFilterRule rule)
    {
        return new DnsFilterResult
        {
            IsMatched = true,
            Action = DnsFilterAction.Allow,
            MatchingRule = rule,
        };
    }
}
