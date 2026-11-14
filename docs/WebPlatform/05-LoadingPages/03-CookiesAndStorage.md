---
title: Cookies and storage
description: Give pages cookies, local storage and the other stores of the browsers, share them between pages, persist them, and let sites clear them.
---

## Cookies

Without a [CookieJar](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar), the pages have no cookies. Give the browsing contexts a jar: the document loader sends its cookies
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
  the cookie prefixes and the public suffixes. Third-party cookies are not blocked by default (see
  [Third-party cookies and storage access](#third-party-cookies-and-storage-access)).
- As in the browsers, the cookies of a host are shared by its schemes and its ports. Set
  [OriginBound](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar.OriginBound) (`new CookieJar { OriginBound = true }`) to bind them to the origin that set them
  ([origin-bound cookies](https://github.com/explainers-by-googlers/origin-bound-cookies)): a cookie set by an `https`
  page is not sent to the `http` pages of the same host, and the reverse, and a cookie without `Domain` attribute is only
  sent to the port that set it. The cookies of the same name set by another scheme or another port do not replace each
  other. [StoredCookie.SourceScheme](cref:Meziantou.Framework.WebPlatform.Dom.StoredCookie.SourceScheme) and [SourcePort](cref:Meziantou.Framework.WebPlatform.Dom.StoredCookie.SourcePort) give the origin that set a cookie: persist them with the
  cookie, a cookie loaded without them is sent to every scheme and port.
- Several browsing contexts can share a jar, as the tabs of a browser share their cookies.
- `SetCookie(url, header)` adds a cookie as a `Set-Cookie` header does (to sign a session in before loading a page);
  `GetCookies(url)` and [GetCookies()](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar.GetCookies) list them, [Remove](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar.Remove) and [Clear](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar.Clear) delete them.
- Create the jar with the `TimeProvider` of the browsing context (`new CookieJar(timeProvider)`) so that the cookies
  expire with its clock.
- To keep the persistent cookies between runs, derive from [CookiePersistence](cref:Meziantou.Framework.WebPlatform.Dom.CookiePersistence) ([LoadCookies](cref:Meziantou.Framework.WebPlatform.Dom.CookiePersistence.LoadCookies), [SetCookie](cref:Meziantou.Framework.WebPlatform.Dom.CookiePersistence.SetCookie), [RemoveCookie](cref:Meziantou.Framework.WebPlatform.Dom.CookiePersistence.RemoveCookie),
  [ClearAll](cref:Meziantou.Framework.WebPlatform.Dom.CookiePersistence.ClearAll)) and give it to the jar: `new CookieJar(persistence, timeProvider)`. The jar keeps the cookies in memory and
  reports each change.

### Cookies in the service workers

A service worker reads and writes the cookies of its script URL with `self.cookieStore` (any URL of its origin with the
`url` option). It does not get the `change` events of `window.cookieStore`: it subscribes to the changes with
`registration.cookies.subscribe([{ name, url }])`, and receives a `cookiechange` event (`ExtendableCookieChangeEvent`)
when a cookie that matches a subscription changes (the service worker is started if needed). A subscription matches the cookies of
its name (any name when it has none) that are sent to its URL (the scope of the registration when it has none), which
must be in the scope of the registration. The subscriptions belong to the registration: an update of the service worker
keeps them, the [ServiceWorkerBackend](cref:Meziantou.Framework.WebPlatform.Dom.ServiceWorkerBackend) saves them with
the registration ([StoredServiceWorkerRegistration.CookieChangeSubscriptions](cref:Meziantou.Framework.WebPlatform.Dom.StoredServiceWorkerRegistration.CookieChangeSubscriptions)),
and unregistering drops them.

## Third-party cookies and storage access

By default, as in the browsers that do not block them, a frame of another site than the top-level page sends and stores
the cookies of its site. Set [BlockThirdPartyCookies](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar.BlockThirdPartyCookies) to block them, and
[SetThirdPartyCookieAccess](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar.SetThirdPartyCookieAccess) to block or allow the cookies of a site in the pages of another site (the settings of the
user):

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/Dom/CookieJarTests.cs;region=WebPlatform_ThirdPartyCookies;format=md-fenced-code-block;dedent=true -->
```csharp
var cookies = new CookieJar { BlockThirdPartyCookies = true };
cookies.SetCookie("https://embed.example/", "id=42; Path=/; Secure; SameSite=None");

// The request of an iframe of https://embed.example in a page of https://top.example
var request = new DocumentRequest("https://embed.example/widget")
{
    CookieJar = cookies,
    SiteForCookies = "https://top.example",
    TopLevelOrigin = "https://top.example",
    Origin = "https://top.example",
    Destination = RequestDestination.Iframe,
};
Assert.Null(request.GetCookieHeader());

// Allow the cookies of https://embed.example in the pages of https://top.example
cookies.SetThirdPartyCookieAccess("https://top.example/", "https://embed.example/", blocked: false);
Assert.Equal("id=42", request.GetCookieHeader());
```
<!-- endref -->

- A context is third-party when its site is not the site of the top-level page, or when one of its parent frames is of
  another site. Its documents (`document.cookie`, `cookieStore`) and its requests (subresources, `fetch()`, WebSockets,
  the navigations of its frames) neither see nor set the unpartitioned cookies of a blocked site. The cookies with the
  `Partitioned` attribute are not blocked: they are kept per top-level site.
- [SetThirdPartyCookieAccess](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar.SetThirdPartyCookieAccess) takes null for every top-level site or every embedded site. The rule of a pair of sites
  wins over the rule of every embedded site, which wins over the rule of every top-level site, which wins over
  [BlockThirdPartyCookies](cref:Meziantou.Framework.WebPlatform.Dom.CookieJar.BlockThirdPartyCookies).

A blocked frame gets its cookies back with the [Storage Access API](https://privacycg.github.io/storage-access/), from
its scripts or from .NET ([Document.HasStorageAccess](cref:Meziantou.Framework.WebPlatform.Dom.Document.HasStorageAccess) and [Document.RequestStorageAccess](cref:Meziantou.Framework.WebPlatform.Dom.Document.RequestStorageAccess)):

- `document.requestStorageAccess()` is granted when the `storage-access` permission is granted, or when the frame has a
  user activation (a click) and [RequestPermission](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.RequestPermission) grants it. The
  permission is kept by [Permissions](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.Permissions) for the pair of the sites of the top-level page and of the frame. It is
  always granted in a top-level page, in a frame of the top-level site, and for a site whose cookies are allowed; it is
  rejected in a page that is not secure (`https`), that is sandboxed without `allow-storage-access-by-user-activation`,
  that is loaded in a credentialless iframe, or whose `storage-access` permissions policy is disabled
  (`<iframe allow="storage-access 'none'">`).
- Once granted, the frame has storage access: its scripts see the cookies, and so do its requests to its own origin. The
  requests to other origins, the requests of the workers it starts and the other frames of the site do not. A reload of the frame, or a
  navigation that the frame starts to its own origin, keeps the access; a navigation started by another frame does not.
- `document.hasStorageAccess()` tells whether the frame has access to its cookies: always true in a first-party context
  and for a site whose cookies are not blocked.
- The requests from a third-party context send the `Sec-Fetch-Storage-Access` header
  ([Storage Access Headers](https://privacycg.github.io/storage-access-headers/)): `active` when the cookies are sent,
  `inactive` when the permission is granted but the request does not use it, `none` otherwise. A response with
  `Activate-Storage-Access: retry; allowed-origin="..."` makes an `inactive` request be sent again with the cookies, and
  the document of a frame whose response has `Activate-Storage-Access: load` gets storage access.

## Local storage and session storage

`window.localStorage` keeps its items in a [StorageShed](cref:Meziantou.Framework.WebPlatform.Dom.StorageShed). By default, each browsing context created by your application
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
  [GetOrigins](cref:Meziantou.Framework.WebPlatform.Dom.StorageShed.GetOrigins), [GetItems](cref:Meziantou.Framework.WebPlatform.Dom.StorageShed.GetItems), [SetItem](cref:Meziantou.Framework.WebPlatform.Dom.StorageShed.SetItem) and [Clear](cref:Meziantou.Framework.WebPlatform.Dom.StorageShed.Clear) read and change them from .NET.
- To keep the items between runs, derive from [WebStoragePersistence](cref:Meziantou.Framework.WebPlatform.Dom.WebStoragePersistence) and give it to the shed:
  `new StorageShed(persistence)`.
- `window.sessionStorage` belongs to the top-level browsing context, and is always kept in memory.

## Storage partitioning

As in the browsers, the storage of a page embedded by another site is partitioned by the site of the top-level page
([storage partitioning](https://privacycg.github.io/storage-partitioning/)): an `https://embed.example` frame in a page
of `https://top.example` does not see the local storage, the session storage, the IndexedDB databases, the caches, the
origin private file system, the `BroadcastChannel` messages and the service workers of `https://embed.example` when it is
the top-level page, nor the ones of the frames of `https://embed.example` in the pages of another site. The storage of a
frame of the top-level site whose parent frame is of another site is partitioned too.

- [StorageShed.GetItems](cref:Meziantou.Framework.WebPlatform.Dom.StorageShed.GetItems), [SetItem](cref:Meziantou.Framework.WebPlatform.Dom.StorageShed.SetItem) and [GetOrigins](cref:Meziantou.Framework.WebPlatform.Dom.StorageShed.GetOrigins) only give the items of the top-level pages (the first-party storage).
- The partitioned items of the local storage, and the partitioned service worker registrations, are kept in memory: they
  are not given to the persistence.
- The backends of the other stores receive the storage key of a partition in place of the origin: the origin, the site
  of the top-level page and whether a parent frame is of another site, separated by `^`
  (`https://embed.example^https://top.example^0`).

## The other stores

The other storage APIs of the browsers keep their data in backends that you can replace, to share or persist them:

| Option | Store | Default |
| --- | --- | --- |
| [IndexedDB](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.IndexedDB) | `indexedDB` | [InMemoryIndexedDBBackend](cref:Meziantou.Framework.WebPlatform.Dom.InMemoryIndexedDBBackend), per browsing context created by the application |
| [CacheStorage](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.CacheStorage) | `caches` (the Cache API) | In memory |
| [FileSystem](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.FileSystem) | The origin private file system (`navigator.storage.getDirectory()`) | In memory |
| [ServiceWorkers](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.ServiceWorkers) | The registrations of the service workers | In memory |

These stores are used by the scripts of the pages: they need a [script engine](../06-Scripting/01-Overview.md).

## Clear-Site-Data

A site clears the data that the browser keeps for it with the `Clear-Site-Data` header of a response. The header is
honored on the responses of the secure URLs (`https`, and `http://localhost`), when the request sends credentials:

| Type | What is cleared for the origin of the response |
| --- | --- |
| `"cache"` | The responses of its URLs in the HTTP cache and its pages in the back/forward cache (for the tab's site only, as the browsers partition their caches), and its prefetched and prerendered pages |
| `"cookies"` | The cookies of its registrable domain (`"cookies"` for `https://www.example.com` removes the cookies of `example.com` and of its subdomains) |
| `"storage"` | Its local storage, its session storage, its IndexedDB databases, its caches of the Cache API, and its service worker registrations |
| `"executionContexts"` | Its pages are reloaded |
| `"clientHints"` | The client hints it opted into (also cleared by `"cache"` and `"cookies"`) |
| `"*"` | All of the above |

The header is also honored on the responses of the background revalidations of the HTTP cache (the responses used
within their `stale-while-revalidate` window), and a response that clears the cache is not stored in it.

The cookies that the response sets are cleared too. As in the browsers, a response loaded by a page of another site
(a third-party resource or frame) only clears the data of that page's partition: the partitioned cookies, the caches,
and the stores of the origin [partitioned](#storage-partitioning) by the site of that page. The stores of the origin as a
top-level page are kept. So are they for a response loaded by a frame of the top-level site whose parent frame is of
another site: it clears the stores of that frame's partition. Some data is always kept:

- An IndexedDB database that a page still has open.
- The origin private file system.
- The pages that a service worker controls keep it until they are unloaded: unregistering does not remove the
  controller.
- `"executionContexts"` only reloads the pages in the tab of the request, and never the page or frame that loaded the
  response, nor the pages that contain it: reloading them would load the response again, forever.
