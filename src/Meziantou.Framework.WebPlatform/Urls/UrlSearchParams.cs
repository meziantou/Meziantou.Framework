using System.Collections;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://url.spec.whatwg.org/#interface-urlsearchparams</summary>
[IdlInterface("URLSearchParams", "https://url.spec.whatwg.org/#urlsearchparams")]
public sealed class UrlSearchParams : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Url? _urlObject;
    private List<KeyValuePair<string, string>> _list;

    /// <summary>Creates an empty list of name-value pairs.</summary>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams")]
    public UrlSearchParams()
    {
        _list = [];
    }

    /// <summary>
    /// Creates a list of name-value pairs by parsing the given query string, ignoring a leading <c>?</c>.
    /// </summary>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams")]
    public UrlSearchParams(string init)
    {
        ArgumentNullException.ThrowIfNull(init);
        init = InfraString.ToScalarValueString(init);
        if (init.StartsWith('?', StringComparison.Ordinal))
        {
            init = init[1..];
        }

        _list = FormUrlEncoded.ParseString(init);
    }

    /// <summary>Initializes the object from a sequence of name-value pairs (each inner sequence must have exactly two items).</summary>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams")]
    public UrlSearchParams(IEnumerable<IReadOnlyList<string>> init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _list = [];
        foreach (var innerSequence in init)
        {
            if (innerSequence.Count != 2)
                throw new TypeErrorException("Each name-value pair must contain exactly two items");

            _list.Add(new(InfraString.ToScalarValueString(innerSequence[0]), InfraString.ToScalarValueString(innerSequence[1])));
        }
    }

    /// <summary>Initializes the object from a record.</summary>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams")]
    public UrlSearchParams(IEnumerable<KeyValuePair<string, string>> init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _list = [];
        foreach (var (name, value) in init)
        {
            _list.Add(new(InfraString.ToScalarValueString(name), InfraString.ToScalarValueString(value)));
        }
    }

    internal UrlSearchParams(Url url, string query)
    {
        _urlObject = url;
        _list = FormUrlEncoded.ParseString(query);
    }

    internal void SetList(List<KeyValuePair<string, string>> list) => _list = list;

    /// <summary>Gets the number of name-value pairs.</summary>
    [IdlMember("size", "https://url.spec.whatwg.org/#dom-urlsearchparams-size")]
    public uint Size => (uint)_list.Count;

    /// <summary>Appends a name-value pair.</summary>
    [IdlMember("append", "https://url.spec.whatwg.org/#dom-urlsearchparams-append")]
    public void Append(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _list.Add(new(InfraString.ToScalarValueString(name), InfraString.ToScalarValueString(value)));
        Update();
    }

    /// <summary>Removes the pairs with the given name, and the given value if specified.</summary>
    [IdlMember("delete", "https://url.spec.whatwg.org/#dom-urlsearchparams-delete")]
    public void Delete(string name, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(name);
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

    /// <summary>Returns the value of the first pair with the given name, or null when there is none.</summary>
    [IdlMember("get", "https://url.spec.whatwg.org/#dom-urlsearchparams-get")]
    public string? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = InfraString.ToScalarValueString(name);
        foreach (var tuple in _list)
        {
            if (tuple.Key == name)
                return tuple.Value;
        }

        return null;
    }

    /// <summary>Returns the values of all the pairs with the given name.</summary>
    [IdlMember("getAll", "https://url.spec.whatwg.org/#dom-urlsearchparams-getall")]
    public IReadOnlyList<string> GetAll(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = InfraString.ToScalarValueString(name);
        return _list.Where(tuple => tuple.Key == name).Select(tuple => tuple.Value).ToArray();
    }

    /// <summary>Returns whether a pair with the given name, and the given value if specified, exists.</summary>
    [IdlMember("has", "https://url.spec.whatwg.org/#dom-urlsearchparams-has")]
    public bool Has(string name, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        name = InfraString.ToScalarValueString(name);
        if (value is not null)
        {
            value = InfraString.ToScalarValueString(value);
            return _list.Exists(tuple => tuple.Key == name && tuple.Value == value);
        }

        return _list.Exists(tuple => tuple.Key == name);
    }

    /// <summary>
    /// Sets the value of the first pair with the given name and removes the other ones, or appends a pair when there is
    /// none.
    /// </summary>
    [IdlMember("set", "https://url.spec.whatwg.org/#dom-urlsearchparams-set")]
    public void Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
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

    /// <summary>Sorts the name-value pairs by name, comparing code units (stable sort).</summary>
    [IdlMember("sort", "https://url.spec.whatwg.org/#dom-urlsearchparams-sort")]
    public void Sort()
    {
        _list = [.. _list.OrderBy(tuple => tuple.Key, StringComparer.Ordinal)];
        Update();
    }

    /// <summary>Returns an enumerator over the name-value pairs.</summary>
    [IdlMember("iterable", "https://url.spec.whatwg.org/#urlsearchparams")]
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        // The iterator of a pair iterable is live: it reads the list at each step
        for (var i = 0; i < _list.Count; i++)
        {
            yield return _list[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Stringification behavior: https://url.spec.whatwg.org/#urlsearchparams-stringification-behavior</summary>
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
