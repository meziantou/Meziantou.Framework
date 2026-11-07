namespace Meziantou.Framework;

/// <summary>Specifies the flags of Unicode IDNA Compatibility Processing used by <see cref="UnicodeIdna"/>.</summary>
/// <remarks>The default values are the ones of the standard for the registration and the lookup of a domain name.</remarks>
/// <seealso href="https://www.unicode.org/reports/tr46/#Processing" />
public sealed class UnicodeIdnaOptions
{
    /// <summary>Gets the default options: every check is enabled, with nontransitional processing.</summary>
    public static UnicodeIdnaOptions Default { get; } = new();

    /// <summary>Gets a value indicating whether a label cannot start or end with a hyphen, or have hyphens in its third and fourth positions (CheckHyphens). The default is <see langword="true"/>.</summary>
    public bool CheckHyphens { get; init; } = true;

    /// <summary>Gets a value indicating whether the labels of a domain name that contains right-to-left characters must satisfy the Bidi Rule of RFC 5893 (CheckBidi). The default is <see langword="true"/>.</summary>
    public bool CheckBidi { get; init; } = true;

    /// <summary>Gets a value indicating whether the zero-width joiner and non-joiner must satisfy the ContextJ rules of RFC 5892 (CheckJoiners). The default is <see langword="true"/>.</summary>
    public bool CheckJoiners { get; init; } = true;

    /// <summary>Gets a value indicating whether the only ASCII characters a label accepts are the lowercase letters, the digits and the hyphen (UseSTD3ASCIIRules). The default is <see langword="true"/>.</summary>
    public bool UseStd3AsciiRules { get; init; } = true;

    /// <summary>Gets a value indicating whether the deviation characters, such as <c>ß</c>, are mapped as in IDNA2003 (Transitional_Processing). The default is <see langword="false"/>.</summary>
    /// <remarks>The standard deprecates transitional processing.</remarks>
    public bool TransitionalProcessing { get; init; }

    /// <summary>Gets a value indicating whether <see cref="UnicodeIdna.TryToAscii(string, UnicodeIdnaOptions?, out string?)"/> rejects a domain name or a label that is empty or too long for the DNS (VerifyDnsLength). The default is <see langword="true"/>.</summary>
    public bool VerifyDnsLength { get; init; } = true;

    /// <summary>Gets a value indicating whether a label that starts with <c>xn--</c> but is not valid Punycode is kept as it is and checked like a decoded label, instead of being an error right away (IgnoreInvalidPunycode). The default is <see langword="false"/>.</summary>
    /// <remarks>
    /// As UTS #46 specifies, the kept label is still an error: it contains only ASCII characters, which is an error for a
    /// label that starts with <c>xn--</c>, and it does not satisfy the validity criteria either (its hyphens, or its
    /// <c>xn--</c> prefix when <see cref="CheckHyphens"/> is <see langword="false"/>). So the option does not make such a
    /// domain name valid: <see cref="UnicodeIdna.TryToAscii(string, UnicodeIdnaOptions?, out string?)"/> rejects it, and
    /// <see cref="UnicodeIdna.ToUnicode(string, UnicodeIdnaOptions?, out bool)"/> keeps the label and reports an error,
    /// with or without the option.
    /// </remarks>
    public bool IgnoreInvalidPunycode { get; init; }
}
