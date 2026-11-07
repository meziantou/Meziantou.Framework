---
title: Getting started
description: Install the package, match a URL against a pattern, edit a query string and compare two sites.
---

## Install the package

Add the `Meziantou.Framework.Uri` package to a project:

<InstallNuGetPackage Id="Meziantou.Framework.Uri" />

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`, and
`using Microsoft.Extensions.Primitives;` for the examples that use `StringValues`.

## Match a URL against a pattern

[UrlPattern.Create](cref:Meziantou.Framework.UrlPattern.Create) parses a pattern. [IsMatch](cref:Meziantou.Framework.UrlPattern.IsMatch) tests a URL, and [Match](cref:Meziantou.Framework.UrlPattern.Match) returns the values of the groups:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Create a pattern and test if a URL matches
var pattern = UrlPattern.Create("https://example.com/users/:id");
Assert.True(pattern.IsMatch("https://example.com/users/123"));

// Extract captured groups from a URL
var result = pattern.Match("https://example.com/users/456");
Assert.NotNull(result);
Assert.Equal("456", result.Pathname.Groups["id"]);

// Use wildcards to match any value
var wildcardPattern = UrlPattern.Create("https://*.example.com/*");
Assert.True(wildcardPattern.IsMatch("https://api.example.com/data"));
```
<!-- endref -->

- `:id` is a named group: it matches one path segment, and its value is in the result.
- `*` is a wildcard: it matches any text.

See [Matching URLs](03-MatchingUrls.md) and [Pattern syntax](04-PatternSyntax.md).

## Read and edit a query string

[QueryStringUtilities](cref:Meziantou.Framework.QueryStringUtilities) parses a query string and returns a new URL with parameters added, replaced or removed:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/QueryStringUtilitiesTests.cs;region=QueryStringUtilities_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var query = QueryStringUtilities.ParseQuery("?author=meziantou&tag=dotnet&tag=csharp");
Assert.Equal("meziantou", query["author"].ToString());
Assert.Equal(["dotnet", "csharp"], query["tag"].ToArray());

var uri = "https://www.meziantou.net/?author=meziantou";
Assert.Equal("https://www.meziantou.net/?author=meziantou&name=value", QueryStringUtilities.AddQueryString(uri, "name", "value"));
Assert.Equal("https://www.meziantou.net/?author=john", QueryStringUtilities.AddOrReplaceQueryString(uri, "author", "john"));

// Replace the whole query string
Assert.Equal("https://www.meziantou.net/?name=value", QueryStringUtilities.SetQueryString(uri, [KeyValuePair.Create("name", (string?)"value")]));

// Remove a query string parameter
Assert.Equal("https://www.meziantou.net/", QueryStringUtilities.RemoveQueryString(uri, "author"));
```
<!-- endref -->

See [Query strings](06-QueryStrings.md).

## Compare two sites

[PublicSuffixList.GetRegistrableDomain](cref:Meziantou.Framework.PublicSuffixList.GetRegistrableDomain) returns the part of a host name that a registrant controls, so two hosts with
the same registrable domain belong to the same site:

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

See [Public Suffix List](07-PublicSuffixList.md).

## Next steps

- [Matching URLs](03-MatchingUrls.md): create patterns, match strings, `Uri` values and URL components, read the
  result.
- [Pattern collections](05-PatternCollections.md): find which pattern of a list matches a URL.
- [Limitations](08-Limitations.md): what the library does not do.
