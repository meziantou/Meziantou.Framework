# Meziantou.Framework.Http.Hsts

This package provides an `HttpClientHandler` that automatically upgrades HTTP requests to HTTPS when the server supports HSTS. It comes with a list of preloaded HSTS hosts.

```c#
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);

// Automatically upgrade to HTTPS as github.com is in the HSTS preload list
using var response = await client.GetAsync("http://github.com");
```

The handler reads the `Strict-Transport-Security` header of HTTPS responses and adds the host to the collection, so later requests to that host are upgraded. A `max-age=0` header removes the policy, unless the host comes from the preload list. Policies learned this way are kept in memory only, and are not removed when they expire; use `Remove` to drop the ones you no longer need.

The `HstsClientHandler` constructor that takes no collection uses `HstsDomainPolicyCollection.Default`, a shared instance for the whole process. Every handler using it observes the policies learned by the others. Create a dedicated `HstsDomainPolicyCollection` to isolate a client from the rest of the application.

Redirects are followed by the handler itself, so each hop is checked against the policies: a redirect to an HSTS host is upgraded instead of being requested over HTTP. When the inner handler is a `SocketsHttpHandler` or an `HttpClientHandler` that follows redirects, the constructor turns its `AllowAutoRedirect` off and takes them over, keeping its `MaxAutomaticRedirections` limit. An inner handler already configured not to follow redirects keeps returning the redirect responses as is.
