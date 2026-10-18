using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://url.spec.whatwg.org/#url-class</summary>
[IdlInterface("URL", "https://url.spec.whatwg.org/#url")]
[SuppressMessage("Naming", "CA1724:Type names should not match namespaces", Justification = "Name defined by the specification")]
public sealed class Url
{
    private UrlRecord _url;

    /// <summary>
    /// Creates a URL by parsing the given string against the optional base URL; throws a <c>TypeError</c> when it is
    /// invalid.
    /// </summary>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-url-url")]
    public Url(string url, string? @base = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        _url = ApiParse(url, @base) ?? throw new TypeErrorException($"Invalid URL '{url}'");
        SearchParams = new UrlSearchParams(this, _url.Query ?? "");
    }

    internal Url(UrlRecord url)
    {
        _url = url;
        SearchParams = new UrlSearchParams(this, _url.Query ?? "");
    }

    internal UrlRecord Record => _url;

    /// <summary>https://url.spec.whatwg.org/#api-url-parser</summary>
    private static UrlRecord? ApiParse(string url, string? @base)
    {
        UrlRecord? parsedBase = null;
        if (@base is not null)
        {
            parsedBase = UrlParser.Parse(@base, baseUrl: null);
            if (parsedBase is null)
                return null;
        }

        return UrlParser.Parse(url, parsedBase);
    }

    /// <summary>
    /// Returns the URL obtained by parsing the given string against the optional base URL, or null when it is invalid.
    /// </summary>
    [IdlMember("parse", "https://url.spec.whatwg.org/#dom-url-parse")]
    public static Url? Parse(string url, string? @base = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        var parsedUrl = ApiParse(url, @base);
        return parsedUrl is null ? null : new Url(parsedUrl);
    }

    /// <summary>Returns whether the given string can be parsed as a URL against the optional base URL.</summary>
    [IdlMember("canParse", "https://url.spec.whatwg.org/#dom-url-canparse")]
    public static bool CanParse(string url, string? @base = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        return ApiParse(url, @base) is not null;
    }

    /// <summary>
    /// Creates a <c>blob:</c> URL referencing a blob (https://w3c.github.io/FileAPI/#dfn-createObjectURL), until it is
    /// revoked or the document of the current global object is destroyed. The URL can be navigated to (e.g. the
    /// <c>src</c> of an iframe) by the documents of the browsing context group of the current global object.
    /// </summary>
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
    [IdlMember("createObjectURL", "https://w3c.github.io/FileAPI/#dfn-createObjectURL")]
    public static string CreateObjectUrl(MediaSource obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        var environment = Document.CurrentGlobalDocument;
        return BlobUrlStore.Get(environment).Add(obj, environment);
    }

    /// <summary>Revokes a <c>blob:</c> URL created by <see cref="CreateObjectUrl(Blob)"/> (https://w3c.github.io/FileAPI/#dfn-revokeObjectURL).</summary>
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
    [IdlMember("href", "https://url.spec.whatwg.org/#dom-url-href")]
    public string Href
    {
        get => _url.Serialize();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var parsedUrl = UrlParser.Parse(value, baseUrl: null) ?? throw new TypeErrorException($"Invalid URL '{value}'");
            _url = parsedUrl;
            SearchParams.SetList(_url.Query is null ? [] : FormUrlEncoded.ParseString(_url.Query));
        }
    }

    /// <summary>Gets the serialized origin of the URL.</summary>
    [IdlMember("origin", "https://url.spec.whatwg.org/#dom-url-origin")]
    public string Origin => _url.SerializeOrigin();

    /// <summary>Gets or sets the scheme of the URL followed by a colon.</summary>
    [IdlMember("protocol", "https://url.spec.whatwg.org/#dom-url-protocol")]
    public string Protocol
    {
        get => _url.Scheme + ":";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            UrlParser.ParseWithStateOverride(value + ":", _url, UrlParserState.SchemeStart);
        }
    }

    /// <summary>Gets or sets the percent-encoded username of the URL.</summary>
    [IdlMember("username", "https://url.spec.whatwg.org/#dom-url-username")]
    public string Username
    {
        get => _url.Username;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_url.CannotHaveUsernamePasswordPort)
                return;

            _url.Username = PercentEncoding.Utf8PercentEncode(value, PercentEncodeSet.Userinfo);
        }
    }

    /// <summary>Gets or sets the percent-encoded password of the URL.</summary>
    [IdlMember("password", "https://url.spec.whatwg.org/#dom-url-password")]
    public string Password
    {
        get => _url.Password;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_url.CannotHaveUsernamePasswordPort)
                return;

            _url.Password = PercentEncoding.Utf8PercentEncode(value, PercentEncodeSet.Userinfo);
        }
    }

    /// <summary>Gets or sets the host of the URL, followed by the port when it is not the default one.</summary>
    [IdlMember("host", "https://url.spec.whatwg.org/#dom-url-host")]
    public string Host
    {
        get
        {
            if (_url.Host is not { } host)
                return "";

            if (_url.Port < 0)
                return host.Serialized;

            return host.Serialized + ":" + _url.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_url.HasOpaquePath)
                return;

            ParseComponent(value, UrlParserState.Host);
        }
    }

    /// <summary>Gets or sets the host of the URL, without the port.</summary>
    [IdlMember("hostname", "https://url.spec.whatwg.org/#dom-url-hostname")]
    public string Hostname
    {
        get => _url.Host?.Serialized ?? "";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_url.HasOpaquePath)
                return;

            ParseComponent(value, UrlParserState.Hostname);
        }
    }

    /// <summary>Gets or sets the port of the URL, or an empty string when it is the default one.</summary>
    [IdlMember("port", "https://url.spec.whatwg.org/#dom-url-port")]
    public string Port
    {
        get => _url.Port < 0 ? "" : _url.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_url.CannotHaveUsernamePasswordPort)
                return;

            if (value.Length == 0)
            {
                _url.Port = -1;
                return;
            }

            ParseComponent(value, UrlParserState.Port);
        }
    }

    /// <summary>Gets or sets the serialized path of the URL.</summary>
    [IdlMember("pathname", "https://url.spec.whatwg.org/#dom-url-pathname")]
    public string Pathname
    {
        get => _url.SerializePath();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_url.HasOpaquePath)
                return;

            // The path is emptied before parsing; parsing failures leave the URL with an empty path, as specified.
            _url.Path.Clear();
            UrlParser.ParseWithStateOverride(value, _url, UrlParserState.PathStart);
        }
    }

    /// <summary>Gets or sets the query of the URL, prefixed by <c>?</c>, or an empty string when it is empty.</summary>
    [IdlMember("search", "https://url.spec.whatwg.org/#dom-url-search")]
    public string Search
    {
        get => string.IsNullOrEmpty(_url.Query) ? "" : "?" + _url.Query;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length == 0)
            {
                _url.Query = null;
                SearchParams.SetList([]);
                return;
            }

            var input = value.StartsWith('?', StringComparison.Ordinal) ? value[1..] : value;
            _url.Query = "";
            UrlParser.ParseWithStateOverride(input, _url, UrlParserState.Query);
            SearchParams.SetList(FormUrlEncoded.ParseString(InfraString.ToScalarValueString(input)));
        }
    }

    /// <summary>Gets the <c>URLSearchParams</c> object associated with the query of the URL.</summary>
    [IdlMember("searchParams", "https://url.spec.whatwg.org/#dom-url-searchparams")]
    public UrlSearchParams SearchParams { get; }

    /// <summary>
    /// Gets or sets the fragment of the URL, prefixed by <c>#</c>, or an empty string when it is empty.
    /// </summary>
    [IdlMember("hash", "https://url.spec.whatwg.org/#dom-url-hash")]
    public string Hash
    {
        get => string.IsNullOrEmpty(_url.Fragment) ? "" : "#" + _url.Fragment;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length == 0)
            {
                _url.Fragment = null;
                return;
            }

            var input = value.StartsWith('#', StringComparison.Ordinal) ? value[1..] : value;
            _url.Fragment = "";
            UrlParser.ParseWithStateOverride(input, _url, UrlParserState.Fragment);
        }
    }

    /// <summary>Returns the serialized URL, as <c>toJSON()</c> does.</summary>
    [IdlMember("toJSON", "https://url.spec.whatwg.org/#dom-url-tojson")]
    public string ToJson() => Href;

    /// <inheritdoc/>
    public override string ToString() => Href;

    /// <summary>Called by <see cref="UrlSearchParams"/> to update the query ("update" steps).</summary>
    internal void SetQuery(string? query) => _url.Query = query;

    /// <summary>
    /// Runs the basic URL parser with a state override on a copy of the URL, and keeps the result only on success
    /// (the parser can modify the URL before failing, but the setters are specified to leave it unchanged in that case... see below).
    /// </summary>
    private void ParseComponent(string value, UrlParserState state)
    {
        // The spec runs the parser directly on this's URL. A failure can happen after some components were set
        // (e.g. the host state sets the host before the port fails), which is the specified behavior, so the
        // parser runs in place.
        UrlParser.ParseWithStateOverride(value, _url, state);
    }
}
