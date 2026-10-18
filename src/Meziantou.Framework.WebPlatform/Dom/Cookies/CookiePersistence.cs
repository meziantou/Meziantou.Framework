namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The persistent storage of the cookies of a <see cref="CookieJar"/>, e.g. a file or a database. The jar keeps the
/// cookies in memory: it loads them with <see cref="LoadCookies"/> when it is created, and reports each change of a
/// persistent cookie (a cookie with an expiry time), so that an implementation only stores the cookies.
/// </summary>
/// <remarks>
/// <para>
/// A cookie is identified by its name, domain, host-only flag, path and partition key: <see cref="SetCookie"/> replaces
/// the cookie with the same identity, and <see cref="RemoveCookie"/> removes it. The session cookies (without expiry time)
/// are never stored, and a persisted cookie replaced by a session cookie is removed.
/// </para>
/// <para>
/// The methods are called synchronously, in the lock of the jar, by the responses and the scripts (<c>document.cookie</c>,
/// <c>cookieStore</c>) of the browsing contexts that share the jar: an implementation must be thread-safe, and should
/// buffer the changes and write them in the background (and expose a way to flush them). Its exceptions propagate to the
/// caller of the jar. The changes are reported in order. The last access time of a cookie, which orders the eviction
/// of the excess cookies, is persisted at most once a minute.
/// </para>
/// <para>
/// The loaded cookies are trusted (they are not parsed again), except that the cookies without domain or whose path does
/// not start with <c>/</c> are ignored, and the expired cookies are removed. The expiry times are limited to 400 days.
/// </para>
/// </remarks>
public abstract class CookiePersistence
{
    /// <summary>
    /// Returns the persisted cookies. The cookies without expiry time are loaded as session cookies (their changes are not
    /// persisted, and they are removed from the persistence when they are removed or replaced).
    /// </summary>
    public abstract IEnumerable<StoredCookie> LoadCookies();

    /// <summary>Stores a persistent cookie: a new cookie, a cookie that replaces the cookie with the same identity, or a new last access time.</summary>
    /// <param name="cookie">The cookie.</param>
    public abstract void SetCookie(StoredCookie cookie);

    /// <summary>Removes the cookie with the same identity (a cookie that is deleted, expired or evicted).</summary>
    /// <param name="cookie">The cookie.</param>
    public abstract void RemoveCookie(StoredCookie cookie);

    /// <summary>Removes all the cookies (<see cref="CookieJar.Clear"/>).</summary>
    public abstract void ClearAll();
}
