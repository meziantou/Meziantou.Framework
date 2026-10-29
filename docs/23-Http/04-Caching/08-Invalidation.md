---
title: Invalidation
description: How POST, PUT, DELETE and PATCH requests remove the stored responses of the resources they change.
---

A request that can change a resource removes the responses stored for it, so the next `GET` reads the new version from
the server:

<!-- ref:../../../tests/Meziantou.Framework.Http.Caching.Tests/UnsafeMethodInvalidationTests.cs;region=HttpCaching_Invalidation;format=md-fenced-code-block;dedent=true -->
```csharp
// The server answers with Cache-Control: max-age=3600
Assert.Equal("before", await httpClient.GetStringAsync("https://example.com/items/1"));

// A successful DELETE removes the stored responses of https://example.com/items/1
using var deleteResponse = await httpClient.DeleteAsync("https://example.com/items/1");
Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

// The next GET is sent to the server
Assert.Equal("after", await httpClient.GetStringAsync("https://example.com/items/1"));
```
<!-- endref -->

`httpClient` is an `HttpClient` with a caching handler.

## Which requests invalidate

- Every method other than `GET`, `HEAD`, `OPTIONS` and `TRACE` invalidates: `POST`, `PUT`, `DELETE`, `PATCH`, and the
  methods of your own.
- Only when the server answers with a status code from `200` to `399`. An error response leaves the stored responses
  in place.
- These requests are never answered from the cache, and their responses are not stored.

## What is removed

- The stored `GET` and `HEAD` responses of the request URL.
- The stored responses of the URLs in the `Location` and `Content-Location` headers of the response, when they are on
  the same host as the request.
- The responses stored with `No-Vary-Search` for the same paths, whatever their query (see
  [Vary and No-Vary-Search](05-VaryAndNoVarySearch.md#no-vary-search)).

Every variant of these URLs is removed, whatever their `Vary` headers.

The cache only sees the requests sent through it. A resource changed by another client, or by another `HttpClient`, is
only refreshed when its stored response becomes stale.
