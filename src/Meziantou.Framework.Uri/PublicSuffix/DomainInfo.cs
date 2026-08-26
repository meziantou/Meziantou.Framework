namespace Meziantou.Framework;

/// <summary>
/// The decomposition of a domain name according to the <see href="https://publicsuffix.org/list/">Public Suffix List</see>.
/// </summary>
public readonly record struct DomainInfo
{
    private readonly string? _domain;
    private readonly string? _publicSuffix;

    internal DomainInfo(string domain, string publicSuffix, string? registrableDomain, string? subdomain, PublicSuffixRuleSources source, bool isKnownPublicSuffix)
    {
        _domain = domain;
        _publicSuffix = publicSuffix;
        RegistrableDomain = registrableDomain;
        Subdomain = subdomain;
        Source = source;
        IsKnownPublicSuffix = isKnownPublicSuffix;
    }

    /// <summary>The normalized domain name: lower-cased and stripped of its trailing dot. The IDN form of the input is preserved.</summary>
    public string Domain => _domain ?? "";

    /// <summary>The public suffix, also known as the effective top-level domain (eTLD). For instance <c>co.uk</c> for <c>www.example.co.uk</c>.</summary>
    public string PublicSuffix => _publicSuffix ?? "";

    /// <summary>The public suffix plus one label (eTLD+1), or <see langword="null"/> when the domain is itself a public suffix. For instance <c>example.co.uk</c> for <c>www.example.co.uk</c>.</summary>
    public string? RegistrableDomain { get; }

    /// <summary>The labels before the registrable domain, or <see langword="null"/> when there is none. For instance <c>www</c> for <c>www.example.co.uk</c>.</summary>
    public string? Subdomain { get; }

    /// <summary>The section of the list the matching rule belongs to, or <see cref="PublicSuffixRuleSources.None"/> when no rule matched.</summary>
    public PublicSuffixRuleSources Source { get; }

    /// <summary><see langword="true"/> when a rule of the list matched; <see langword="false"/> when the implicit <c>*</c> rule was used.</summary>
    public bool IsKnownPublicSuffix { get; }

    public override string ToString() => Domain;
}
