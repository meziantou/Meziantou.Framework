---
title: Managing policies
description: Add, check, expire and remove HSTS policies in an HstsDomainPolicyCollection, share or isolate a collection, and bound what servers can add.
---

An `HstsDomainPolicyCollection` holds the policies that `HstsClientHandler` applies. You can use it on its own, to
decide whether a URL must be upgraded, or edit it to add hosts that the preload list and the headers do not cover.

## Add a policy and check a host

`Add` registers a host for a duration, or until a date with the `DateTimeOffset` overload. `MustUpgradeRequest` tells
whether a request to a host must be sent over HTTPS:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsDomainPolicyCollectionTests.cs;region=HstsDomainPolicyCollection_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Create a collection with preloaded domains from the Chromium HSTS preload list
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
Assert.True(policies.MustUpgradeRequest("github.com"));

// Add a custom domain policy
policies.Add("example.com", TimeSpan.FromDays(365), includeSubdomains: true);

// Check if a domain should be upgraded to HTTPS
Assert.True(policies.MustUpgradeRequest("example.com"));
Assert.True(policies.MustUpgradeRequest("www.example.com"));
```
<!-- endref -->

- Adding a host that already has a policy replaces it.
- `includeSubdomains: true` covers every subdomain of the host.
- Host names are compared case-insensitively, and a trailing dot is ignored. An internationalized name can be given in
  its Unicode or its Punycode form; the policy stores the Punycode form, which `HstsDomainPolicy.Host` reports.
- `Add` throws an `ArgumentException` for a value that is not a host name, such as one with a scheme, a port, a path,
  user information, whitespace or an empty label: a request could never match it.

## Expiration

A policy stops applying when its expiration date passes. The constructor that takes a `TimeProvider` sets the clock of
the collection, which lets a test move time forward:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsDomainPolicyCollectionTests.cs;region=HstsDomainPolicyCollection_Expiration;format=md-fenced-code-block;dedent=true -->
```csharp
var timeProvider = new FakeTimeProvider();
var policies = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: false);

policies.Add("example.com", TimeSpan.FromDays(1), includeSubdomains: false);
Assert.True(policies.MustUpgradeRequest("example.com"));
Assert.False(policies.MustUpgradeRequest("www.example.com")); // Subdomains are not included

timeProvider.Advance(TimeSpan.FromDays(2));
Assert.False(policies.MustUpgradeRequest("example.com")); // The policy has expired
```
<!-- endref -->

`FakeTimeProvider` comes from the `Microsoft.Extensions.TimeProvider.Testing` package. An expired policy is removed
when its host is looked up, and by a sweep that runs from time to time as policies are added.

## Read the policy of a host

`TryGetPolicy` returns the policy registered for a host, with its expiration date, whether it includes the subdomains,
and whether it comes from the preload list:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsDomainPolicyCollectionTests.cs;region=HstsDomainPolicyCollection_TryGetPolicy;format=md-fenced-code-block;dedent=true -->
```csharp
if (policies.TryGetPolicy("github.com", out var policy))
{
    Assert.Equal("github.com", policy.Host);
    Assert.True(policy.IsPreloaded);
    Assert.True(policy.IncludeSubdomains);
}
```
<!-- endref -->

It reports the policy of that exact host. A host covered only by the policy of a parent domain has none of its own:
use `MustUpgradeRequest` to know whether a request to it is upgraded.

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsDomainPolicyCollectionTests.cs;region=HstsDomainPolicyCollection_TryGetPolicyOfASubdomain;format=md-fenced-code-block;dedent=true -->
```csharp
var policies = new HstsDomainPolicyCollection(includePreloadDomains: false);
policies.Add("example.com", TimeSpan.FromDays(365), includeSubdomains: true);

// www.example.com is covered by the policy of example.com, but has no policy of its own
Assert.True(policies.MustUpgradeRequest("www.example.com"));
Assert.False(policies.TryGetPolicy("www.example.com", out _));
```
<!-- endref -->

The collection is also enumerable. Enumeration is a snapshot that goes through the whole preload list, in no
particular order, and it can include expired policies, so prefer `MustUpgradeRequest` and `TryGetPolicy` to answer a
question about one host.

## Remove policies

`Remove` takes a host off the collection, including a host of the preload list. `ClearLearnedPolicies` drops the
policies added with `Add` or learned from headers, and restores the preloaded hosts that `Remove` took off:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsDomainPolicyCollectionTests.cs;region=HstsDomainPolicyCollection_RemoveAndClear;format=md-fenced-code-block;dedent=true -->
```csharp
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);
policies.Add("example.com", TimeSpan.FromDays(365), includeSubdomains: true);

// Remove takes a host off the list, even a preloaded one, for this collection only
Assert.True(policies.Remove("github.com"));
Assert.False(policies.MustUpgradeRequest("github.com"));

// ClearLearnedPolicies returns the collection to its initial state
policies.ClearLearnedPolicies();
Assert.False(policies.MustUpgradeRequest("example.com"));
Assert.True(policies.MustUpgradeRequest("github.com"));
```
<!-- endref -->

Removing a preloaded host affects that collection only: the preload data is shared between the collections and never
modified.

## The preload list is a floor

A policy added for a preloaded host, by `Add` or by a header, can widen what the preload list covers but never narrow
it: neither `max-age=0` nor a short `max-age` nor `includeSubdomains: false` takes a host off the built-in list:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsDomainPolicyCollectionTests.cs;region=HstsDomainPolicyCollection_PreloadListIsAFloor;format=md-fenced-code-block;dedent=true -->
```csharp
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true);

// github.com is preloaded with includeSubdomains. Nothing a server sends changes that.
policies.Add("github.com", TimeSpan.FromSeconds(1), includeSubdomains: false);
Assert.True(policies.MustUpgradeRequest("sub.github.com")); // still true, now and after the second has passed
```
<!-- endref -->

`Remove` is the only way to take a preloaded host off a collection.

## The shared collection

`HstsDomainPolicyCollection.Default` is a single collection for the whole process, with the preload list. Every
`HstsClientHandler` created without a collection uses it, so each of them applies the policies the others learned.

Create your own collection to isolate a client, for instance a client that talks to test servers, or tests that must
not see each other's policies. `Default.ClearLearnedPolicies()` returns the shared collection to its initial state.

## Bound the learned policies

Any server your application talks to can add a policy, so a collection keeps at most `MaxLearnedPolicies` of the
policies added with `Add` or learned from headers: 10,000 by default (`DefaultMaxLearnedPolicies`). The preloaded hosts
do not count. Raise the limit for an application that talks to many HSTS hosts:

<!-- ref:../../../tests/Meziantou.Framework.Http.Hsts.Tests/HstsDomainPolicyCollectionTests.cs;region=HstsDomainPolicyCollection_MaxLearnedPolicies;format=md-fenced-code-block;dedent=true -->
```csharp
var policies = new HstsDomainPolicyCollection(includePreloadDomains: true, maxLearnedPolicies: 100_000);
```
<!-- endref -->

When the limit is exceeded, the expired policies are dropped first. If that is not enough, the collection evicts
policies down to 90% of the limit. Eviction ignores `max-age`, which the server chooses: the policies are grouped by
domain, and each domain shares what it may keep evenly between its subdomains. A server that answers for thousands of
its own subdomains only evicts its own policies.
