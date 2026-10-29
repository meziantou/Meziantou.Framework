---
title: Public Suffix List
description: Find the public suffix, the registrable domain and the subdomain of a host name, and tell whether two hosts are the same site.
---

The [Public Suffix List](https://publicsuffix.org/list/) lists the domain names under which anyone can register a
name: `com`, `co.uk`, and services such as `github.io`. It tells which part of a host name a registrant controls: use
it to scope cookies, or to decide whether two hosts are the same site.

`PublicSuffixList` looks host names up in a snapshot of the list compiled into the assembly: it never accesses the
network.

## Decompose a host name

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/PublicSuffixListTests.cs;region=PublicSuffixList_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Get the registrable domain (eTLD+1)
Assert.Equal("example.co.uk", PublicSuffixList.GetRegistrableDomain("www.example.co.uk"));
Assert.Null(PublicSuffixList.GetRegistrableDomain("co.uk"));

// Get the public suffix (eTLD)
Assert.Equal("co.uk", PublicSuffixList.GetPublicSuffix("www.example.co.uk"));

// Test if a domain is a public suffix
Assert.True(PublicSuffixList.IsPublicSuffix("co.uk"));
Assert.False(PublicSuffixList.IsPublicSuffix("example.com"));

// Decompose a domain name
Assert.True(PublicSuffixList.TryGetDomainInfo("www.a.example.co.uk", out DomainInfo info));
Assert.Equal("co.uk", info.PublicSuffix);
Assert.Equal("example.co.uk", info.RegistrableDomain);
Assert.Equal("www.a", info.Subdomain);
Assert.Equal(PublicSuffixRuleSources.Icann, info.Source);
Assert.True(info.IsKnownPublicSuffix);

// A Uri can be used directly
Assert.True(PublicSuffixList.TryGetDomainInfo(new System.Uri("https://www.example.co.uk/path"), out _));
```
<!-- endref -->

| Member | `www.a.example.co.uk` |
| --- | --- |
| `GetPublicSuffix`, `DomainInfo.PublicSuffix` | `co.uk`: the public suffix, or effective top-level domain (eTLD). |
| `GetRegistrableDomain`, `DomainInfo.RegistrableDomain` | `example.co.uk`: the public suffix and one more label (eTLD+1). |
| `DomainInfo.Subdomain` | `www.a`: the labels before the registrable domain. |
| `DomainInfo.Domain` | `www.a.example.co.uk`: the normalized host name. |

- `GetRegistrableDomain` returns `null` when the host name is itself a public suffix, such as `co.uk`, and
  `DomainInfo.Subdomain` is `null` when there is no label before the registrable domain.
- `TryGetDomainInfo` returns everything in one lookup. It accepts a string, a `ReadOnlySpan<char>` or a `Uri`, whose
  host must be a domain name.
- `IsPublicSuffix` tells whether the host name is a suffix of the list.

## Tell whether two hosts are the same site

Two hosts belong to the same site when they have the same registrable domain:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/PublicSuffixListTests.cs;region=PublicSuffixList_SameSite;format=md-fenced-code-block;dedent=true -->
```csharp
static bool IsSameSite(string host1, string host2)
{
    var site1 = PublicSuffixList.GetRegistrableDomain(host1);
    var site2 = PublicSuffixList.GetRegistrableDomain(host2);
    return site1 is not null && site1 == site2;
}

Assert.True(IsSameSite("www.example.co.uk", "shop.example.co.uk"));
Assert.False(IsSameSite("example.co.uk", "contoso.co.uk"));

// github.io is a public suffix: two sites hosted there are different sites
Assert.False(IsSameSite("alice.github.io", "bob.github.io"));
```
<!-- endref -->

Check that the registrable domain is not `null`: a host name that is a public suffix has none, and two of them are not
the same site.

## Normalization and invalid input

The host name is lower-cased and a trailing dot is ignored. A host name whose top-level domain is not in the list still
has a public suffix, by the implicit `*` rule of the list:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/PublicSuffixListTests.cs;region=PublicSuffixList_Normalization;format=md-fenced-code-block;dedent=true -->
```csharp
// The domain is lower-cased and a trailing dot is ignored
Assert.True(PublicSuffixList.TryGetDomainInfo("WWW.Example.COM.", out var info));
Assert.Equal("www.example.com", info.Domain);
Assert.Equal("example.com", info.RegistrableDomain);

// A top-level domain that is not in the list is still a public suffix, by the implicit "*" rule
Assert.True(PublicSuffixList.TryGetDomainInfo("www.example.unknowntld", out var unknown));
Assert.Equal("unknowntld", unknown.PublicSuffix);
Assert.Equal("example.unknowntld", unknown.RegistrableDomain);
Assert.False(unknown.IsKnownPublicSuffix);
Assert.Equal(PublicSuffixRuleSources.None, unknown.Source);
Assert.False(PublicSuffixList.IsPublicSuffix("unknowntld"));

// A value that is not a domain name
Assert.False(PublicSuffixList.TryGetDomainInfo("example..com", out _));
Assert.Null(PublicSuffixList.GetRegistrableDomain(""));

// The host of a Uri must be a domain name, not an IP address
Assert.False(PublicSuffixList.TryGetDomainInfo(new System.Uri("https://192.168.1.1/"), out _));
```
<!-- endref -->

- `DomainInfo.IsKnownPublicSuffix` is `false` when no rule of the list matched and the implicit rule applied, and
  `IsPublicSuffix` returns `false` for such a top-level domain.
- A value that is not a domain name (an empty label, a forbidden character, more than 253 characters) makes
  `TryGetDomainInfo` return `false`, and the other methods return `null` or `false`. They do not throw.
- An internationalized host name keeps the form it was given in: the lookup uses its ASCII form, and the results are
  parts of the normalized input.

## Choose the sections of the list

The list has two sections: the suffixes delegated by ICANN, and the private suffixes that domain holders submitted,
such as `blogspot.com` or `github.io`. Both are used by default. Pass a `PublicSuffixRuleSources` value to use one of
them only:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/PublicSuffixListTests.cs;region=PublicSuffixList_RuleSources;format=md-fenced-code-block;dedent=true -->
```csharp
// blogspot.com is a private rule
Assert.Equal("foo.blogspot.com", PublicSuffixList.GetRegistrableDomain("foo.blogspot.com"));
Assert.Equal("blogspot.com", PublicSuffixList.GetRegistrableDomain("foo.blogspot.com", PublicSuffixRuleSources.Icann));
```
<!-- endref -->

`DomainInfo.Source` tells which section the matching rule comes from, and is `PublicSuffixRuleSources.None` when no rule
matched. Keep both sections to decide what a registrant controls; use `Icann` alone to group host names by the domain
that was bought from a registrar.

## Version of the list

The list changes over time, and the snapshot is updated when a new version of the package is released.
`PublicSuffixList.LastUpdated` returns the date of the snapshot and `PublicSuffixList.RuleCount` its number of rules.
Update the package to get the recent suffixes.

## Next steps

- [Limitations](08-Limitations.md): what the library does not do.
