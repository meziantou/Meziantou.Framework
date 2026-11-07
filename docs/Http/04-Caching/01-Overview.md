---
title: Overview
description: What the HTTP cache of Meziantou.Framework.Http.Caching does, its packages, and the pages of this group.
---

`Meziantou.Framework.Http.Caching` adds a cache to an `HttpClient`. [HttpCachingDelegateHandler](cref:Meziantou.Framework.Http.Caching.HttpCachingDelegateHandler) is a
`DelegatingHandler`: it stores the responses the server allows to store, serves them while they are fresh, revalidates
them with conditional requests once they are stale, and removes them when a request changes the resource. It follows
the HTTP caching rules (RFC 7234, since obsoleted by RFC 9111), the `immutable` directive (RFC 8246), `stale-if-error`
(RFC 5861) and the `No-Vary-Search` response header.

## Packages

The cache stores its entries in an [IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore). Each store has its own package:

| Package | Store | Where the entries live |
| --- | --- | --- |
| `Meziantou.Framework.Http.Caching` | | The handler and the [IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore) interface |
| `Meziantou.Framework.Http.Caching.InMemory` | [InMemoryHttpCacheStore](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore) | In memory, with an optional JSON file to keep them between runs |
| `Meziantou.Framework.Http.Caching.Sqlite` | [SqliteHttpCacheStore](cref:Meziantou.Framework.Http.Caching.Sqlite.SqliteHttpCacheStore) | In a SQLite database |
| `Meziantou.Framework.Http.Caching.Redis` | [RedisHttpCacheStore](cref:Meziantou.Framework.Http.Caching.Redis.RedisHttpCacheStore) | In a Redis database |

You can also write your own store. See [Cache stores](03-CacheStores.md).

## A private cache

This is a **private** cache: it belongs to one `HttpClient`, and its entries are not shared between users. Responses to
requests that carry an `Authorization` header are stored like any other, which the HTTP caching rules allow for a
private cache. Do not share one `HttpClient`, and therefore one cache, between users: a user could receive a response
stored for another one.

For the same reason, `s-maxage` and `proxy-revalidate`, which only apply to shared caches, are ignored.

## Pages of this group

- [Getting started](02-GettingStarted.md): add the cache to an `HttpClient`, directly or with `IHttpClientFactory`.
- [Cache stores](03-CacheStores.md): keep the entries in memory, in a file, in SQLite or in Redis, remove the obsolete
  ones, or write your own store.
- [Freshness and validation](04-FreshnessAndValidation.md): which responses are stored, how long they are fresh, and
  how the cache revalidates them.
- [Vary and No-Vary-Search](05-VaryAndNoVarySearch.md): store several variants of a response, or reuse one response for
  several URLs.
- [Request directives](06-RequestDirectives.md): force a revalidation, accept stale responses, or only use the cache,
  per request.
- [Serving stale responses on errors](07-StaleIfError.md): keep serving a stored response while the server fails.
- [Invalidation](08-Invalidation.md): how `POST`, `PUT`, `DELETE` and `PATCH` requests remove stored responses.
- [Options](09-Options.md): limit the size of the stored responses, choose which ones to store, control the clock,
  observe the store failures.
- [Limitations](10-Limitations.md): what the cache does not do.
