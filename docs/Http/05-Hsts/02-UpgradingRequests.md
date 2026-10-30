---
title: Upgrading requests
description: Add HstsClientHandler to an HttpClient, learn the policies of the hosts from their Strict-Transport-Security header, and register the handler with IHttpClientFactory.
---

## Upgrade the requests of an HttpClient

Create an [HstsClientHandler](cref:Meziantou.Framework.Http.HstsClientHandler) with the handler that sends the requests and the collection of policies to apply:

<!-- ref:../../../samples/Meziantou.Framework.Http.HstsSample/ReadmeExamples.cs;region=HstsClientHandler_UpgradePreloadedHost;format=md-fenced-code-block;dedent=true -->
```csharp
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);

// Automatically upgrade to HTTPS as github.com is in the HSTS preload list
using var response = await client.GetAsync("http://github.com");
```
<!-- endref -->

A request is upgraded when its scheme is `http` and its host has a policy in the collection, either its own or the one
of a parent domain that includes the subdomains. The handler then replaces the scheme with `https`:

- The default port becomes 443, and an explicit port is kept: `http://example.com:8080/` becomes
  `https://example.com:8080/`.
- The path, the query, the fragment and the user information are kept as they are.
- An internationalized host name matches the policies in its Unicode and its Punycode forms.

The other requests, including `https` ones, are sent unchanged.

The constructors that take no collection use [HstsDomainPolicyCollection.Default](cref:Meziantou.Framework.Http.HstsDomainPolicyCollection.Default), a collection shared by the whole
process. See [The shared collection](03-ManagingPolicies.md#the-shared-collection).

## Learn policies from the Strict-Transport-Security header

The handler reads the `Strict-Transport-Security` header of every response. When an HTTPS response has a valid one,
the host is added to the collection, and the next requests to it are upgraded:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsClientHandlerTests.cs;region=HstsClientHandler_LearnFromHeader;format=md-fenced-code-block;dedent=true -->
```csharp
var policies = new HstsDomainPolicyCollection(includePreloadDomains: false);
using var client = new HttpClient(new HstsClientHandler(innerHandler, policies), disposeHandler: true);

// The HTTPS response carries "Strict-Transport-Security: max-age=31536000; includeSubDomains"
using var response1 = await client.GetAsync(new Uri("https://example.com/"), cancellationToken);
Assert.True(policies.MustUpgradeRequest("www.example.com"));

// From now on, the requests to the host and its subdomains are sent over HTTPS
using var response2 = await client.GetAsync(new Uri("http://www.example.com/page"), cancellationToken);
Assert.Equal(new Uri("https://www.example.com/page"), response2.RequestMessage?.RequestUri);
```
<!-- endref -->

Here, `innerHandler` stands for the handler that sends the requests; the server answers with
`Strict-Transport-Security: max-age=31536000; includeSubDomains`.

The header is processed as RFC 6797 describes:

- `max-age` (in seconds) is required: the policy expires after it. A header without it, or with a malformed one, is
  ignored, and the response is returned as usual.
- `includeSubDomains` extends the policy to every subdomain of the host.
- `max-age=0` removes the policy the host had declared. It does not remove a host of the preload list (see
  [The preload list is a floor](03-ManagingPolicies.md#the-preload-list-is-a-floor)).
- A header received over plaintext HTTP is ignored, so a network attacker cannot add or remove a policy.
- A header received from an IP address is ignored: only host names get a policy.
- When a response has several `Strict-Transport-Security` headers, only the first one is read.
- A `max-age` larger than 100 years is reduced to 100 years.

The policies learned this way are kept in memory only: they are lost when the process exits.

## Register the handler with IHttpClientFactory

The constructors that take no inner handler let `IHttpClientFactory` set it:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsClientHandlerTests.cs;region=HstsClientHandler_DependencyInjection;format=md-fenced-code-block;dedent=true -->
```csharp
services.AddTransient(_ => new HstsClientHandler(policies));
services.AddHttpClient("api")
        .AddHttpMessageHandler<HstsClientHandler>()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler());
```
<!-- endref -->

The handler takes over the redirects of the primary handler that the factory creates. See [Redirects](04-Redirects.md).
