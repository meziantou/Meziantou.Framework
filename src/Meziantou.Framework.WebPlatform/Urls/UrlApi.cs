using System.Globalization;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Urls;

// The algorithms of the URL and URLSearchParams classes (https://url.spec.whatwg.org/#api), without the bindings: the
// public classes of WebPlatform and the web APIs of Meziantou.Framework.JavaScriptEngine.WebApis (which links this file)
// wrap them. The failures are reported to the caller, which throws its own exception type.

/// <summary>The state of a <c>URL</c> object (https://url.spec.whatwg.org/#url-class).</summary>
internal sealed class UrlApiObject
{
    private UrlRecord _url;

    public UrlApiObject(UrlRecord url)
    {
        _url = url;
        SearchParams = new UrlApiSearchParams(this, url.Query ?? "");
    }

    public UrlRecord Record => _url;

    public UrlApiSearchParams SearchParams { get; }

    /// <summary>https://url.spec.whatwg.org/#api-url-parser</summary>
    public static UrlRecord? ApiParse(string url, string? @base)
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

    public string Href => _url.Serialize();

    /// <summary>Sets the href; returns false (and leaves the URL unchanged) when the value is not a valid URL.</summary>
    public bool TrySetHref(string value)
    {
        var parsedUrl = UrlParser.Parse(value, baseUrl: null);
        if (parsedUrl is null)
            return false;

        _url = parsedUrl;
        SearchParams.SetList(_url.Query is null ? [] : FormUrlEncoded.ParseString(_url.Query));
        return true;
    }

    public string Origin => _url.SerializeOrigin();

    public string Protocol
    {
        get => _url.Scheme + ":";
        set => UrlParser.ParseWithStateOverride(value + ":", _url, UrlParserState.SchemeStart);
    }

    public string Username
    {
        get => _url.Username;
        set
        {
            if (_url.CannotHaveUsernamePasswordPort)
                return;

            _url.Username = PercentEncoding.Utf8PercentEncode(value, PercentEncodeSet.Userinfo);
        }
    }

    public string Password
    {
        get => _url.Password;
        set
        {
            if (_url.CannotHaveUsernamePasswordPort)
                return;

            _url.Password = PercentEncoding.Utf8PercentEncode(value, PercentEncodeSet.Userinfo);
        }
    }

    public string Host
    {
        get
        {
            if (_url.Host is not { } host)
                return "";

            if (_url.Port < 0)
                return host.Serialized;

            return host.Serialized + ":" + _url.Port.ToString(CultureInfo.InvariantCulture);
        }
        set
        {
            if (_url.HasOpaquePath)
                return;

            UrlParser.ParseWithStateOverride(value, _url, UrlParserState.Host);
        }
    }

    public string Hostname
    {
        get => _url.Host?.Serialized ?? "";
        set
        {
            if (_url.HasOpaquePath)
                return;

            UrlParser.ParseWithStateOverride(value, _url, UrlParserState.Hostname);
        }
    }

    public string Port
    {
        get => _url.Port < 0 ? "" : _url.Port.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (_url.CannotHaveUsernamePasswordPort)
                return;

            if (value.Length == 0)
            {
                _url.Port = -1;
                return;
            }

            UrlParser.ParseWithStateOverride(value, _url, UrlParserState.Port);
        }
    }

    public string Pathname
    {
        get => _url.SerializePath();
        set
        {
            if (_url.HasOpaquePath)
                return;

            // The path is emptied before parsing; parsing failures leave the URL with an empty path, as specified.
            _url.Path.Clear();
            UrlParser.ParseWithStateOverride(value, _url, UrlParserState.PathStart);
        }
    }

    public string Search
    {
        get => string.IsNullOrEmpty(_url.Query) ? "" : "?" + _url.Query;
        set
        {
            if (value.Length == 0)
            {
                _url.Query = null;
                SearchParams.SetList([]);
                return;
            }

            var input = value.StartsWith('?', StringComparison.Ordinal) ? value[1..] : value;
            _url.Query = "";
            UrlParser.ParseWithStateOverride(input, _url, UrlParserState.Query);
            SearchParams.SetList(FormUrlEncoded.ParseString(_url.Query ?? ""));
        }
    }

    public string Hash
    {
        get => string.IsNullOrEmpty(_url.Fragment) ? "" : "#" + _url.Fragment;
        set
        {
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

    /// <summary>Called by <see cref="UrlApiSearchParams"/> to update the query ("update" steps).</summary>
    internal void SetQuery(string? query) => _url.Query = query;
}

/// <summary>The list of a <c>URLSearchParams</c> object (https://url.spec.whatwg.org/#interface-urlsearchparams).</summary>
internal sealed class UrlApiSearchParams
{
    private readonly UrlApiObject? _urlObject;
    private List<KeyValuePair<string, string>> _list;

    public UrlApiSearchParams()
    {
        _list = [];
    }

    internal UrlApiSearchParams(UrlApiObject url, string query)
    {
        _urlObject = url;
        _list = FormUrlEncoded.ParseString(query);
    }

    /// <summary>Creates a list by parsing the given query string, ignoring a leading <c>?</c>.</summary>
    public static UrlApiSearchParams FromQuery(string init)
    {
        init = InfraString.ToScalarValueString(init);
        if (init.StartsWith('?', StringComparison.Ordinal))
        {
            init = init[1..];
        }

        var result = new UrlApiSearchParams();
        result._list = FormUrlEncoded.ParseString(init);
        return result;
    }

    internal void SetList(List<KeyValuePair<string, string>> list) => _list = list;

    public int Count => _list.Count;

    public KeyValuePair<string, string> this[int index] => _list[index];

    /// <summary>Appends a pair without running the update steps (initialization of the object).</summary>
    public void AppendInitial(string name, string value) => _list.Add(new(InfraString.ToScalarValueString(name), InfraString.ToScalarValueString(value)));

    public void Append(string name, string value)
    {
        AppendInitial(name, value);
        Update();
    }

    public void Delete(string name, string? value)
    {
        name = InfraString.ToScalarValueString(name);
        if (value is not null)
        {
            value = InfraString.ToScalarValueString(value);
            _list.RemoveAll(tuple => tuple.Key == name && tuple.Value == value);
        }
        else
        {
            _list.RemoveAll(tuple => tuple.Key == name);
        }

        Update();
    }

    public string? Get(string name)
    {
        name = InfraString.ToScalarValueString(name);
        foreach (var tuple in _list)
        {
            if (tuple.Key == name)
                return tuple.Value;
        }

        return null;
    }

    public string[] GetAll(string name)
    {
        name = InfraString.ToScalarValueString(name);
        return _list.Where(tuple => tuple.Key == name).Select(tuple => tuple.Value).ToArray();
    }

    public bool Has(string name, string? value)
    {
        name = InfraString.ToScalarValueString(name);
        if (value is not null)
        {
            value = InfraString.ToScalarValueString(value);
            return _list.Exists(tuple => tuple.Key == name && tuple.Value == value);
        }

        return _list.Exists(tuple => tuple.Key == name);
    }

    public void Set(string name, string value)
    {
        name = InfraString.ToScalarValueString(name);
        value = InfraString.ToScalarValueString(value);
        var index = _list.FindIndex(tuple => tuple.Key == name);
        if (index >= 0)
        {
            _list[index] = new(name, value);
            for (var i = _list.Count - 1; i > index; i--)
            {
                if (_list[i].Key == name)
                {
                    _list.RemoveAt(i);
                }
            }
        }
        else
        {
            _list.Add(new(name, value));
        }

        Update();
    }

    /// <summary>Sorts the pairs by name, comparing code units (stable sort).</summary>
    public void Sort()
    {
        _list = [.. _list.OrderBy(tuple => tuple.Key, StringComparer.Ordinal)];
        Update();
    }

    public override string ToString() => FormUrlEncoded.Serialize(_list);

    /// <summary>https://url.spec.whatwg.org/#concept-urlsearchparams-update</summary>
    private void Update()
    {
        if (_urlObject is null)
            return;

        var serializedQuery = ToString();
        _urlObject.SetQuery(serializedQuery.Length == 0 ? null : serializedQuery);
    }
}
