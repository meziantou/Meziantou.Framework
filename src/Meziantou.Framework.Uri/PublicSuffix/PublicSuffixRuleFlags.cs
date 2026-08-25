namespace Meziantou.Framework;

[Flags]
internal enum PublicSuffixRuleFlags : byte
{
    None = 0,

    /// <summary>An ICANN rule matching the suffix exactly.</summary>
    IcannRule = 1,

    /// <summary>A private rule matching the suffix exactly.</summary>
    PrivateRule = 2,

    /// <summary>An ICANN wildcard rule (<c>*.suffix</c>).</summary>
    IcannWildcard = 4,

    /// <summary>A private wildcard rule (<c>*.suffix</c>).</summary>
    PrivateWildcard = 8,

    /// <summary>An ICANN exception rule (<c>!suffix</c>).</summary>
    IcannException = 16,

    /// <summary>A private exception rule (<c>!suffix</c>).</summary>
    PrivateException = 32,
}
