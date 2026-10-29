---
title: Cookies and storage
description: Give pages cookies, local storage and the other stores of the browsers, share them between pages, and persist them.
---

## Cookies

Without a `CookieJar`, the pages have no cookies. Give the browsing contexts a jar: the document loader sends its cookies
and stores the cookies of the responses, and the pages read and write it with `document.cookie` and `cookieStore`.

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/CookieJarTests.cs;region=WebPlatform_Cookies;format=md-fenced-code-block;dedent=true -->
```csharp
var cookies = new CookieJar();
cookies.SetCookie("https://example.com/", "session=abc; Path=/; Secure; HttpOnly");
cookies.SetCookie("https://example.com/", "theme=dark; Path=/");

using var context = new BrowsingContext(new BrowsingContextOptions { CookieJar = cookies });
var document = context.Navigate("https://example.com/", _ => "<p>Hello</p>");

// document.cookie does not see the HttpOnly cookies
Assert.Equal("theme=dark", document.Cookie);
document.Cookie = "lang=fr; Path=/";

var names = cookies.GetCookies("https://example.com/").Select(cookie => cookie.Name).Order(StringComparer.Ordinal);
Assert.Equal(["lang", "session", "theme"], names);
```
<!-- endref -->

- The jar follows the rules of the browsers ([RFC 6265bis](https://httpwg.org/http-extensions/draft-ietf-httpbis-rfc6265bis.html)):
  `Expires` and `Max-Age`, `Domain` and `Path`, `Secure`, `HttpOnly`, `SameSite` (`Lax` by default), `Partitioned`,
  the cookie prefixes and the public suffixes. Third-party cookies are not blocked.
- Several browsing contexts can share a jar, as the tabs of a browser share their cookies.
- `SetCookie(url, header)` adds a cookie as a `Set-Cookie` header does (to sign a session in before loading a page);
  `GetCookies(url)` and `GetCookies()` list them, `Remove` and `Clear` delete them.
- Create the jar with the `TimeProvider` of the browsing context (`new CookieJar(timeProvider)`) so that the cookies
  expire with its clock.
- To keep the persistent cookies between runs, derive from `CookiePersistence` (`LoadCookies`, `SetCookie`, `RemoveCookie`,
  `ClearAll`) and give it to the jar: `new CookieJar(persistence, timeProvider)`. The jar keeps the cookies in memory and
  reports each change.

## Local storage and session storage

`window.localStorage` keeps its items in a `StorageShed`. By default, each browsing context created by your application
has its own; give the same shed to several browsing contexts to share the items of an origin:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/WebStorageTests.cs;region=WebPlatform_LocalStorage;format=md-fenced-code-block;dedent=true -->
```csharp
// The local storage areas of the origins, which several browsing contexts can share
var storage = new StorageShed();
using var context = new BrowsingContext(new BrowsingContextOptions { LocalStorage = storage });
context.Navigate("https://example.com/", _ => "<p>Hello</p>");

// window.localStorage, from .NET or from the scripts of the page
context.Window.LocalStorage.SetItem("theme", "dark");
Assert.Equal([new("theme", "dark")], storage.GetItems("https://example.com"));
```
<!-- endref -->

- The items are kept per origin, with a quota of 5 Mi characters per origin (`new StorageShed(quota)` sets another one).
  `GetOrigins`, `GetItems`, `SetItem` and `Clear` read and change them from .NET.
- To keep the items between runs, derive from `WebStoragePersistence` and give it to the shed:
  `new StorageShed(persistence)`.
- `window.sessionStorage` belongs to the top-level browsing context, and is always kept in memory.

## The other stores

The other storage APIs of the browsers keep their data in backends that you can replace, to share or persist them:

| Option | Store | Default |
| --- | --- | --- |
| `IndexedDB` | `indexedDB` | `InMemoryIndexedDBBackend`, per browsing context created by the application |
| `CacheStorage` | `caches` (the Cache API) | In memory |
| `FileSystem` | The origin private file system (`navigator.storage.getDirectory()`) | In memory |
| `ServiceWorkers` | The registrations of the service workers | In memory |

These stores are used by the scripts of the pages: they need a [script engine](../06-Scripting/01-Overview.md).
