---
title: Matching URLs
description: Create a URL pattern from a string or from components, match URLs, Uri values and components, and read the groups.
---

A [UrlPattern](cref:Meziantou.Framework.UrlPattern) holds one pattern per component of a URL: protocol, username, password, hostname, port, pathname,
search and hash. A URL matches when every component matches its pattern.

## Create a pattern from a string

Write the pattern as a URL. The components the string leaves out match anything:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_CreateFromString;format=md-fenced-code-block;dedent=true -->
```csharp
var pattern = UrlPattern.Create("https://example.com/books/:id");

// Each component of the URL gets its own pattern
Assert.Equal("https", pattern.Protocol);
Assert.Equal("example.com", pattern.Hostname);
Assert.Equal("/books/:id", pattern.Pathname);

// A component the string does not mention matches anything
Assert.Equal("*", pattern.Search);
Assert.True(pattern.IsMatch("https://example.com/books/123?format=json#reviews"));

// A relative pattern needs a base URL, which supplies the missing components
var relativePattern = UrlPattern.Create("/books/:id", "https://example.com");
Assert.Equal("example.com", relativePattern.Hostname);
Assert.True(relativePattern.IsMatch("https://example.com/books/123"));
```
<!-- endref -->

- The [Protocol](cref:Meziantou.Framework.UrlPattern.Protocol), [Username](cref:Meziantou.Framework.UrlPattern.Username), [Password](cref:Meziantou.Framework.UrlPattern.Password), [Hostname](cref:Meziantou.Framework.UrlPattern.Hostname), [Port](cref:Meziantou.Framework.UrlPattern.Port), [Pathname](cref:Meziantou.Framework.UrlPattern.Pathname), [Search](cref:Meziantou.Framework.UrlPattern.Search) and [Hash](cref:Meziantou.Framework.UrlPattern.Hash) properties return the
  normalized pattern of each component.
- A pattern that has no protocol, such as `/books/:id`, needs a base URL: [UrlPattern.Create](cref:Meziantou.Framework.UrlPattern.Create) throws a
  [UrlPatternException](cref:Meziantou.Framework.UrlPatternException) without one.
- An origin alone matches every URL of that origin: `https://example.com` matches `https://example.com/anything`. With
  a trailing slash, `https://example.com/` only matches the root path.

## Create a pattern from components

A [UrlPatternInit](cref:Meziantou.Framework.UrlPatternInit) sets the pattern of each component separately. The components you do not set default to `*`, so
the pattern below matches the path on any host:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_CreateFromInit;format=md-fenced-code-block;dedent=true -->
```csharp
// Only the pathname is constrained: every other component defaults to "*"
var pattern = UrlPattern.Create(new UrlPatternInit
{
    Pathname = "/books/:id",
});

Assert.True(pattern.IsMatch("https://example.com/books/123"));
Assert.True(pattern.IsMatch("http://localhost:5000/books/123"));
Assert.False(pattern.IsMatch("https://example.com/authors/123"));
```
<!-- endref -->

The delimiters are optional: the `:` after the protocol and the `?` and `#` before the search and the hash are removed,
so `Search = "?page=1"` and `Search = "page=1"` are the same pattern. Set
[BaseUrl](cref:Meziantou.Framework.UrlPatternInit.BaseUrl) to take the components that a relative pattern does not give from a URL; the text of the base URL is
literal, so a `:` or a `*` in it is not pattern syntax.

The two forms are equivalent: prefer the string for a pattern that reads like a URL, and [UrlPatternInit](cref:Meziantou.Framework.UrlPatternInit) when only a
few components matter, or when a component comes from configuration.

## Match a URL

[IsMatch](cref:Meziantou.Framework.UrlPattern.IsMatch) tells whether a URL matches. It accepts a string, a relative URL with its base URL, a `Uri`, or the
components of a URL in a [UrlPatternInit](cref:Meziantou.Framework.UrlPatternInit):

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_MatchInputs;format=md-fenced-code-block;dedent=true -->
```csharp
var pattern = UrlPattern.Create("https://example.com/books/:id");

// A string
Assert.True(pattern.IsMatch("https://example.com/books/123"));

// A relative URL and the base URL to resolve it against
Assert.True(pattern.IsMatch("/books/123", "https://example.com"));

// A System.Uri. A relative Uri never matches.
Assert.True(pattern.IsMatch(new System.Uri("https://example.com/books/123")));
Assert.False(pattern.IsMatch(new System.Uri("/books/123", UriKind.Relative)));

// The components of a URL, given separately
Assert.True(pattern.IsMatch(new UrlPatternInit
{
    Protocol = "https",
    Hostname = "example.com",
    Pathname = "/books/123",
}));

// A string that is not a URL does not match, it does not throw
Assert.False(pattern.IsMatch("not a url"));
```
<!-- endref -->

A string that cannot be parsed as a URL does not match: [IsMatch](cref:Meziantou.Framework.UrlPattern.IsMatch) returns `false` and [Match](cref:Meziantou.Framework.UrlPattern.Match) returns `null`, so you can
pass untrusted input without catching exceptions. A relative `Uri` never matches; resolve it first, or pass the
relative string with its base URL.

## Read the values of the groups

[Match](cref:Meziantou.Framework.UrlPattern.Match) returns a [UrlPatternResult](cref:Meziantou.Framework.UrlPatternResult), or `null` when the URL does not match. The result has one
[UrlPatternComponentResult](cref:Meziantou.Framework.UrlPatternComponentResult) per component, with the value that was matched ([Input](cref:Meziantou.Framework.UrlPatternComponentResult.Input)) and the value of each group
([Groups](cref:Meziantou.Framework.UrlPatternComponentResult.Groups)):

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_MatchResult;format=md-fenced-code-block;dedent=true -->
```csharp
var pattern = UrlPattern.Create("https://:tenant.example.com/orders/:orderId");

var result = pattern.Match("https://contoso.example.com/orders/42?page=2");
Assert.NotNull(result);

// One result per component: the value it was matched against and its groups
Assert.Equal("contoso", result.Hostname.Groups["tenant"]);
Assert.Equal("42", result.Pathname.Groups["orderId"]);
Assert.Equal("/orders/42", result.Pathname.Input);
Assert.Equal("page=2", result.Search.Input);

// The URL that was matched, as given
Assert.Equal("https://contoso.example.com/orders/42?page=2", result.Inputs[0].Url);

// No match: Match returns null
Assert.Null(pattern.Match("https://contoso.example.com/invoices/42"));
```
<!-- endref -->

- [Groups](cref:Meziantou.Framework.UrlPatternComponentResult.Groups) is keyed by the name of the group. A group without a name, such as a wildcard, is keyed by its index:
  `"0"`, `"1"`… See [Pattern syntax](04-PatternSyntax.md).
- The value of an optional group that matched nothing is `null`.
- [Input](cref:Meziantou.Framework.UrlPatternComponentResult.Input) is the component of the URL as it was compared, without its delimiter: `page=2`, not `?page=2`.
- [Inputs](cref:Meziantou.Framework.UrlPatternResult.Inputs) holds what you passed to [Match](cref:Meziantou.Framework.UrlPattern.Match): [Url](cref:Meziantou.Framework.UrlPatternInput.Url) for a string or a `Uri`, [Init](cref:Meziantou.Framework.UrlPatternInput.Init) for a [UrlPatternInit](cref:Meziantou.Framework.UrlPatternInit).

## Ignore the case

By default, the pathname, the search and the hash are case-sensitive. Set [UrlPatternOptions.IgnoreCase](cref:Meziantou.Framework.UrlPatternOptions.IgnoreCase) to compare
them case-insensitively:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_IgnoreCase;format=md-fenced-code-block;dedent=true -->
```csharp
var caseSensitive = UrlPattern.Create(new UrlPatternInit { Pathname = "/Books/:id" });
Assert.False(caseSensitive.IsMatch("https://example.com/books/123"));

// IgnoreCase applies to the pathname, the search and the hash
var options = new UrlPatternOptions { IgnoreCase = true };
var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/Books/:id", Search = "lang=en" }, options);
Assert.True(pattern.IsMatch("https://example.com/BOOKS/123?LANG=EN"));

// The protocol and the hostname are always compared in lower case
var hostPattern = UrlPattern.Create("https://example.com/*");
Assert.True(hostPattern.IsMatch("HTTPS://EXAMPLE.COM/books"));
```
<!-- endref -->

The option does not apply to the other components. The protocol and the hostname are compared in lower case whatever
the option; the username, the password and the port are always case-sensitive.

## Handle invalid patterns

[UrlPattern.Create](cref:Meziantou.Framework.UrlPattern.Create) throws a [UrlPatternException](cref:Meziantou.Framework.UrlPatternException) when the pattern cannot be parsed: an unclosed group, an invalid
regular expression, a protocol or a hostname that is not valid, a port out of range, or a pattern string without a
protocol and without a base URL:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_InvalidPattern;format=md-fenced-code-block;dedent=true -->
```csharp
// An unclosed regular expression group
Assert.Throws<UrlPatternException>(() => UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id(" }));

// A pattern string without a protocol, and no base URL
Assert.Throws<UrlPatternException>(() => UrlPattern.Create("/books/:id"));
```
<!-- endref -->

Create patterns once, at startup or when the configuration loads, and reuse them: parsing a pattern builds its regular
expressions, while matching only runs them.

## Next steps

- [Pattern syntax](04-PatternSyntax.md): named groups, wildcards, regular expressions and modifiers.
- [Pattern collections](05-PatternCollections.md): match a URL against several patterns.
