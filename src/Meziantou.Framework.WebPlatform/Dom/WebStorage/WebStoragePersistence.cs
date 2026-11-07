namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The persistent storage of the local storage areas of a <see cref="StorageShed"/> (<c>window.localStorage</c>), e.g. a
/// file or a database. The shed keeps the items of a storage area in memory: it loads them with <see cref="LoadItems"/>
/// the first time the area of an origin is used, and calls the other methods after each change, so that an
/// implementation only stores the items.
/// </summary>
/// <remarks>
/// <para>
/// The methods are called synchronously by the scripts (<c>localStorage.setItem()</c>...): an implementation that writes
/// to a slow storage should buffer the changes and write them in the background (and expose a way to flush them). The
/// changes of a storage area are reported in order, but the areas of different origins can be changed concurrently by the
/// browsing contexts of several threads, so the implementation must be thread-safe.
/// </para>
/// <para>
/// The quota of the shed is enforced on the items in memory. A storage area whose loaded items exceed it keeps them, but
/// cannot grow. The areas of the documents of the credentialless iframes are partitioned by their page and are not
/// persisted: they are kept in memory only.
/// </para>
/// </remarks>
public abstract class WebStoragePersistence
{
    /// <summary>
    /// Returns the persisted items of the storage area of an origin. The order of the items is the order of
    /// <c>localStorage.key()</c> (the shed appends the new keys, and keeps the position of a key whose value changes).
    /// An exception makes <c>localStorage</c> throw a <c>SecurityError</c> (the storage bottle map cannot be obtained),
    /// and the area is loaded again the next time it is used.
    /// </summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>, or <c>http://example.com:8080</c>).</param>
    public abstract IEnumerable<KeyValuePair<string, string>> LoadItems(string origin);

    /// <summary>
    /// Stores the value of a key of the storage area of an origin (a new key, or a new value). An exception makes
    /// <c>setItem()</c> throw a <c>QuotaExceededError</c>, and the item is not changed.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The new value.</param>
    public abstract void SetItem(string origin, string key, string value);

    /// <summary>Removes a key of the storage area of an origin. An exception is thrown by <c>removeItem()</c>, and the item is not removed.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="key">The key.</param>
    public abstract void RemoveItem(string origin, string key);

    /// <summary>Removes all the items of the storage area of an origin. An exception is thrown by <c>clear()</c>, and the items are not removed.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    public abstract void ClearItems(string origin);

    /// <summary>Removes the items of all the origins (<see cref="StorageShed.Clear"/>).</summary>
    public abstract void ClearAll();
}
