---
title: The preload list
description: The HSTS preload list built into the package, how it is loaded and shared, and how to turn it off for a collection or for the whole application.
---

The package embeds the HSTS preload list of Chromium: the hosts that the browsers send over HTTPS from the first
request, without having seen their `Strict-Transport-Security` header. It holds tens of thousands of host names, with
whether each one includes its subdomains. The list is compiled into the assembly: update the package to get a newer
one.

A collection created with `includePreloadDomains: true`, the default, applies it. That includes
`HstsDomainPolicyCollection.Default`. The preloaded policies never expire: `HstsDomainPolicy.IsPreloaded` is `true` and
`ExpiresAt` is `DateTimeOffset.MaxValue` for them.

## How the list is loaded

The list is loaded the first time a collection needs it, once per process, and shared by every collection: creating
more collections does not load it again. It is searched in place, without creating an object per host.

## Turn the list off for a collection

Pass `includePreloadDomains: false` to create a collection without it. The handler then only upgrades the hosts that
you add and the ones that send a `Strict-Transport-Security` header:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsDomainPolicyCollectionTests.cs;region=HstsDomainPolicyCollection_TryGetPolicyOfASubdomain;format=md-fenced-code-block;dedent=true -->
```csharp
var policies = new HstsDomainPolicyCollection(includePreloadDomains: false);
policies.Add("example.com", TimeSpan.FromDays(365), includeSubdomains: true);

// www.example.com is covered by the policy of example.com, but has no policy of its own
Assert.True(policies.MustUpgradeRequest("www.example.com"));
Assert.False(policies.TryGetPolicy("www.example.com", out _));
```
<!-- endref -->

## Turn the list off for the application

An application that never wants the list can turn it off with a feature switch in its project file. No collection
loads it then, `HstsDomainPolicyCollection.Default` included, even with `includePreloadDomains: true`:

```xml
<ItemGroup>
  <RuntimeHostConfigurationOption Include="Meziantou.Framework.Http.Hsts.IncludePreloadList" Value="false" Trim="true" />
</ItemGroup>
```

With `Trim="true"`, a trimmed application also drops the code that loads the list. The embedded data still ships in
the assembly: the switch only stops it from being read.
