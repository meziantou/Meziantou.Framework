# Meziantou.Framework.Http.Caching

An HTTP caching implementation for `HttpClient` that follows [RFC 7234 (HTTP Caching)](https://www.rfc-editor.org/rfc/rfc7234.html) specifications.

`Meziantou.Framework.Http.Caching` provides a `CachingDelegateHandler` that intercepts HTTP requests and caches responses according to standard HTTP caching rules. It automatically handles cache storage, validation, freshness calculation, and cache invalidation, reducing network traffic and improving application performance.

## Features

- **RFC 7234 Compliant**: Fully implements HTTP caching specifications
- **Cache-Control Directives**: Supports `max-age`, `no-cache`, `no-store`, `must-revalidate`, `max-stale`, `min-fresh`, `only-if-cached`
- **Conditional Requests**: Automatic validation using `ETag` and `Last-Modified` headers
- **Vary Support**: Caches multiple variants based on request headers (e.g., `Accept-Language`, `Accept-Encoding`)
- **Cache Invalidation**: Automatically invalidates cache entries on unsafe methods (POST, PUT, DELETE, PATCH)
- **Pragma Support**: Handles `Pragma: no-cache` for HTTP/1.0 backward compatibility
- **Thread-Safe**: Designed for concurrent access across multiple threads

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching
```

## Usage

### Basic Usage

```csharp
using Meziantou.Framework.Http;

// Create the caching handler
var innerHandler = new HttpClientHandler();
var cachingHandler = new CachingDelegateHandler(innerHandler);

// Create HttpClient with the caching handler
using var httpClient = new HttpClient(cachingHandler);

// Make requests - responses will be cached automatically
var response1 = await httpClient.GetAsync("https://api.example.com/data");
var response2 = await httpClient.GetAsync("https://api.example.com/data"); // Served from cache
```
