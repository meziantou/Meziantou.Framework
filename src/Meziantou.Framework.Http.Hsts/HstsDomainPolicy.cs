namespace Meziantou.Framework.Http;

/// <summary>Represents an HSTS (HTTP Strict Transport Security) policy for a specific domain.</summary>
public sealed class HstsDomainPolicy
{
    internal HstsDomainPolicy(string host, DateTimeOffset expiresAt, bool includeSubdomains)
    {
        Host = host;
        ExpiresAt = expiresAt;
        IncludeSubdomains = includeSubdomains;
    }

    /// <summary>Gets the domain host name for this HSTS policy.</summary>
    public string Host { get; }

    /// <summary>Gets the date and time when this HSTS policy expires.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets a value indicating whether the HSTS policy applies to subdomains of the host.</summary>
    public bool IncludeSubdomains { get; private set; }

    public override string ToString()
    {
        var result = Host + "; expires=" + ExpiresAt;
        if (IncludeSubdomains)
        {
            result += "; includeSubdomains";
        }
        return result;
    }
}