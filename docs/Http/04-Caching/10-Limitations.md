---
title: Limitations
description: What the HTTP cache does not do - synchronous requests, request coalescing, shared caching, partial content and eviction.
---

## No synchronous requests

[IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore) is asynchronous, so there is no synchronous path through the cache. `HttpClient.Send` throws a
`NotSupportedException` rather than sending the request to the server without caching it. Use `SendAsync`, `GetAsync`,
`PostAsync` and the other asynchronous methods.

## No request coalescing

The handler is thread-safe and can be used by concurrent requests. However, concurrent requests for a URL that is not
stored yet each go to the server, and each stores its response: the last one written wins. To send a single request
per URL, coordinate the callers above the handler.

## Private cache only

The cache belongs to one `HttpClient` and does not separate users: it stores the responses to requests with an
`Authorization` header, and ignores the directives of shared caches (`s-maxage`, `proxy-revalidate`). Do not use it as
a cache shared between users, such as a proxy. See [Overview](01-Overview.md#a-private-cache).

## No partial content

Requests with a `Range` header are sent to the server, and `206 Partial Content` responses are never stored. A stored
full response is not used to answer a range request.

## No eviction

The stores have no size limit and no eviction policy: an entry stays until a request replaces or invalidates it, or
until [PruneObsoleteEntriesAsync](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore.PruneObsoleteEntriesAsync) removes it once it is obsolete. A cache that receives many distinct URLs keeps
growing; prune it from time to time, and bound the size of each response with [MaximumResponseSize](cref:Meziantou.Framework.Http.Caching.HttpCachingOptions.MaximumResponseSize). See
[Cache stores](03-CacheStores.md#removing-obsolete-entries) and [Options](09-Options.md).

## Only GET and HEAD

Only the responses to `GET` and `HEAD` requests are stored. The responses to `POST` requests are never stored, even
when they have explicit freshness information.
