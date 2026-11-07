# Meziantou.Framework.Uri

`Meziantou.Framework.Uri` provides methods to manipulate URIs.

## URL Pattern API

Implementation of the [WHATWG URL Pattern API](https://urlpattern.spec.whatwg.org/) for matching URLs against patterns.

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Create a pattern from a URL pattern init
var pattern = UrlPattern.Create(new UrlPatternInit
{
    Protocol = "https",
    Hostname = "example.com",
    Pathname = "/books/:id",
});

// Test if a URL matches the pattern
Assert.True(pattern.IsMatch("https://example.com/books/123"));

// Create a pattern from a pattern string
var pattern2 = UrlPattern.Create("https://example.com/api/:version/*");
Assert.True(pattern2.IsMatch("https://example.com/api/v1/users"));

// Use wildcards
var wildcardPattern = UrlPattern.Create(new UrlPatternInit
{
    Pathname = "/files/*",
});
Assert.True(wildcardPattern.IsMatch("https://example.com/files/a/b/c"));

// Use modifiers (optional, one-or-more, zero-or-more)
var optionalPattern = UrlPattern.Create(new UrlPatternInit
{
    Pathname = "/items{/:category}?",
});
Assert.True(optionalPattern.IsMatch("https://example.com/items"));
Assert.True(optionalPattern.IsMatch("https://example.com/items/books"));

// Case-insensitive matching
var caseInsensitivePattern = UrlPattern.Create(
    new UrlPatternInit { Pathname = "/Books/:id" },
    new UrlPatternOptions { IgnoreCase = true });
Assert.True(caseInsensitivePattern.IsMatch("https://example.com/BOOKS/123"));

// Execute pattern and capture groups
var pattern3 = UrlPattern.Create(new UrlPatternInit
{
    Pathname = "/users/:userId/posts/:postId",
});
UrlPatternResult? result = pattern3.Match("https://example.com/users/42/posts/99");
Assert.NotNull(result);
Assert.Equal("42", result.Pathname.Groups["userId"]);
Assert.Equal("99", result.Pathname.Groups["postId"]);
```
<!-- endref -->

### UrlPatternCollection

Match URLs against multiple patterns:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternCollectionTests.cs;region=UrlPatternCollection_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var docsPattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/docs/*" });
var collection = new UrlPatternCollection
{
    UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
    docsPattern,
};

// Test if any pattern matches
Assert.True(collection.IsMatch("https://example.com/api/users"));

// Get the first matching pattern
UrlPattern? match = collection.FindPattern("https://example.com/docs/guide");
Assert.Same(docsPattern, match);

// Match and capture groups from the first matching pattern
var apiCollection = new UrlPatternCollection
{
    UrlPattern.Create(new UrlPatternInit { Pathname = "/api/:version/*" }),
};
UrlPatternResult? apiResult = apiCollection.Match("https://example.com/api/v2/users");
Assert.NotNull(apiResult);
Assert.Equal("v2", apiResult.Pathname.Groups["version"]);
```
<!-- endref -->

## Query String Utilities

Parse and edit query strings

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

## Public Suffix List

A snapshot of the [Public Suffix List](https://publicsuffix.org/list/) is compiled into the assembly and used to find the public suffix (eTLD) and the registrable domain (eTLD+1) of a domain name. No network access is performed; the list is refreshed when a new version of the package is released.

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

The list has two sections: the suffixes delegated by ICANN, and the suffixes submitted by domain holders such as `blogspot.com`. Both are used by default; the matching can be restricted to one of them.

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/PublicSuffixListTests.cs;region=PublicSuffixList_RuleSources;format=md-fenced-code-block;dedent=true -->
```csharp
// blogspot.com is a private rule
Assert.Equal("foo.blogspot.com", PublicSuffixList.GetRegistrableDomain("foo.blogspot.com"));
Assert.Equal("blogspot.com", PublicSuffixList.GetRegistrableDomain("foo.blogspot.com", PublicSuffixRuleSources.Icann));
```
<!-- endref -->

When a domain matches no rule of the list, the implicit `*` rule applies and the top-level domain is the public suffix. `DomainInfo.IsKnownPublicSuffix` reports whether an actual rule matched.
