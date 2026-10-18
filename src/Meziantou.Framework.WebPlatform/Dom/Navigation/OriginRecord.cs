using System.Globalization;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An origin (https://html.spec.whatwg.org/multipage/browsers.html#concept-origin): an opaque origin, which is only equal to
/// itself, or a tuple origin (scheme, host, port, domain). The domain is set by the <c>document.domain</c> setter, so the
/// documents sharing an origin (e.g. an <c>about:blank</c> document and its creator) see the change.
/// </summary>
internal sealed class OriginRecord
{
    private OriginRecord()
    {
    }

    private OriginRecord(string scheme, UrlHost host, int port)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
    }

    /// <summary>Gets whether the origin is opaque (https://html.spec.whatwg.org/multipage/browsers.html#concept-origin-opaque).</summary>
    public bool IsOpaque => Scheme is null;

    /// <summary>The scheme of a tuple origin.</summary>
    public string? Scheme { get; }

    /// <summary>The host of a tuple origin.</summary>
    public UrlHost Host { get; }

    /// <summary>The port of a tuple origin, or -1 for null.</summary>
    public int Port { get; } = -1;

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#concept-origin-domain: null, or the domain set by <c>document.domain</c>.</summary>
    public UrlHost? Domain { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#concept-origin-effective-domain</summary>
    public UrlHost? EffectiveDomain => IsOpaque ? null : Domain ?? Host;

    /// <summary>Creates a new opaque origin.</summary>
    public static OriginRecord CreateOpaque() => new();

    /// <summary>Creates a tuple origin.</summary>
    public static OriginRecord CreateTuple(string scheme, UrlHost host, int port) => new(scheme, host, port);

    /// <summary>The origin of a URL (https://url.spec.whatwg.org/#concept-url-origin): a new opaque origin for the URLs without a tuple origin.</summary>
    public static OriginRecord FromUrl(UrlRecord url)
    {
        switch (url.Scheme)
        {
            case "blob":
            {
                // The origin of the path URL, when its scheme is http, https or file
                var pathUrl = UrlParser.Parse(url.SerializePath(), baseUrl: null);
                return pathUrl is { Scheme: "http" or "https" } ? FromUrl(pathUrl) : CreateOpaque();
            }

            case "ftp" or "http" or "https" or "ws" or "wss":
                return new OriginRecord(url.Scheme, url.Host!.Value, url.Port);

            default:
                // The origin of a file: URL is implementation-defined: it is opaque
                return CreateOpaque();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#ascii-serialisation-of-an-origin</summary>
    public string Serialize()
    {
        if (IsOpaque)
            return "null";

        // The scheme, the host and the port are immutable (unlike the domain, which is not serialized)
        if (_serialized is { } serialized)
            return serialized;

        var result = Scheme + "://" + Host.Serialized;
        if (Port >= 0)
        {
            result += ":" + Port.ToString(CultureInfo.InvariantCulture);
        }

        return _serialized = result;
    }

    private string? _serialized;

    /// <summary>
    /// The serialization of the site of the origin (https://html.spec.whatwg.org/multipage/browsers.html#serialization-of-a-site):
    /// the scheme and the registrable domain of the host (or the host when it has none), or "null" for an opaque origin.
    /// </summary>
    public string SerializeSite()
    {
        if (IsOpaque)
            return "null";

        return _serializedSite ??= Scheme + "://" + (PublicSuffixList.GetRegistrableDomain(Host) ?? Host.Serialized);
    }

    private string? _serializedSite;

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#same-origin</summary>
    public static bool IsSameOrigin(OriginRecord a, OriginRecord b)
    {
        if (ReferenceEquals(a, b))
            return true;

        return !a.IsOpaque && !b.IsOpaque && a.Scheme == b.Scheme && a.Host == b.Host && a.Port == b.Port;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#same-origin-domain</summary>
    public static bool IsSameOriginDomain(OriginRecord a, OriginRecord b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a.IsOpaque || b.IsOpaque)
            return false;

        if (a.Domain is { } domainA && b.Domain is { } domainB)
            return a.Scheme == b.Scheme && domainA == domainB;

        return a.Domain is null && b.Domain is null && IsSameOrigin(a, b);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/browsers.html#same-site: schemelessly same site, with the same scheme.</summary>
    public static bool IsSameSite(OriginRecord a, OriginRecord b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a.IsOpaque || b.IsOpaque || a.Scheme != b.Scheme)
            return false;

        // https://html.spec.whatwg.org/multipage/browsers.html#schemelessly-same-site
        var registrableDomain = PublicSuffixList.GetRegistrableDomain(a.Host);
        if (registrableDomain is null)
            return a.Host == b.Host;

        return registrableDomain == PublicSuffixList.GetRegistrableDomain(b.Host);
    }

    /// <summary>
    /// The site of the origin (https://html.spec.whatwg.org/multipage/browsers.html#obtain-a-site), as a key with value
    /// equality: the origin itself when it is opaque, else its scheme and the registrable domain of its host (or the host).
    /// </summary>
    public object GetSiteKey()
    {
        if (IsOpaque)
            return this;

        return new SiteKey(Scheme!, PublicSuffixList.GetRegistrableDomain(Host) ?? Host.Serialized);
    }

    /// <summary>A key with value equality: the origin itself when it is opaque, else its scheme, host and port.</summary>
    public object GetOriginKey() => IsOpaque ? this : new TupleOriginKey(Scheme!, Host.Serialized, Port);

    private sealed record SiteKey(string Scheme, string Host);

    private sealed record TupleOriginKey(string Scheme, string Host, int Port);

    public override string ToString() => Serialize();
}
