---
title: Redirects
description: HstsClientHandler follows redirects itself so that every hop is upgraded; how it reconfigures the inner handler and how to avoid it.
---

## Why the handler follows redirects

A redirect followed by the inner handler never goes back through [HstsClientHandler](cref:Meziantou.Framework.Http.HstsClientHandler): a redirect to an HSTS host
would be requested over HTTP. So the handler follows the redirects itself, and every hop is checked against the
policies:

<!-- ref:../../../samples/Meziantou.Framework.Http.HstsSample/ReadmeExamples.cs;region=HstsClientHandler_Redirects;format=md-fenced-code-block;dedent=true -->
```csharp
// http://example.com/go redirects to http://github.com, which is in the preload list.
// The redirect is followed as https://github.com
using var response = await client.GetAsync("http://example.com/go");
```
<!-- endref -->

The `Strict-Transport-Security` header of every hop is read too.

The redirects are followed with the rules of `SocketsHttpHandler`:

- A `Location` header that is relative is resolved against the URI of the request that received it.
- A redirect from HTTPS to HTTP is never followed, nor a redirect to a scheme other than `http` and `https`: the
  redirect response is returned.
- `300`, `301` and `302` turn a `POST` into a `GET`, and `303` turns any method other than `GET` and `HEAD` into a
  `GET`; the body is dropped with the method. `307` and `308` keep the method and the body.
- The `Authorization` header is removed: the credentials were meant for the host that answered, not for the one it
  points to.
- The fragment of the original URL carries over to a `Location` that has none.

## The inner handler is modified

To avoid following the redirects twice, the handler turns `AllowAutoRedirect` off on the inner handler when it is a
`SocketsHttpHandler` or an `HttpClientHandler` that follows them, and uses its `MaxAutomaticRedirections` as its own
limit. A `DelegatingHandler` between them is looked through.

This happens on the first request, not in the constructor. Two consequences:

- An inner handler shared with another `HttpClient` stops following redirects for that client too.
- An inner handler that has already sent a request cannot be reconfigured: the first request through
  [HstsClientHandler](cref:Meziantou.Framework.Http.HstsClientHandler) throws an `InvalidOperationException`.

## Follow redirects without touching the inner handler

Pass `maxAutomaticRedirections` to the constructor, and configure the inner handler not to follow the redirects. This
is how you add HSTS to a shared or an already used handler, and the only way to follow redirects when the inner handler
is of another type:

<!-- ref:../../../samples/Meziantou.Framework.Http.HstsSample/ReadmeExamples.cs;region=HstsClientHandler_MaxAutomaticRedirections;format=md-fenced-code-block;dedent=true -->
```csharp
var inner = new SocketsHttpHandler { AllowAutoRedirect = false };
using var client = new HttpClient(new HstsClientHandler(inner, policies, maxAutomaticRedirections: 50), disposeHandler: true);
```
<!-- endref -->

With `maxAutomaticRedirections: 0`, the handler follows no redirect.

## Do not follow redirects

An inner handler already configured not to follow redirects is left untouched, and the handler does not follow them
either: the redirect responses are returned to the caller:

<!-- ref:../../../samples/Meziantou.Framework.Http.HstsSample/ReadmeExamples.cs;region=HstsClientHandler_NoRedirects;format=md-fenced-code-block;dedent=true -->
```csharp
// HstsClientHandler does not follow redirects either: the 3xx response is returned as is
var inner = new SocketsHttpHandler { AllowAutoRedirect = false };
using var client = new HttpClient(new HstsClientHandler(inner, policies), disposeHandler: true);
```
<!-- endref -->

An inner handler of a type the handler does not recognize keeps its own behavior, unless you pass
`maxAutomaticRedirections`.

With `IHttpClientFactory`, the primary handler belongs to the pipeline of the client, so the take-over only affects that
client. Configure the primary handler not to follow redirects only if the client must not follow them at all.
