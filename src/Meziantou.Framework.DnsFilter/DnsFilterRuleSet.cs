namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// An aggregated collection of DNS filter rules from one or more parsed sources.
/// </summary>
/// <remarks>
/// This type is not thread-safe for concurrent mutation. It is safe to hand a rule set to
/// <see cref="DnsFilterEngine"/> and keep mutating it afterwards: the engine snapshots the rules
/// when it builds.
/// </remarks>
public sealed class DnsFilterRuleSet
{
    private readonly List<DnsFilterRule> _rules = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Gets a snapshot of the rules in this rule set.
    /// </summary>
    public IReadOnlyList<DnsFilterRule> Rules => ToArray();

    /// <summary>
    /// Adds a single rule to this rule set.
    /// </summary>
    /// <param name="rule">The rule to add.</param>
    public void Add(DnsFilterRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_lock)
        {
            _rules.Add(rule);
        }
    }

    /// <summary>
    /// Adds multiple rules to this rule set.
    /// </summary>
    /// <param name="rules">The rules to add.</param>
    public void AddRange(IEnumerable<DnsFilterRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        lock (_lock)
        {
            _rules.AddRange(rules);
        }
    }

    /// <summary>
    /// Adds rules parsed from a filter list.
    /// </summary>
    /// <param name="reader">The text reader containing the filter list.</param>
    /// <param name="format">The format of the filter list.</param>
    /// <returns>A diagnostic for each line that did not produce a rule.</returns>
    public IReadOnlyList<DnsFilterParseDiagnostic> AddFromList(TextReader reader, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
    {
        var parsed = DnsFilterListReader.ParseWithDiagnostics(reader, format);
        AddRange(parsed.Rules);
        return parsed.Diagnostics;
    }

    /// <summary>
    /// Adds rules parsed from a filter list string.
    /// </summary>
    /// <param name="text">The filter list text.</param>
    /// <param name="format">The format of the filter list.</param>
    /// <returns>A diagnostic for each line that did not produce a rule.</returns>
    public IReadOnlyList<DnsFilterParseDiagnostic> AddFromList(string text, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
    {
        var parsed = DnsFilterListReader.ParseWithDiagnostics(text, format);
        AddRange(parsed.Rules);
        return parsed.Diagnostics;
    }

    /// <summary>
    /// Gets the number of rules in this rule set.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _rules.Count;
            }
        }
    }

    /// <summary>
    /// Removes all rules from this rule set.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _rules.Clear();
        }
    }

    internal DnsFilterRule[] ToArray()
    {
        lock (_lock)
        {
            return [.. _rules];
        }
    }
}
