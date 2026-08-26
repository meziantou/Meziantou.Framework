namespace Meziantou.Framework;

/// <summary>
/// Identifies the section of the <see href="https://publicsuffix.org/list/">Public Suffix List</see> a rule comes from.
/// </summary>
[Flags]
public enum PublicSuffixRuleSources
{
    /// <summary>No section. Reported when the implicit <c>*</c> rule was used, meaning the domain matched no rule of the list.</summary>
    None = 0,

    /// <summary>Suffixes delegated by ICANN or present in the IANA root zone.</summary>
    Icann = 1,

    /// <summary>Suffixes submitted by domain holders, such as <c>blogspot.com</c> or <c>github.io</c>.</summary>
    Private = 2,

    /// <summary>Both the ICANN and the private sections.</summary>
    All = Icann | Private,
}
