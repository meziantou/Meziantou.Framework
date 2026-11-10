---
title: Limitations
description: What the HTTP cache does not do - synchronous requests, storing partial content and coalescing across handlers.
---

## No synchronous requests

[IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore) is asynchronous, so there is no synchronous path through the cache. `HttpClient.Send` throws a
`NotSupportedException` rather than sending the request to the server without caching it. Use `SendAsync`, `GetAsync`,
`PostAsync` and the other asynchronous methods.

## Partial responses are not stored

Range requests are answered from stored complete responses (see
[Request directives](06-RequestDirectives.md#range-requests)), but `206 Partial Content` responses are never stored, nor
combined into a complete response. A range request that the cache cannot answer goes to the server every time.

## Coalescing is per handler

[CoalesceRequests](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.CoalesceRequests) only coalesces the requests that go through the same [HttpCachingDelegateHandler](cref:Meziantou.Framework.Http.Caching.HttpCachingDelegateHandler).
Handlers that share a store, in other processes for instance, still send their own requests. See
[Options](09-Options.md#coalesce-concurrent-requests).

## Only GET and HEAD

Only the responses to `GET` and `HEAD` requests are stored. The responses to `POST` requests are never stored, even
when they have explicit freshness information.
