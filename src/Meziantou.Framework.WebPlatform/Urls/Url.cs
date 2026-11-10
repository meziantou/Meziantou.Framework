using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://url.spec.whatwg.org/#url-class</summary>
/// <seealso href="https://url.spec.whatwg.org/#url"/>
[IdlInterface("URL", "https://url.spec.whatwg.org/#url")]
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Name defined by the specification")]
public sealed class Url
{
    private readonly UrlApiObject _url;

    /// <summary>
    /// Creates a URL by parsing the given string against the optional base URL; throws a <c>TypeError</c> when it is
    /// invalid.
    /// </summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-url"/>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-url-url")]
    public Url(string url, string? @base = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = new UrlApiObject(UrlApiObject.ApiParse(url, @base) ?? throw new TypeErrorException($"Invalid URL '{url}'"));
        SearchParams = new UrlSearchParams(_url.SearchParams);
    }

    internal Url(UrlRecord url)
    {
        _url = new UrlApiObject(url);
        SearchParams = new UrlSearchParams(_url.SearchParams);
    }

    internal UrlRecord Record => _url.Record;

    /// <summary>
    /// Returns the URL obtained by parsing the given string against the optional base URL, or null when it is invalid.
    /// </summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-parse"/>
    [IdlMember("parse", "https://url.spec.whatwg.org/#dom-url-parse")]
    public static Url? Parse(string url, string? @base = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        var parsedUrl = UrlApiObject.ApiParse(url, @base);
        return parsedUrl is null ? null : new Url(parsedUrl);
    }

    /// <summary>Returns whether the given string can be parsed as a URL against the optional base URL.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-canparse"/>
    [IdlMember("canParse", "https://url.spec.whatwg.org/#dom-url-canparse")]
    public static bool CanParse(string url, string? @base = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        return UrlApiObject.ApiParse(url, @base) is not null;
    }

    /// <summary>
    /// Creates a <c>blob:</c> URL referencing a blob (https://w3c.github.io/FileAPI/#dfn-createObjectURL), until it is
    /// revoked or the document of the current global object is destroyed. The URL can be navigated to (e.g. the
    /// <c>src</c> of an iframe) by the documents of the browsing context group of the current global object.
    /// </summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-createObjectURL"/>
    [IdlMember("createObjectURL", "https://w3c.github.io/FileAPI/#dfn-createObjectURL")]
    public static string CreateObjectUrl(Blob obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var environment = Document.CurrentGlobalDocument;
        return BlobUrlStore.Get(environment).Add(obj, environment);
    }

    /// <summary>
    /// Creates a <c>blob:</c> URL referencing a media source (https://w3c.github.io/media-source/#mediasource-object-url),
    /// until it is revoked: a media element whose <c>src</c> attribute (or the one of a <c>source</c> child) is set to the URL
    /// attaches the source. The URL of a media source constructed in a dedicated worker cannot be used (the source is
    /// attached by its <see cref="MediaSource.Handle"/>).
    /// </summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-createObjectURL"/>
    [IdlMember("createObjectURL", "https://w3c.github.io/FileAPI/#dfn-createObjectURL")]
    public static string CreateObjectUrl(MediaSource obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var environment = Document.CurrentGlobalDocument;
        return BlobUrlStore.Get(environment).Add(obj, environment);
    }

    /// <summary>Revokes a <c>blob:</c> URL created by <see cref="CreateObjectUrl(Blob)"/> (https://w3c.github.io/FileAPI/#dfn-revokeObjectURL).</summary>
    /// <seealso href="https://w3c.github.io/FileAPI/#dfn-revokeObjectURL"/>
    [IdlMember("revokeObjectURL", "https://w3c.github.io/FileAPI/#dfn-revokeObjectURL")]
    public static void RevokeObjectUrl(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var urlRecord = UrlParser.Parse(url, baseUrl: null);
        if (urlRecord is null || urlRecord.Scheme != "blob")
            return;

        var environment = Document.CurrentGlobalDocument;
        BlobUrlStore.Get(environment).Remove(urlRecord, environment);
    }

    /// <summary>Gets or sets the serialized URL; setting an invalid URL throws a <c>TypeError</c>.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-href"/>
    [IdlMember("href", "https://url.spec.whatwg.org/#dom-url-href")]
    public string Href
    {
        get => _url.Href;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!_url.TrySetHref(value))
                throw new TypeErrorException($"Invalid URL '{value}'");
        }
    }

    /// <summary>Gets the serialized origin of the URL.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-origin"/>
    [IdlMember("origin", "https://url.spec.whatwg.org/#dom-url-origin")]
    public string Origin => _url.Origin;

    /// <summary>Gets or sets the scheme of the URL followed by a colon.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-protocol"/>
    [IdlMember("protocol", "https://url.spec.whatwg.org/#dom-url-protocol")]
    public string Protocol
    {
        get => _url.Protocol;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Protocol = value;
        }
    }

    /// <summary>Gets or sets the percent-encoded username of the URL.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-username"/>
    [IdlMember("username", "https://url.spec.whatwg.org/#dom-url-username")]
    public string Username
    {
        get => _url.Username;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Username = value;
        }
    }

    /// <summary>Gets or sets the percent-encoded password of the URL.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-password"/>
    [IdlMember("password", "https://url.spec.whatwg.org/#dom-url-password")]
    public string Password
    {
        get => _url.Password;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Password = value;
        }
    }

    /// <summary>Gets or sets the host of the URL, followed by the port when it is not the default one.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-host"/>
    [IdlMember("host", "https://url.spec.whatwg.org/#dom-url-host")]
    public string Host
    {
        get => _url.Host;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Host = value;
        }
    }

    /// <summary>Gets or sets the host of the URL, without the port.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-hostname"/>
    [IdlMember("hostname", "https://url.spec.whatwg.org/#dom-url-hostname")]
    public string Hostname
    {
        get => _url.Hostname;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Hostname = value;
        }
    }

    /// <summary>Gets or sets the port of the URL, or an empty string when it is the default one.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-port"/>
    [IdlMember("port", "https://url.spec.whatwg.org/#dom-url-port")]
    public string Port
    {
        get => _url.Port;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Port = value;
        }
    }

    /// <summary>Gets or sets the serialized path of the URL.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-pathname"/>
    [IdlMember("pathname", "https://url.spec.whatwg.org/#dom-url-pathname")]
    public string Pathname
    {
        get => _url.Pathname;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Pathname = value;
        }
    }

    /// <summary>Gets or sets the query of the URL, prefixed by <c>?</c>, or an empty string when it is empty.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-search"/>
    [IdlMember("search", "https://url.spec.whatwg.org/#dom-url-search")]
    public string Search
    {
        get => _url.Search;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Search = value;
        }
    }

    /// <summary>Gets the <c>URLSearchParams</c> object associated with the query of the URL.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-searchparams"/>
    [IdlMember("searchParams", "https://url.spec.whatwg.org/#dom-url-searchparams")]
    public UrlSearchParams SearchParams { get; }

    /// <summary>
    /// Gets or sets the fragment of the URL, prefixed by <c>#</c>, or an empty string when it is empty.
    /// </summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-hash"/>
    [IdlMember("hash", "https://url.spec.whatwg.org/#dom-url-hash")]
    public string Hash
    {
        get => _url.Hash;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _url.Hash = value;
        }
    }

    /// <summary>Returns the serialized URL, as <c>toJSON()</c> does.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-url-tojson"/>
    [IdlMember("toJSON", "https://url.spec.whatwg.org/#dom-url-tojson")]
    public string ToJson() => Href;

    /// <inheritdoc/>
    public override string ToString() => Href;
}
