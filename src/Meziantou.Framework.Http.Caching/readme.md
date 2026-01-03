# Meziantou.Framework.Http.Caching

An HTTP caching implementation for `HttpClient` that follows [RFC 7234 (HTTP Caching)](https://www.rfc-editor.org/rfc/rfc7234.html) and [RFC 8246 (Immutable directive)](https://www.rfc-editor.org/rfc/rfc8246.html) specifications.

## What is it?

`Meziantou.Framework.Http.Caching` provides a `CachingDelegateHandler` that intercepts HTTP requests and caches responses according to standard HTTP caching rules. It automatically handles cache storage, validation, freshness calculation, and cache invalidation, reducing network traffic and improving application performance.

## Features

- ✅ **RFC 7234 Compliant**: Fully implements HTTP caching specifications
- ✅ **RFC 8246 Immutable**: Supports the `immutable` directive to prevent unnecessary revalidation
- ✅ **Cache-Control Directives**: Supports `max-age`, `no-cache`, `no-store`, `must-revalidate`, `max-stale`, `min-fresh`, `only-if-cached`
- ✅ **Conditional Requests**: Automatic validation using `ETag` and `Last-Modified` headers
- ✅ **Vary Support**: Caches multiple variants based on request headers (e.g., `Accept-Language`, `Accept-Encoding`)
- ✅ **Cache Invalidation**: Automatically invalidates cache entries on unsafe methods (POST, PUT, DELETE, PATCH)
- ✅ **Pragma Support**: Handles `Pragma: no-cache` for HTTP/1.0 backward compatibility
- ✅ **Thread-Safe**: Designed for concurrent access across multiple threads
- ✅ **Testable**: Supports custom `TimeProvider` for deterministic testing

## Installation

```bash
dotnet add package Meziantou.Framework.Http.Caching
```

## Usage

### Basic Usage

```csharp
using Meziantou.Framework.Http;

// Create the caching handler
var cachingHandler = new CachingDelegateHandler();

// Create HttpClient with the caching handler
using var httpClient = new HttpClient(cachingHandler);

// Make requests - responses will be cached automatically
var response1 = await httpClient.GetAsync("https://api.example.com/data");
var response2 = await httpClient.GetAsync("https://api.example.com/data"); // Served from cache
```

### With Custom Inner Handler

```csharp
using Meziantou.Framework.Http;

var innerHandler = new HttpClientHandler();
var cachingHandler = new CachingDelegateHandler(innerHandler);

using var httpClient = new HttpClient(cachingHandler);
var response = await httpClient.GetAsync("https://api.example.com/data");
```

### With Dependency Injection

```csharp
services.AddHttpClient("MyApi")
    .AddHttpMessageHandler<CachingDelegateHandler>();

// Register the handler
services.AddTransient<CachingDelegateHandler>();
```

### With Custom TimeProvider (for testing)

```csharp
using Microsoft.Extensions.Time.Testing;

var fakeTimeProvider = new FakeTimeProvider();
var cachingHandler = new CachingDelegateHandler(fakeTimeProvider);

using var httpClient = new HttpClient(cachingHandler);

// Make a request
var response1 = await httpClient.GetAsync("https://api.example.com/data");

// Advance time
fakeTimeProvider.Advance(TimeSpan.FromMinutes(5));

// Make another request (cache freshness is evaluated with the fake time)
var response2 = await httpClient.GetAsync("https://api.example.com/data");
```

## How It Works

### Caching Behavior

The handler follows these rules when processing requests:

1. **Only GET and HEAD requests are cached** (per RFC 7234 Section 4)
2. **Checks request directives** like `no-store`, `no-cache`, `max-age`, `min-fresh`, `max-stale`, `only-if-cached`
3. **Evaluates cache freshness** using `Cache-Control: max-age` and `Expires` headers
4. **Validates stale entries** using conditional requests with `If-None-Match` (ETag) or `If-Modified-Since`
5. **Handles 304 Not Modified** responses by updating and serving the cached entry
6. **Stores new responses** according to caching rules

### Cache Invalidation

Unsafe HTTP methods (POST, PUT, DELETE, PATCH) automatically invalidate cache entries for:
- The request URI
- URIs in `Location` header
- URIs in `Content-Location` header

### Vary Header Support

The cache differentiates responses based on headers specified in the `Vary` response header:

```csharp
// Server responds with:
// Cache-Control: max-age=3600
// Vary: Accept-Language

var request1 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request1.Headers.Add("Accept-Language", "en-US");
var response1 = await httpClient.SendAsync(request1); // Caches with Accept-Language=en-US

var request2 = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request2.Headers.Add("Accept-Language", "fr-FR");
var response2 = await httpClient.SendAsync(request2); // Fetches new response and caches separately
```

### Immutable Directive (RFC 8246)

The `immutable` directive indicates that the response body will not change over time. When a response has `Cache-Control: immutable` and is still fresh, the cache will NOT perform conditional revalidation even if the client sends `Cache-Control: no-cache`:

```csharp
// Server responds with:
// Cache-Control: max-age=31536000, immutable
// ETag: "abc123"

// First request fetches from origin
var response1 = await httpClient.GetAsync("https://cdn.example.com/script.js");

// Second request with no-cache still uses cached version (because immutable + fresh)
var request2 = new HttpRequestMessage(HttpMethod.Get, "https://cdn.example.com/script.js");
request2.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
var response2 = await httpClient.SendAsync(request2); // Served from cache, no revalidation
```

This is particularly useful for versioned resources (e.g., `/assets/script.v123.js`) that never change once published.

## Supported HTTP Headers

### Request Headers

| Header | Directive | Description |
|--------|-----------|-------------|
| `Cache-Control` | `no-store` | Prevents caching the response |
| | `no-cache` | Forces validation before using cached response (unless response is `immutable` and fresh) |
| | `max-age=<seconds>` | Maximum age for cached response |
| | `min-fresh=<seconds>` | Requires response to be fresh for at least this duration |
| | `max-stale[=<seconds>]` | Accepts stale responses (optionally up to specified staleness) |
| | `only-if-cached` | Returns cached response or 504 Gateway Timeout |
| `Pragma` | `no-cache` | HTTP/1.0 compatibility (equivalent to `Cache-Control: no-cache`) |
| `If-None-Match` | `<etag>` | Used for conditional validation |
| `If-Modified-Since` | `<date>` | Used for conditional validation |

### Response Headers

| Header | Directive | Description |
|--------|-----------|-------------|
| `Cache-Control` | `max-age=<seconds>` | How long the response is fresh |
| | `no-cache` | Requires validation before reuse |
| | `no-store` | Must not be cached |
| | `must-revalidate` | Must validate when stale |
| | `immutable` | Response will never change; prevents conditional revalidation when fresh (RFC 8246) |
| `Expires` | `<date>` | Expiration date (fallback if `max-age` not present) |
| `ETag` | `<value>` | Entity tag for conditional requests |
| `Last-Modified` | `<date>` | Last modification date for conditional requests |
| `Vary` | `<headers>` | Headers that affect response variant |
| `Age` | `<seconds>` | Age of cached response (added when serving from cache) |

## Thread Safety

The `CachingDelegateHandler` is thread-safe and can be used concurrently across multiple threads. The internal cache implementation ensures that concurrent requests to the same URL are properly coordinated.

## Examples

### Force Revalidation

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
var response = await httpClient.SendAsync(request);
// Note: If the response has Cache-Control: immutable and is fresh, it will still be served from cache
```

### Accept Stale Responses

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request.Headers.CacheControl = new CacheControlHeaderValue 
{ 
    MaxStale = true,
    MaxStaleLimit = TimeSpan.FromMinutes(5) // Accept up to 5 minutes stale
};
var response = await httpClient.SendAsync(request);
```

### Only Use Cache

```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
request.Headers.CacheControl = new CacheControlHeaderValue { OnlyIfCached = true };
var response = await httpClient.SendAsync(request);
// Returns cached response or 504 Gateway Timeout
```

## Testing

The library includes comprehensive tests demonstrating various caching scenarios. See the test project for examples of:
- Cache-Control directive handling
- Conditional request validation
- Vary header support
- Cache invalidation
- Thread safety
- URL handling
- Immutable directive behavior

## License

This project is part of the [Meziantou.Framework](https://github.com/meziantou/Meziantou.Framework) library.
