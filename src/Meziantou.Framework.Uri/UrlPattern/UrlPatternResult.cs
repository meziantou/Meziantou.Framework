using System.Collections.ObjectModel;

namespace Meziantou.Framework;

// https://urlpattern.spec.whatwg.org/#urlpatternresult

/// <summary>Represents the result of executing a URL pattern against a URL.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#urlpatternresult">WHATWG URL Pattern Spec - URLPatternResult</see>
/// <see href="https://developer.mozilla.org/en-US/docs/Web/API/URLPattern/exec#return_value">MDN - URLPattern.exec() return value</see>
/// </remarks>
public sealed class UrlPatternResult
{
    /// <summary>Gets the input URL or URL components that were matched.</summary>
    public ReadOnlyCollection<UrlPatternInput> Inputs { get; } // TODO IReadOnlyCollection / List

    /// <summary>Gets the protocol component result.</summary>
    public UrlPatternComponentResult Protocol { get; }

    /// <summary>Gets the username component result.</summary>
    public UrlPatternComponentResult Username { get; }

    /// <summary>Gets the password component result.</summary>
    public UrlPatternComponentResult Password { get; }

    /// <summary>Gets the hostname component result.</summary>
    public UrlPatternComponentResult Hostname { get; }

    /// <summary>Gets the port component result.</summary>
    public UrlPatternComponentResult Port { get; }

    /// <summary>Gets the pathname component result.</summary>
    public UrlPatternComponentResult Pathname { get; }

    /// <summary>Gets the search component result.</summary>
    public UrlPatternComponentResult Search { get; }

    /// <summary>Gets the hash component result.</summary>
    public UrlPatternComponentResult Hash { get; }

    internal UrlPatternResult(
        UrlPatternInput[] inputs,
        UrlPatternComponentResult protocol,
        UrlPatternComponentResult username,
        UrlPatternComponentResult password,
        UrlPatternComponentResult hostname,
        UrlPatternComponentResult port,
        UrlPatternComponentResult pathname,
        UrlPatternComponentResult search,
        UrlPatternComponentResult hash)
    {
        Inputs = new ReadOnlyCollection<UrlPatternInput>(inputs);
        Protocol = protocol;
        Username = username;
        Password = password;
        Hostname = hostname;
        Port = port;
        Pathname = pathname;
        Search = search;
        Hash = hash;
    }
}
