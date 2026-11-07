---
title: Freshness and validation
description: Which responses the cache stores, how long they are fresh, and how it revalidates them with ETag and Last-Modified.
---

The server decides what the cache does with its responses, through the `Cache-Control`, `Expires`, `ETag` and
`Last-Modified` headers. A stored response is **fresh** for a while: the cache serves it without contacting the
server. Once it is **stale**, the cache asks the server whether it changed, with a conditional request:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/ConditionalRequestsAndRevalidationTests.cs;region=HttpCaching_FreshnessAndRevalidation;format=md-fenced-code-block;dedent=true -->
```csharp
// The server answers with:
// Cache-Control: max-age=60
// ETag: "v1"
using var response1 = await httpClient.GetAsync("https://example.com/data");

// The response is fresh for 60 seconds: the cache serves it without contacting the server
timeProvider.Advance(TimeSpan.FromSeconds(30));
using var response2 = await httpClient.GetAsync("https://example.com/data");
Assert.Equal(TimeSpan.FromSeconds(30), response2.Headers.Age);

// The response is stale: the cache sends If-None-Match: "v1", the server answers
// 304 Not Modified, and the cache serves the stored response again
timeProvider.Advance(TimeSpan.FromSeconds(60));
using var response3 = await httpClient.GetAsync("https://example.com/data");
Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
Assert.Equal("v1", await response3.Content.ReadAsStringAsync());
```
<!-- endref -->

`httpClient` is an `HttpClient` with a caching handler, and `timeProvider` the `FakeTimeProvider` of its options, to
move the clock (see [Options](09-Options.md#control-the-clock)).

## Which responses are stored

A response is stored when all these conditions hold:

- The request is a `GET` or a `HEAD`. They are stored under different keys, so a stored `HEAD` response is never served
  for a `GET`.
- The request has no `Range` header, no `Cache-Control: no-store`, and no precondition of its own (`If-None-Match`,
  `If-Modified-Since`, `If-Match`, `If-Unmodified-Since`, `If-Range`). Those requests are sent to the server unchanged.
- The response has no `Cache-Control: no-store`, and no `Vary: *`.
- The status code can be stored:
  - `200`, `203`, `204`, `300`, `301`, `404`, `405`, `410`, `414` and `501` are stored by default;
  - `500` is only stored when the response says how long it is fresh (`max-age` or a future `Expires`), or has
    `Cache-Control: public` or `private`;
  - any other status code, `206 Partial Content` included, is never stored.
- The response is not larger than [MaximumResponseSize](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.MaximumResponseSize), and [ShouldCacheResponse](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.ShouldCacheResponse) accepts it. See
  [Options](09-Options.md).

The response is returned to the caller whether it is stored or not.

## How long a response is fresh

The cache computes the freshness lifetime of a response from, in this order:

1. `Cache-Control: max-age=<seconds>`.
2. `Expires`, minus the `Date` of the response.
3. Without either, 10% of the time between `Last-Modified` and `Date`: a heuristic for resources that do not change
   often. A response served with such a lifetime of 24 hours or more gets a `Warning: 113 - "Heuristic Expiration"`
   header.
4. Otherwise, zero: the response is stale as soon as it is stored, and only reusable after a revalidation.

`s-maxage` only applies to shared caches and is ignored. The age of a stored response takes the `Age` and `Date`
headers of the server into account, and the time the request took.

Each response served from the cache has an `Age` header that gives its current age, in seconds. A response sent by the
server has the `Age` header the server sent, usually none.

## Revalidation

When a stored response is stale, or must be revalidated, the cache sends the request with the validators of the stored
response: `If-None-Match` with its `ETag`, and `If-Modified-Since` with its `Last-Modified` date.

- When the server answers `304 Not Modified`, the cache updates the stored response with the headers of the `304`
  (a new `Cache-Control` replaces the stored directives) and serves it.
- When the server answers with a full response, the cache returns it and stores it in place of the old one.
- A `304` whose `ETag` or `Last-Modified` does not match the stored response cannot validate it: the cache sends the
  request again without validators and returns the full response.
- A stale response without `ETag` or `Last-Modified` cannot be revalidated: the cache sends the request as is.

The directives of the response change when the cache revalidates:

| Response directive | Effect |
| --- | --- |
| `no-cache` | The response is stored, but revalidated before each use, even when fresh |
| `must-revalidate` | Once stale, the response is never served without revalidation, even when the request accepts stale responses |
| `immutable` | While fresh, the response is served without revalidation, even when the request asks for one. See [Request directives](06-RequestDirectives.md#immutable-responses) |
| `stale-if-error=<seconds>` | The stale response can be served when the server fails. See [Serving stale responses on errors](07-StaleIfError.md) |

## Warning headers

The cache adds a `Warning` header to some of the responses it serves:

| Warning | When |
| --- | --- |
| `110 - "Response is Stale"` | The response is stale, served because the request accepts it or the server failed |
| `111 - "Revalidation Failed"` | The server failed while the cache revalidated the response |
| `113 - "Heuristic Expiration"` | The freshness lifetime comes from the heuristic and is 24 hours or more |
