# Meziantou.Framework.Http.Hsts

This package provides an `HttpClientHandler` that automatically upgrades HTTP requests to HTTPS when the server supports HSTS. It comes with a list of preloaded HSTS hosts.

```c#
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);

// Automatically upgrade to HTTPS as github.com is in the HSTS preload list
using var response = await client.GetAsync("http://github.com");
```

The handler reads the `Strict-Transport-Security` header of HTTPS responses and adds the host to the collection, so later requests to that host are upgraded. A header received over plaintext HTTP is ignored, as are headers from an IP address. Policies learned this way are kept in memory only, and an expired one is dropped either when a request looks up its host or by a periodic sweep; use `Remove` to drop a policy before it expires.

`Add` rejects a host name a request can never produce — one carrying a scheme, a port, a path, userinfo, whitespace or an empty label — rather than storing a policy that would silently never match.

## The preload list is a floor

The built-in preload list is a separate, immutable layer. A policy learned from a response header may **widen** the coverage of a preloaded host but can never narrow it, expire it or delete it, so neither `max-age=0` nor a short `max-age` can take a host off the built-in list:

```c#
// github.com is preloaded with includeSubdomains. Nothing a server sends changes that.
policies.Add("github.com", TimeSpan.FromSeconds(1), includeSubdomains: false);
policies.MustUpgradeRequest("sub.github.com"); // still true, now and after the second has passed
```

`Remove` is the only way to take a host off the list, and it does so for that collection only — the preload data itself is shared between collections and never modified.

## Learned policies are bounded

A collection keeps at most `MaxLearnedPolicies` policies learned from response headers (`DefaultMaxLearnedPolicies`, 10,000). When the limit is reached, expired policies are dropped first and then the ones closest to expiring. Preloaded entries do not count towards the limit. Raise it for an application that legitimately talks to many HSTS hosts:

```c#
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true, maxLearnedPolicies: 100_000);
```

The limit exists because any server can add an entry: a service that fetches user-supplied URLs would otherwise let a remote peer grow the store without bound.

## The shared collection

The `HstsClientHandler` constructors that take no collection use `HstsDomainPolicyCollection.Default`, a shared instance for the whole process. Every handler using it observes the policies learned by the others. Create a dedicated `HstsDomainPolicyCollection` to isolate a client, and call `ClearLearnedPolicies()` to return a collection to the state it was constructed in — learned policies dropped, preload entries restored. That is also how to isolate tests that share `Default`.

`TryGetPolicy` reports the policy registered for one host without enumerating the collection:

```c#
if (policies.TryGetPolicy("github.com", out var policy))
{
    Console.WriteLine($"{policy.Host} preloaded={policy.IsPreloaded} expires={policy.ExpiresAt}");
}
```

Enumerating the collection is a moment-in-time view that materializes the ~95,000 preloaded host names as it goes, so prefer `MustUpgradeRequest` or `TryGetPolicy` to answer a question about one host.

## Redirects

The handler follows redirects itself, so every hop is checked against the policies. Without this, the inner handler would follow them below `HstsClientHandler` and a redirect to an HSTS host would be requested over HTTP:

```c#
// http://example.com/go redirects to http://github.com, which is in the preload list.
// The redirect is followed as https://github.com
using var response = await client.GetAsync("http://example.com/go");
```

To avoid following redirects twice, the handler sets `AllowAutoRedirect` to `false` on the inner handler when it is a `SocketsHttpHandler` or an `HttpClientHandler` that would have followed them, and reuses its `MaxAutomaticRedirections`. **The inner handler you pass in is modified**, on the first request rather than in the constructor. Two consequences:

- An inner handler shared with another `HttpClient` stops following redirects for that client too.
- An inner handler that has already sent a request can no longer be reconfigured, and the first request through `HstsClientHandler` throws `InvalidOperationException`.

Pass `maxAutomaticRedirections` to take the redirects over without touching the inner handler. That is the way to add HSTS to a shared or already-used handler, and the only way to follow redirects when the inner handler is neither of the two recognized types (a `WinHttpHandler`, say, or a custom handler that resolves redirects itself):

```c#
var inner = new SocketsHttpHandler { AllowAutoRedirect = false };
using var client = new HttpClient(new HstsClientHandler(inner, policies, maxAutomaticRedirections: 50), disposeHandler: true);
```

An inner handler already configured not to follow redirects is left untouched, and redirect responses are returned to the caller as before:

```c#
// HstsClientHandler does not follow redirects either: the 3xx response is returned as is
var inner = new SocketsHttpHandler { AllowAutoRedirect = false };
using var client = new HttpClient(new HstsClientHandler(inner, policies), disposeHandler: true);
```

The rules match `SocketsHttpHandler`: a redirect from HTTPS to HTTP is never followed, `300`, `301` and `302` turn a POST into a GET, `303` turns any method other than GET and HEAD into a GET, `307` and `308` keep the method and the body, the `Authorization` header is cleared, and the fragment of the original request carries over to a `Location` that has none.

## Dependency injection

`AddHttpMessageHandler` requires a handler whose `InnerHandler` is unset, which the constructors that take no inner handler provide:

```c#
services.AddTransient(_ => new HstsClientHandler(policies));
services.AddHttpClient("api")
        .AddHttpMessageHandler<HstsClientHandler>()
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
```

Configure the primary handler not to follow redirects, or pass `maxAutomaticRedirections`; otherwise the first request throws once the factory has built the pipeline, because the primary handler it created is already in use.

## Dropping the preload list

The preload list holds about 95,000 host names. It is stored as a sorted blob that is searched in place and shared between every collection, so it costs roughly 2 MB of managed memory and a few tens of milliseconds, paid once per process and only when something asks for it. `new HstsDomainPolicyCollection(includePreloadDomains: false)` skips it for one collection.

An application that never wants it can turn it off process-wide with a feature switch, which also keeps `HstsDomainPolicyCollection.Default` from loading it:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Meziantou.Framework.Http.Hsts.IncludePreloadList" Value="false" Trim="true" />
</ItemGroup>
```

The embedded resources holding the list still ship inside the assembly (about 600 KB); the switch only stops them being read.
