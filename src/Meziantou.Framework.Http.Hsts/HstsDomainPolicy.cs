namespace Meziantou.Framework.Http;

public sealed class HstsDomainPolicy
{
    internal HstsDomainPolicy(string host, DateTimeOffset expiresAt, bool includeSubdomains)
    {
        Host = host;
        ExpiresAt = expiresAt;
        IncludeSubdomains = includeSubdomains;
    }

    public string Host { get; }
    public DateTimeOffset ExpiresAt { get; private set; }
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