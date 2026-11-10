using System.Collections;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://url.spec.whatwg.org/#interface-urlsearchparams</summary>
/// <seealso href="https://url.spec.whatwg.org/#urlsearchparams"/>
[IdlInterface("URLSearchParams", "https://url.spec.whatwg.org/#urlsearchparams")]
public sealed class UrlSearchParams : IEnumerable<KeyValuePair<string, string>>
{
    private readonly UrlApiSearchParams _list;

    /// <summary>Creates an empty list of name-value pairs.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams"/>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams")]
    public UrlSearchParams()
    {
        _list = new UrlApiSearchParams();
    }

    /// <summary>
    /// Creates a list of name-value pairs by parsing the given query string, ignoring a leading <c>?</c>.
    /// </summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams"/>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams")]
    public UrlSearchParams(string init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _list = UrlApiSearchParams.FromQuery(init);
    }

    /// <summary>Initializes the object from a sequence of name-value pairs (each inner sequence must have exactly two items).</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams"/>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams")]
    public UrlSearchParams(IEnumerable<IReadOnlyList<string>> init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _list = new UrlApiSearchParams();
        foreach (var innerSequence in init)
        {
            if (innerSequence.Count != 2)
                throw new TypeErrorException("Each name-value pair must contain exactly two items");

            _list.AppendInitial(innerSequence[0], innerSequence[1]);
        }
    }

    /// <summary>Initializes the object from a record.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams"/>
    [IdlMember("constructor", "https://url.spec.whatwg.org/#dom-urlsearchparams-urlsearchparams")]
    public UrlSearchParams(IEnumerable<KeyValuePair<string, string>> init)
    {
        ArgumentNullException.ThrowIfNull(init);
        _list = new UrlApiSearchParams();
        foreach (var (name, value) in init)
        {
            _list.AppendInitial(name, value);
        }
    }

    internal UrlSearchParams(UrlApiSearchParams list)
    {
        _list = list;
    }

    /// <summary>Gets the number of name-value pairs.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-size"/>
    [IdlMember("size", "https://url.spec.whatwg.org/#dom-urlsearchparams-size")]
    public uint Size => (uint)_list.Count;

    /// <summary>Appends a name-value pair.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-append"/>
    [IdlMember("append", "https://url.spec.whatwg.org/#dom-urlsearchparams-append")]
    public void Append(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _list.Append(name, value);
    }

    /// <summary>Removes the pairs with the given name, and the given value if specified.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-delete"/>
    [IdlMember("delete", "https://url.spec.whatwg.org/#dom-urlsearchparams-delete")]
    public void Delete(string name, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        _list.Delete(name, value);
    }

    /// <summary>Returns the value of the first pair with the given name, or null when there is none.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-get"/>
    [IdlMember("get", "https://url.spec.whatwg.org/#dom-urlsearchparams-get")]
    public string? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _list.Get(name);
    }

    /// <summary>Returns the values of all the pairs with the given name.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-getall"/>
    [IdlMember("getAll", "https://url.spec.whatwg.org/#dom-urlsearchparams-getall")]
    public IReadOnlyList<string> GetAll(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _list.GetAll(name);
    }

    /// <summary>Returns whether a pair with the given name, and the given value if specified, exists.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-has"/>
    [IdlMember("has", "https://url.spec.whatwg.org/#dom-urlsearchparams-has")]
    public bool Has(string name, string? value = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _list.Has(name, value);
    }

    /// <summary>
    /// Sets the value of the first pair with the given name and removes the other ones, or appends a pair when there is
    /// none.
    /// </summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-set"/>
    [IdlMember("set", "https://url.spec.whatwg.org/#dom-urlsearchparams-set")]
    public void Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _list.Set(name, value);
    }

    /// <summary>Sorts the name-value pairs by name, comparing code units (stable sort).</summary>
    /// <seealso href="https://url.spec.whatwg.org/#dom-urlsearchparams-sort"/>
    [IdlMember("sort", "https://url.spec.whatwg.org/#dom-urlsearchparams-sort")]
    public void Sort() => _list.Sort();

    /// <summary>Returns an enumerator over the name-value pairs.</summary>
    /// <seealso href="https://url.spec.whatwg.org/#urlsearchparams"/>
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
    public override string ToString() => _list.ToString();
}
