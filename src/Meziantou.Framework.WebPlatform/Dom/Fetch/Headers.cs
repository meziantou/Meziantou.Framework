using System.Collections;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The guard of a <see cref="Headers"/> object (https://fetch.spec.whatwg.org/#concept-headers-guard).</summary>
internal enum HeadersGuard
{
    None,
    Request,
    RequestNoCors,
    Response,
    Immutable,
}

/// <summary>
/// A list of HTTP headers (https://fetch.spec.whatwg.org/#headers-class): the headers of a request or a response of the
/// Fetch API, or a list created by <c>new Headers()</c>. The names and the values are byte strings (each character is a
/// byte, in the range U+0000 to U+00FF). The names are compared ASCII case-insensitively, and the iteration returns the
/// lowercase names, sorted, with the values of the headers with the same name combined (except <c>Set-Cookie</c>).
/// </summary>
/// <seealso href="https://fetch.spec.whatwg.org/#headers-class"/>
[IdlInterface("Headers", "https://fetch.spec.whatwg.org/#headers-class")]
public sealed class Headers : IEnumerable<KeyValuePair<string, string>>
{
    /// <summary>Creates an empty list.</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers"/>
    [IdlMember("constructor", "https://fetch.spec.whatwg.org/#dom-headers")]
    public Headers()
    {
    }

    /// <summary>Creates a list from pairs of a name and a value (<c>sequence&lt;sequence&lt;ByteString&gt;&gt;</c>).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers"/>
    [IdlMember("constructor", "https://fetch.spec.whatwg.org/#dom-headers")]
    public Headers(IEnumerable<IReadOnlyList<string>> init)
    {
        ArgumentNullException.ThrowIfNull(init);
        Fill(init);
    }

    /// <summary>Creates a list from the entries of a record (<c>record&lt;ByteString, ByteString&gt;</c>).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers"/>
    [IdlMember("constructor", "https://fetch.spec.whatwg.org/#dom-headers")]
    public Headers(IEnumerable<KeyValuePair<string, string>> init)
    {
        ArgumentNullException.ThrowIfNull(init);
        Fill(init);
    }

    internal Headers(List<KeyValuePair<string, string>> list, HeadersGuard guard)
    {
        List = list;
        Guard = guard;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-headers-header-list: shared with the request or the response.</summary>
    internal List<KeyValuePair<string, string>> List { get; set; } = [];

    /// <summary>https://fetch.spec.whatwg.org/#concept-headers-guard</summary>
    internal HeadersGuard Guard { get; set; }

    /// <summary>https://fetch.spec.whatwg.org/#concept-headers-fill</summary>
    internal void Fill(IEnumerable<IReadOnlyList<string>> init)
    {
        foreach (var header in init)
        {
            if (header.Count != 2)
                throw new TypeErrorException("A header must be a pair of a name and a value");

            AppendCore(header[0], header[1]);
        }
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-headers-fill</summary>
    internal void Fill(IEnumerable<KeyValuePair<string, string>> init)
    {
        foreach (var (name, value) in init)
        {
            AppendCore(name, value);
        }
    }

    /// <summary>Appends a header (https://fetch.spec.whatwg.org/#dom-headers-append).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers-append"/>
    [IdlMember("append", "https://fetch.spec.whatwg.org/#dom-headers-append")]
    public void Append(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        AppendCore(name, value);
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-headers-append</summary>
    private void AppendCore(string name, string value)
    {
        value = FetchHeaders.Normalize(value);
        if (!Validate(name, value))
            return;

        if (Guard == HeadersGuard.RequestNoCors)
        {
            var temporaryValue = HeaderList.Get(List, name);
            temporaryValue = temporaryValue is null ? value : temporaryValue + ", " + value;
            if (!FetchHeaders.IsNoCorsSafelistedRequestHeader(name, temporaryValue))
                return;
        }

        FetchHeaders.Append(List, name, value);
        if (Guard == HeadersGuard.RequestNoCors)
        {
            RemovePrivilegedNoCorsRequestHeaders();
        }
    }

    /// <summary>Removes the headers with the name (https://fetch.spec.whatwg.org/#dom-headers-delete).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers-delete"/>
    [IdlMember("delete", "https://fetch.spec.whatwg.org/#dom-headers-delete")]
    public void Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!Validate(name, ""))
            return;

        if (Guard == HeadersGuard.RequestNoCors && !FetchHeaders.IsNoCorsSafelistedRequestHeaderName(name) && !FetchHeaders.IsPrivilegedNoCorsRequestHeaderName(name))
            return;

        if (!FetchHeaders.Contains(List, name))
            return;

        FetchHeaders.Delete(List, name);
        if (Guard == HeadersGuard.RequestNoCors)
        {
            RemovePrivilegedNoCorsRequestHeaders();
        }
    }

    /// <summary>Returns the values of the headers with the name, separated by <c>", "</c>, or null (https://fetch.spec.whatwg.org/#dom-headers-get).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers-get"/>
    [IdlMember("get", "https://fetch.spec.whatwg.org/#dom-headers-get")]
    public string? Get(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!FetchHeaders.IsHeaderName(name))
            throw new TypeErrorException($"'{name}' is not a valid header name");

        return HeaderList.Get(List, name);
    }

    /// <summary>Returns the values of the <c>Set-Cookie</c> headers (https://fetch.spec.whatwg.org/#dom-headers-getsetcookie).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers-getsetcookie"/>
    [IdlMember("getSetCookie", "https://fetch.spec.whatwg.org/#dom-headers-getsetcookie")]
    public IReadOnlyList<string> GetSetCookie()
    {
        var values = new List<string>();
        foreach (var (name, value) in List)
        {
            if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>Returns whether there is a header with the name (https://fetch.spec.whatwg.org/#dom-headers-has).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers-has"/>
    [IdlMember("has", "https://fetch.spec.whatwg.org/#dom-headers-has")]
    public bool Has(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!FetchHeaders.IsHeaderName(name))
            throw new TypeErrorException($"'{name}' is not a valid header name");

        return FetchHeaders.Contains(List, name);
    }

    /// <summary>Replaces the headers with the name (https://fetch.spec.whatwg.org/#dom-headers-set).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#dom-headers-set"/>
    [IdlMember("set", "https://fetch.spec.whatwg.org/#dom-headers-set")]
    public void Set(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        value = FetchHeaders.Normalize(value);
        if (!Validate(name, value))
            return;

        if (Guard == HeadersGuard.RequestNoCors && !FetchHeaders.IsNoCorsSafelistedRequestHeader(name, value))
            return;

        FetchHeaders.Set(List, name, value);
        if (Guard == HeadersGuard.RequestNoCors)
        {
            RemovePrivilegedNoCorsRequestHeaders();
        }
    }

    /// <summary>Returns the headers sorted by name, with the values of the headers with the same name combined (https://fetch.spec.whatwg.org/#ref-for-dfn-iterable-1).</summary>
    /// <seealso href="https://fetch.spec.whatwg.org/#headers-class"/>
    [IdlMember("iterable", "https://fetch.spec.whatwg.org/#headers-class")]
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => FetchHeaders.SortAndCombine(List).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>https://fetch.spec.whatwg.org/#headers-validate</summary>
    private bool Validate(string name, string value)
    {
        if (!FetchHeaders.IsHeaderName(name))
            throw new TypeErrorException($"'{name}' is not a valid header name");

        if (!FetchHeaders.IsHeaderValue(value))
            throw new TypeErrorException($"'{value}' is not a valid header value");

        if (Guard == HeadersGuard.Immutable)
            throw new TypeErrorException("The headers are immutable");

        if (Guard == HeadersGuard.Request && FetchHeaders.IsForbiddenRequestHeader(name, value))
            return false;

        if (Guard == HeadersGuard.Response && FetchHeaders.IsForbiddenResponseHeaderName(name))
            return false;

        return true;
    }

    /// <summary>https://fetch.spec.whatwg.org/#concept-headers-remove-privileged-no-cors-request-headers</summary>
    private void RemovePrivilegedNoCorsRequestHeaders() => FetchHeaders.Delete(List, "Range");
}
