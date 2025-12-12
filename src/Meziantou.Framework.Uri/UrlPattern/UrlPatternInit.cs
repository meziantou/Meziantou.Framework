namespace Meziantou.Framework;

/// <summary>Initialization dictionary for creating a URLPattern.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#dictdef-urlpatterninit">WHATWG URL Pattern Spec - URLPatternInit</see>
/// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URLPattern/URLPattern">MDN - URLPattern constructor</see>
/// </remarks>
public sealed class UrlPatternInit // TODO remove and use named parameters in UrlPattern.Create? 
{
    /// <summary>Gets or sets the protocol pattern string.</summary>
    public string? Protocol { get; set; }

    /// <summary>Gets or sets the username pattern string.</summary>
    public string? Username { get; set; }

    /// <summary>Gets or sets the password pattern string.</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets the hostname pattern string.</summary>
    public string? Hostname { get; set; }

    /// <summary>Gets or sets the port pattern string.</summary>
    public string? Port { get; set; }

    /// <summary>Gets or sets the pathname pattern string.</summary>
    public string? Pathname { get; set; }

    /// <summary>Gets or sets the search (query) pattern string.</summary>
    public string? Search { get; set; }

    /// <summary>Gets or sets the hash (fragment) pattern string.</summary>
    public string? Hash { get; set; }

    /// <summary>Gets or sets the base URL to use for resolving relative patterns.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
    public string? BaseUrl { get; set; }
}
