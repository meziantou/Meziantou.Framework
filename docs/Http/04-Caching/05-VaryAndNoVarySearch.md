---
title: Vary and No-Vary-Search
description: Store one variant of a response per request header value with Vary, or reuse one response for URLs that differ by their query with No-Vary-Search.
---

By default, the cache stores one response per request method and URL, query included. Two response headers change
that: `Vary` splits one URL into several variants, and `No-Vary-Search` merges URLs that only differ by their query.
The examples of this page use `httpClient`, an `HttpClient` with a caching handler, and the comments give what the
server answers.

## Vary

A server that returns different content depending on a request header, such as the language, says so with `Vary`. The
cache stores one response per value of the listed request headers:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/VaryHeaderTests.cs;region=HttpCachingDelegateHandler_Vary;format=md-fenced-code-block;dedent=true -->
```csharp
// Server responds with:
// Cache-Control: max-age=3600
// Vary: Accept-Language

using var request1 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request1.Headers.Add("Accept-Language", "en-US");
using var response1 = await httpClient.SendAsync(request1); // Caches with Accept-Language=en-US

using var request2 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request2.Headers.Add("Accept-Language", "fr-FR");
using var response2 = await httpClient.SendAsync(request2); // Fetches new response and caches separately
Assert.Null(response2.Headers.Age); // The cache adds the Age header only to the responses it serves
```
<!-- endref -->

- A stored response is only served for a request whose listed headers have the same values, in the same order. A header
  absent from the request is a value of its own: a response stored for a request without `Accept-Language` is only
  served to requests without it.
- Header names are case-insensitive, and a response can list several headers (`Vary: Accept-Language, Accept-Encoding`).
- A response with `Vary: *` can never be selected, so it is not stored.
- When several stored responses match a request, the most recent one, by its `Date` header, is served.

## No-Vary-Search

A server can declare, with the `No-Vary-Search` response header, the query parameters that do not change the response,
such as tracking parameters. The cache then serves one stored response for all the URLs that only differ by these
parameters:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/NoVarySearchTests.cs;region=HttpCachingDelegateHandler_NoVarySearch;format=md-fenced-code-block;dedent=true -->
```csharp
// Server responds with:
// Cache-Control: max-age=3600
// No-Vary-Search: params=("utm_source")

using var response1 = await httpClient.GetAsync("https://api.example.com/data?utm_source=newsletter");
using var response2 = await httpClient.GetAsync("https://api.example.com/data?utm_source=twitter"); // Served from cache
using var response3 = await httpClient.GetAsync("https://api.example.com/data"); // Served from cache
Assert.NotNull(response2.Headers.Age); // The cache adds the Age header to the responses it serves
Assert.NotNull(response3.Headers.Age);
```
<!-- endref -->

The header is a structured field dictionary:

| Value | Meaning |
| --- | --- |
| `key-order` | Only the order of the query parameters does not matter |
| `params=("a" "b")` | The listed parameters do not matter; every other one still makes URLs different |
| `params` | No query parameter matters |
| `except=("a" "b")` | Only the listed parameters matter; every other one is ignored |
| `params, except=("a" "b")` | Another spelling of `except`, with the same meaning |
| `except=()` | No query parameter matters |

The entries can be combined: `No-Vary-Search: key-order, params=("utm_source")`.

- The queries are compared after being decoded as `application/x-www-form-urlencoded`, so `?a=x`, `?%61=%78` and
  `?a=x&&&` are the same query.
- A malformed or unknown header value is ignored: the URLs are compared exactly, as without the header.
- `No-Vary-Search` and `Vary` combine: a request must match both to be served a stored response.
- A response stored for the exact URL of a request is preferred over one that only matches thanks to `No-Vary-Search`.
- A `POST`, `PUT`, `DELETE` or `PATCH` request removes every response stored with `No-Vary-Search` for the same path,
  whatever their query. See [Invalidation](08-Invalidation.md).
