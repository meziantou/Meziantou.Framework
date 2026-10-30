---
title: Pattern collections
description: Match a URL against a list of patterns, find the pattern that matches and read its groups.
---

A `UrlPatternCollection` holds a list of patterns and matches a URL against each of them in turn, for instance to find
the route of a request or to apply a list of allowed URLs.

## Match a URL against several patterns

Add patterns as `UrlPattern` instances, pattern strings or `UrlPatternInit` values. `IsMatch` tells whether any
pattern matches, `FindPattern` returns the first pattern that matches, and `Match` returns the result of the first
pattern that matches:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternCollectionTests.cs;region=UrlPatternCollection_Example;format=md-fenced-code-block;dedent=true -->
```csharp
// Create a collection of patterns for routing
var patterns = new UrlPatternCollection();
patterns.Add("https://example.com/api/users/:id");
patterns.Add("https://example.com/api/products/:id");
patterns.Add("https://example.com/static/*");

// Check if any pattern matches
Assert.True(patterns.IsMatch("https://example.com/api/users/123"));

// Find the first matching pattern
var matchingPattern = patterns.FindPattern("https://example.com/api/products/456");
Assert.NotNull(matchingPattern);
Assert.Equal("/api/products/:id", matchingPattern.Pathname);

// Get match result with captured groups
var result = patterns.Match("https://example.com/api/users/789");
Assert.NotNull(result);
Assert.Equal("789", result.Pathname.Groups["id"]);
```
<!-- endref -->

The collection can also be built with a collection initializer, or from an `IEnumerable<UrlPattern>` with the
constructor:

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

Every overload of `UrlPattern` exists on the collection: a string, a relative string with its base URL, a `Uri` or a
`UrlPatternInit`. `FindPattern` and `Match` return `null` when no pattern matches.

## Order the patterns

The patterns are tried in the order they were added, and the first one that matches wins. Add the specific patterns
before the general ones:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternCollectionTests.cs;region=UrlPatternCollection_FirstMatchWins;format=md-fenced-code-block;dedent=true -->
```csharp
// Patterns are tried in the order they were added: put the specific ones first
var routes = new UrlPatternCollection();
var newBook = routes.Add("https://example.com/books/new");
var bookById = routes.Add("https://example.com/books/:id");

Assert.Same(newBook, routes.FindPattern("https://example.com/books/new"));
Assert.Same(bookById, routes.FindPattern("https://example.com/books/123"));
Assert.Null(routes.FindPattern("https://example.com/authors/123"));
```
<!-- endref -->

The `Add` overloads that take a string or a `UrlPatternInit` return the pattern they create, so you can compare it with
the result of `FindPattern` to know which one matched.

## Edit the collection

`UrlPatternCollection` implements `IReadOnlyList<UrlPattern>` (`Count`, the indexer and enumeration) and has `Add`,
`Remove`, `Contains` and `Clear`.
Matching tries every pattern until one matches, so its cost grows with the number of patterns.

The collection is not synchronized: build it first, then match from as many threads as you need, but do not add or
remove patterns while other threads use it.

## Next steps

- [Pattern syntax](04-PatternSyntax.md): named groups, wildcards, regular expressions and modifiers.
- [Matching URLs](03-MatchingUrls.md): read the result of a match.
