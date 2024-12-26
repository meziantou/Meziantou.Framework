# Meziantou.Framework.Http.Hsts

This package provides an `HttpClientHandler` that automatically upgrades HTTP requests to HTTPS when the server supports HSTS. It comes with a list of preloaded HSTS hosts.

```c#
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policies), disposeHandler: true);

// Automatically upgrade to HTTPS as google.com is in the HSTS preload list
using var response = await client.GetAsync("http://google.com");
```
