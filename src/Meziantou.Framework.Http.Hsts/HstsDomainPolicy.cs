namespace Meziantou.Framework.Http;

/// <summary>Represents an HSTS (HTTP Strict Transport Security) policy for a specific domain.</summary>
public sealed class HstsDomainPolicy
{
    internal HstsDomainPolicy(string host, DateTimeOffset expiresAt, bool includeSubdomains, bool isPreloaded)
    {
        Host = host;
        ExpiresAt = expiresAt;
        IncludeSubdomains = includeSubdomains;
        IsPreloaded = isPreloaded;
    }

    /// <summary>Gets the domain host name for this HSTS policy.</summary>
    public string Host { get; }

    /// <summary>Gets the date and time when this HSTS policy expires.</summary>
    /// <remarks><see cref="DateTimeOffset.MaxValue"/> when <see cref="IsPreloaded"/> is <see langword="true"/>: the built-in list is compiled into the assembly and stays valid until the package is updated.</remarks>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Gets a value indicating whether the HSTS policy applies to subdomains of the host.</summary>
    public bool IncludeSubdomains { get; }

    /// <summary>Gets a value indicating whether the policy comes from the built-in HSTS preload list.</summary>
    /// <remarks>
    /// A preloaded policy never expires, and no <c>Strict-Transport-Security</c> response header can narrow it,
    /// expire it or remove it: a policy learned from a header may only widen the coverage of a preloaded host.
    /// Only <see cref="HstsDomainPolicyCollection.Remove"/> takes a host off the built-in list, and only for the
    /// collection it is called on.
    /// </remarks>
    public bool IsPreloaded { get; }

    public override string ToString()
    {
        // The date is part of a diagnostic string that ends up in logs, so it must not depend on the culture
        var result = Host + "; expires=" + ExpiresAt.ToString("O", CultureInfo.InvariantCulture);
        if (IncludeSubdomains)
        {
            result += "; includeSubdomains";
        }
        return result;
    }
}
