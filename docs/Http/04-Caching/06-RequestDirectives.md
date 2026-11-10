---
title: Request directives
description: Force a revalidation, accept stale responses, require fresh ones, or only use the cache, with the Cache-Control header of a request.
---

The `Cache-Control` header of a request changes how the cache answers that request only. Set it with
`request.Headers.CacheControl` (`CacheControlHeaderValue` is in the `System.Net.Http.Headers` namespace). The examples of
this page use `httpClient`, an `HttpClient` with a caching handler that already stored a response for the URL.

## Force a revalidation

`no-cache` makes the cache revalidate the stored response with the server before using it, even when it is fresh:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/RequestValidationTests.cs;region=HttpCachingDelegateHandler_ForceRevalidation;format=md-fenced-code-block;dedent=true -->
```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
using var response = await httpClient.SendAsync(request);
// Note: If the response has Cache-Control: immutable and is fresh, it will still be served from cache
```
<!-- endref -->

`Pragma: no-cache` has the same effect when the request has no `Cache-Control` header, for HTTP/1.0 compatibility.

## Accept stale responses

`max-stale` lets the cache serve a stale response without contacting the server, up to the given staleness, or
whatever its staleness without a limit:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/RequestValidationTests.cs;region=HttpCachingDelegateHandler_AcceptStaleResponses;format=md-fenced-code-block;dedent=true -->
```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request.Headers.CacheControl = new CacheControlHeaderValue
{
    MaxStale = true,
    MaxStaleLimit = TimeSpan.FromMinutes(5), // Accept up to 5 minutes stale
};
using var response = await httpClient.SendAsync(request);
```
<!-- endref -->

A stale response served this way has a `Warning: 110 - "Response is Stale"` header. A response stored with
`must-revalidate` is never served stale.

## Only use the cache

`only-if-cached` never contacts the server. The cache returns the stored response when it can use it without a
revalidation, fresh or stale within `max-stale`, and a `504 Gateway Timeout` response otherwise:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/RequestValidationTests.cs;region=HttpCachingDelegateHandler_OnlyIfCached;format=md-fenced-code-block;dedent=true -->
```csharp
using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request.Headers.CacheControl = new CacheControlHeaderValue { OnlyIfCached = true };
using var response = await httpClient.SendAsync(request);
// Returns cached response or 504 Gateway Timeout
Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
```
<!-- endref -->

## All the request directives

| Directive | Effect |
| --- | --- |
| `no-store` | The cache is not used: the request is sent to the server, and the response is not stored |
| `no-cache` | The stored response is revalidated before use, unless it is `immutable` and fresh |
| `max-age=<seconds>` | A stored response older than this is treated as stale |
| `min-fresh=<seconds>` | A stored response that stays fresh for less than this is treated as stale |
| `max-stale[=<seconds>]` | A stale response is served without revalidation, up to the given staleness |
| `only-if-cached` | The server is never contacted: a stored response or `504 Gateway Timeout` |

A stale response is revalidated as described in [Freshness and validation](04-FreshnessAndValidation.md#revalidation).

A request that carries its own precondition (`If-None-Match`, `If-Modified-Since`, `If-Match` or `If-Unmodified-Since`)
bypasses the cache: it is sent to the server unchanged, so the caller gets the `304` or the `200` it asked for, and the
response is not stored.

## Range requests

A `GET` request with a `Range` header asks for part of the response. When the cache holds a complete `200` response it can
serve, it answers from it:

<!-- ref:../../../samples/Meziantou.Framework.Http.Samples/HttpCachingExamples.cs;region=HttpCaching_RangeRequest;format=md-fenced-code-block;dedent=true -->
```csharp
using var cachingHandler = new HttpCachingDelegateHandler(new SocketsHttpHandler(), new InMemoryHttpCacheStore());
using var httpClient = new HttpClient(cachingHandler);

// The server answers with a complete response, which the cache stores
using var response = await httpClient.GetAsync("https://example.com/file.txt");

// While the stored response is fresh, the cache serves the range from it without contacting the server:
// 206 Partial Content, with Content-Range: bytes 5-11/<length of the response>
using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/file.txt");
rangeRequest.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(5, 11);
using var rangeResponse = await httpClient.SendAsync(rangeRequest);
```
<!-- endref -->

- A single range gets a `206 Partial Content` response with a `Content-Range` header. Open-ended (`bytes=13-`) and suffix
  (`bytes=-4`) ranges are supported.
- Several ranges get a `206` response with a `multipart/byteranges` body, one part per range. When the ranges overlap and
  add up to more than the response, when there are more than 16, or when the response has a `Content-Encoding`, the
  complete response is sent instead.
- When no range is within the response, the cache answers `416 Range Not Satisfiable`, with `Content-Range: bytes */<length>`.
- A `Range` header that is invalid, or in another unit than `bytes`, is ignored: the complete response is served.
- `If-Range` is evaluated against the stored response: the range is served when the entity tag matches its `ETag` (both
  strong), or when the date matches its `Last-Modified` date. Otherwise, the complete response is served.
- A stale stored response is revalidated first, with the `Range` header. When the server answers `304`, the range is
  served from the refreshed response; otherwise, the response of the server is returned.

When the cache has no response to serve, the request is sent to the server unchanged. A `206` response is never stored,
so only the complete responses the cache already holds can answer range requests. `Range` only applies to `GET`: a `HEAD`
request is answered as if it had none.

## Immutable responses

A response with `Cache-Control: immutable` never changes while it is fresh, typically a versioned file such as
`/assets/script.v123.js`. The cache serves it without revalidation even when the request has `no-cache`:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/AdvancedCachingTests.cs;region=HttpCachingDelegateHandler_Immutable;format=md-fenced-code-block;dedent=true -->
```csharp
// Server responds with:
// Cache-Control: max-age=31536000, immutable
// ETag: "abc123"

// First request fetches from origin
using var response1 = await httpClient.GetAsync("https://cdn.example.com/script.js");

// Second request with no-cache still uses cached version (because immutable + fresh)
using var request2 = new HttpRequestMessage(HttpMethod.Get, "https://cdn.example.com/script.js");
request2.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
using var response2 = await httpClient.SendAsync(request2); // Served from cache, no revalidation
Assert.NotNull(response2.Headers.Age);
```
<!-- endref -->

Once the response is stale, `immutable` has no effect: it is revalidated like any other.
