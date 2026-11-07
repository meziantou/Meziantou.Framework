namespace Meziantou.Framework.DnsFilter;

/// <summary>
/// Identifies why a filter list line was not turned into a rule.
/// </summary>
public enum DnsFilterParseError
{
    /// <summary>
    /// The line carries a modifier this library does not implement. The rule is discarded rather
    /// than applied without that modifier, because ignoring it would widen the rule beyond
    /// what its author wrote.
    /// </summary>
    UnsupportedModifier,

    /// <summary>
    /// The line carries a supported modifier whose value could not be parsed
    /// (for example an unknown <c>$dnstype</c> record type or a malformed CIDR in <c>$client</c>).
    /// </summary>
    InvalidModifierValue,

    /// <summary>
    /// The regular expression of a <c>/pattern/</c> rule is not valid, or its closing delimiter is missing.
    /// </summary>
    InvalidRegex,

    /// <summary>
    /// The line has no usable match pattern, or the pattern is not a valid domain name.
    /// </summary>
    InvalidPattern,
}
