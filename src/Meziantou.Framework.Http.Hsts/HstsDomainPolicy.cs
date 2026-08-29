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
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Gets a value indicating whether the HSTS policy applies to subdomains of the host.</summary>
    public bool IncludeSubdomains { get; }

    /// <summary>Gets a value indicating whether the policy comes from the built-in HSTS preload list.</summary>
    /// <remarks>A preloaded policy never expires and is not removed by a <c>max-age=0</c> response header.</remarks>
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
