---
title: Introduction
description: The Meziantou.Framework.Http packages extend HttpClient with caching, HSTS, SSRF protection and record-and-replay, and parse Link headers and htpasswd files.
---

The `Meziantou.Framework.Http.*` packages add features to `HttpClient` that it does not have on its own. Most of them
are a `DelegatingHandler`: you put it in the pipeline of a client, and every request that goes through the client gets
the feature. The others parse HTTP formats.

## What you can do with them

- **Cache responses** on the client, following the HTTP caching rules: freshness, validation with `ETag` and
  `Last-Modified`, `Vary`, `No-Vary-Search`, `immutable` and `stale-if-error`. Keep the cache in memory, in a file, in
  SQLite or in Redis. See [Caching](04-Caching/01-Overview.md).
- **Enforce HSTS**: send the requests to the hosts that require HTTPS over HTTPS, even when the URL says `http://`, with
  the HSTS preload list built in. See [HSTS](05-Hsts/01-Overview.md).
- **Block server-side request forgery** when your application fetches URLs it does not control: reject the schemes,
  the IP addresses and the networks it must not reach, checked on every connection. See
  [Server-side request forgery](08-ServerSideRequestForgery/01-Overview.md).
- **Record and replay** the traffic of an `HttpClient` in tests, to a JSON or a HAR file, with the secrets removed. See
  [Recording](07-Recording/01-Overview.md).
- **Parse `Link` headers** (RFC 8288), for instance to follow pagination links. See [Link headers](03-LinkHeaders.md).
- **Verify credentials** against an Apache htpasswd file. See [Htpasswd files](06-Htpasswd.md).

## Packages

| Package | Main types | Pages |
| --- | --- | --- |
| `Meziantou.Framework.Http` | [LinkHeaderValue](cref:Meziantou.Framework.Http.LinkHeaderValue) | [Link headers](03-LinkHeaders.md) |
| `Meziantou.Framework.Http.Caching` | [HttpCachingDelegateHandler](cref:Meziantou.Framework.Http.Caching.HttpCachingDelegateHandler), [IHttpCacheStore](cref:Meziantou.Framework.Http.Caching.IHttpCacheStore) | [Caching](04-Caching/01-Overview.md) |
| `Meziantou.Framework.Http.Caching.InMemory` | [InMemoryHttpCacheStore](cref:Meziantou.Framework.Http.Caching.InMemory.InMemoryHttpCacheStore) | [Caching](04-Caching/01-Overview.md) |
| `Meziantou.Framework.Http.Caching.Sqlite` | [SqliteHttpCacheStore](cref:Meziantou.Framework.Http.Caching.Sqlite.SqliteHttpCacheStore) | [Caching](04-Caching/01-Overview.md) |
| `Meziantou.Framework.Http.Caching.Redis` | [RedisHttpCacheStore](cref:Meziantou.Framework.Http.Caching.Redis.RedisHttpCacheStore) | [Caching](04-Caching/01-Overview.md) |
| `Meziantou.Framework.Http.Hsts` | [HstsClientHandler](cref:Meziantou.Framework.Http.HstsClientHandler), [HstsDomainPolicyCollection](cref:Meziantou.Framework.Http.HstsDomainPolicyCollection) | [HSTS](05-Hsts/01-Overview.md) |
| `Meziantou.Framework.Http.Htpasswd` | [HtpasswdFile](cref:Meziantou.Framework.Http.HtpasswdFile) | [Htpasswd files](06-Htpasswd.md) |
| `Meziantou.Framework.Http.Recording` | [HttpRecordingHandler](cref:Meziantou.Framework.Http.Recording.HttpRecordingHandler) | [Recording](07-Recording/01-Overview.md) |
| `Meziantou.Framework.Http.ServerSideRequestForgery` | [ServerSideRequestForgeryClientHandler](cref:Meziantou.Framework.Http.ServerSideRequestForgery.ServerSideRequestForgeryClientHandler) | [Server-side request forgery](08-ServerSideRequestForgery/01-Overview.md) |

Each package is independent: install only the ones you need. The HAR files that the recording package reads and
writes are described by the [HttpArchive](../HttpArchive/01-Introduction.md) package.

## Next steps

- [Getting started](02-GettingStarted.md): install a package and add a handler to an `HttpClient`.
