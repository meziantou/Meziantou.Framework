---
title: Serving stale responses on errors
description: Keep serving a stored response while the server fails or cannot be reached, with the stale-if-error directive.
---

A server can allow the cache to serve a stale response when it fails, with the `stale-if-error=<seconds>` directive of
`Cache-Control` (RFC 5861). The cache then serves the stored response instead of the error for that long after the
response became stale:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/StaleResponseTests.cs;region=HttpCaching_StaleIfError;format=md-fenced-code-block;dedent=true -->
```csharp
// The server answers with Cache-Control: max-age=60, stale-if-error=3600
using var response1 = await httpClient.GetAsync("https://example.com/data");

// Two minutes later, the response is stale and the server answers 503 Service Unavailable.
// The response is stale for less than an hour, so the cache serves it instead of the error.
timeProvider.Advance(TimeSpan.FromMinutes(2));
using var response2 = await httpClient.GetAsync("https://example.com/data");
Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
Assert.Equal("content", await response2.Content.ReadAsStringAsync());
Assert.Equal("110 - \"Response is Stale\", 111 - \"Revalidation Failed\"", string.Join(", ", response2.Headers.GetValues("Warning")));
```
<!-- endref -->

`httpClient` is an `HttpClient` with a caching handler, and `timeProvider` the `FakeTimeProvider` of its
[options](09-Options.md#control-the-clock).

## What counts as an error

The cache serves the stale response, with the `Warning: 110 - "Response is Stale", 111 - "Revalidation Failed"` header,
when:

- the server answers `500`, `502`, `503` or `504`;
- the server cannot be reached: the inner handler throws an `HttpRequestException`, or a `TaskCanceledException`
  while the cancellation token of the request is not canceled.

Any other answer of the server reaches the caller: a `404` or a `410` means the resource is gone, a `401` or a `403`
that it is no longer accessible. A canceled request is not an error either: it still throws. This includes the
timeout of `HttpClient.Timeout`, which cancels the token the handler receives.

## When it does not apply

- The response is stale for longer than `stale-if-error` allows: the error reaches the caller.
- The stored response has `must-revalidate` or `no-cache`: the cache never serves it without the server's approval.
- The response is still fresh: the cache serves it without contacting the server anyway.

The stored response is kept while it can be served on errors: `PruneObsoleteEntriesAsync` does not remove it. See
[Cache stores](03-CacheStores.md#removing-obsolete-entries).
