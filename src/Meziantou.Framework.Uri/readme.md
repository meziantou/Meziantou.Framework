# Meziantou.Framework.Uri

`Meziantou.Framework.Uri` provides methods to manipulate URIs.

## URL Pattern API

Implementation of the [WHATWG URL Pattern API](https://urlpattern.spec.whatwg.org/) for matching URLs against patterns.

```c#
// Create a pattern from a URL pattern init
var pattern = UrlPattern.Create(new UrlPatternInit
{
    Protocol = "https",
    Hostname = "example.com",
    Pathname = "/books/:id",
});

// Test if a URL matches the pattern
bool matches = pattern.IsMatch("https://example.com/books/123"); // true

// Create a pattern from a pattern string
var pattern2 = UrlPattern.Create("https://example.com/api/:version/*");

// Use wildcards
var wildcardPattern = UrlPattern.Create(new UrlPatternInit
{
    Pathname = "/files/*",
});
wildcardPattern.IsMatch("https://example.com/files/a/b/c"); // true

// Use modifiers (optional, one-or-more, zero-or-more)
var optionalPattern = UrlPattern.Create(new UrlPatternInit
{
    Pathname = "/items{/:category}?",
});
optionalPattern.IsMatch("https://example.com/items"); // true
optionalPattern.IsMatch("https://example.com/items/books"); // true

// Case-insensitive matching
var caseInsensitivePattern = UrlPattern.Create(
    new UrlPatternInit { Pathname = "/Books/:id" },
    new UrlPatternOptions { IgnoreCase = true });
caseInsensitivePattern.IsMatch("https://example.com/BOOKS/123"); // true

// Execute pattern and capture groups
var pattern3 = UrlPattern.Create(new UrlPatternInit
{
    Pathname = "/users/:userId/posts/:postId",
});
UrlPatternResult? result = pattern3.Match("https://example.com/users/42/posts/99");
if (result != null)
{
    string? userId = result.Pathname.Groups["userId"]; // "42"
    string? postId = result.Pathname.Groups["postId"]; // "99"
}
```

### UrlPatternCollection

Match URLs against multiple patterns:

```c#
var collection = new UrlPatternCollection
{
    UrlPattern.Create(new UrlPatternInit { Pathname = "/api/*" }),
    UrlPattern.Create(new UrlPatternInit { Pathname = "/docs/*" }),
};

// Test if any pattern matches
bool anyMatch = collection.IsMatch("https://example.com/api/users"); // true

// Get the first matching pattern
UrlPattern? match = collection.FindPattern("https://example.com/docs/guide"); // returns the /docs/* pattern

// Match and capture groups from the first matching pattern
var apiCollection = new UrlPatternCollection
{
    UrlPattern.Create(new UrlPatternInit { Pathname = "/api/:version/*" }),
};
UrlPatternResult? apiResult = apiCollection.Match("https://example.com/api/v2/users");
if (apiResult != null)
{
    string? version = apiResult.Pathname.Groups["version"]; // "v2"
}
```

## Query String Utilities

Parse and edit query strings

````c#
var uri = "https://www.meziantou.net";
var query = QueryStringUtilities.ParseQueryString(uri);
var author = query["author"];

_ = QueryStringUtilities.AddQueryString(uri, "name", "value");
_ = QueryStringUtilities.AddOrReplaceQueryString(uri, "name", "value");
_ = QueryStringUtilities.SetQueryString(uri, "name", "value");

// Remove a query string parameter
_ = QueryStringUtilities.AddQueryString(uri, "name", null);
````

## Public Suffix List

A snapshot of the [Public Suffix List](https://publicsuffix.org/list/) is compiled into the assembly and used to find the public suffix (eTLD) and the registrable domain (eTLD+1) of a domain name. No network access is performed; the list is refreshed when a new version of the package is released.

````c#
// Get the registrable domain (eTLD+1)
_ = PublicSuffixList.GetRegistrableDomain("www.example.co.uk"); // example.co.uk
_ = PublicSuffixList.GetRegistrableDomain("co.uk");             // null

// Get the public suffix (eTLD)
_ = PublicSuffixList.GetPublicSuffix("www.example.co.uk"); // co.uk

// Test if a domain is a public suffix
_ = PublicSuffixList.IsPublicSuffix("co.uk");       // true
_ = PublicSuffixList.IsPublicSuffix("example.com"); // false

// Decompose a domain name
if (PublicSuffixList.TryGetDomainInfo("www.a.example.co.uk", out DomainInfo info))
{
    _ = info.PublicSuffix;         // co.uk
    _ = info.RegistrableDomain;    // example.co.uk
    _ = info.Subdomain;            // www.a
    _ = info.Source;               // PublicSuffixRuleSources.Icann
    _ = info.IsKnownPublicSuffix;  // true
}

// A Uri can be used directly
_ = PublicSuffixList.TryGetDomainInfo(new Uri("https://www.example.co.uk/path"), out _);
````

The list has two sections: the suffixes delegated by ICANN, and the suffixes submitted by domain holders such as `blogspot.com`. Both are used by default; the matching can be restricted to one of them.

````c#
// blogspot.com is a private rule
_ = PublicSuffixList.GetRegistrableDomain("foo.blogspot.com");                                   // foo.blogspot.com
_ = PublicSuffixList.GetRegistrableDomain("foo.blogspot.com", PublicSuffixRuleSources.Icann);    // blogspot.com
````

When a domain matches no rule of the list, the implicit `*` rule applies and the top-level domain is the public suffix. `DomainInfo.IsKnownPublicSuffix` reports whether an actual rule matched.
