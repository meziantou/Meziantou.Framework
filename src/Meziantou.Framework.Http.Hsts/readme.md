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

## Redirects

The handler follows redirects itself, so every hop is checked against the policies. Without this, the inner handler would follow them below `HstsClientHandler` and a redirect to an HSTS host would be requested over HTTP:

```c#
// http://example.com/go redirects to http://github.com, which is in the preload list.
// The redirect is followed as https://github.com
using var response = await client.GetAsync("http://example.com/go");
```

To avoid following redirects twice, the constructor sets `AllowAutoRedirect` to `false` on the inner handler when it is a `SocketsHttpHandler` or an `HttpClientHandler` that would have followed them, and reuses its `MaxAutomaticRedirections` value. **The inner handler you pass in is modified.** Requests still follow redirects as before; each hop now goes through the HSTS upgrade first.

An inner handler already configured not to follow redirects is left untouched, and redirect responses are returned to the caller as before:

```c#
// HstsClientHandler does not follow redirects either: the 3xx response is returned as is
var inner = new SocketsHttpHandler { AllowAutoRedirect = false };
using var client = new HttpClient(new HstsClientHandler(inner, policies), disposeHandler: true);
```

The rules match `SocketsHttpHandler`: a redirect from HTTPS to HTTP is never followed, `300`, `301` and `302` turn a POST into a GET, `303` turns any method other than GET and HEAD into a GET, `307` and `308` keep the method and the body, and the `Authorization` header is cleared.
