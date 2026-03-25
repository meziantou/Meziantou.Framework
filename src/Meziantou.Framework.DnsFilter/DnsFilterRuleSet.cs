namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// An aggregated collection of DNS filter rules from one or more parsed sources.
/// </summary>
public sealed class DnsFilterRuleSet
{
    private readonly List<DnsFilterRule> _rules = [];

    /// <summary>
    /// Gets the rules in this rule set.
    /// </summary>
    public IReadOnlyList<DnsFilterRule> Rules => _rules;

    /// <summary>
    /// Adds a single rule to this rule set.
    /// </summary>
    /// <param name="rule">The rule to add.</param>
    public void Add(DnsFilterRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
    }

    /// <summary>
    /// Adds multiple rules to this rule set.
    /// </summary>
    /// <param name="rules">The rules to add.</param>
    public void AddRange(IEnumerable<DnsFilterRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules.AddRange(rules);
    }

    /// <summary>
    /// Adds rules parsed from a filter list.
    /// </summary>
    /// <param name="reader">The text reader containing the filter list.</param>
    /// <param name="format">The format of the filter list.</param>
    public void AddFromList(TextReader reader, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
    {
        var parsed = DnsFilterListReader.Parse(reader, format);
        _rules.AddRange(parsed);
    }

    /// <summary>
    /// Adds rules parsed from a filter list string.
    /// </summary>
    /// <param name="text">The filter list text.</param>
    /// <param name="format">The format of the filter list.</param>
    public void AddFromList(string text, DnsFilterListFormat format = DnsFilterListFormat.AutoDetect)
    {
        var parsed = DnsFilterListReader.Parse(text, format);
        _rules.AddRange(parsed);
    }

    /// <summary>
    /// Removes all rules from this rule set.
    /// </summary>
    public void Clear() => _rules.Clear();
}
