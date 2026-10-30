---
title: Pattern syntax
description: Write URL patterns with named groups, wildcards, regular expressions, modifiers, braces and escapes.
---

A pattern is fixed text mixed with groups. The fixed text must appear as is in the URL; each group matches a part of
the URL and captures it. The syntax is the same in every component, but the pathname and the hostname split their
values into segments.

| Syntax | Meaning |
| --- | --- |
| `:name` | A named group: one segment of the component. |
| `*` | A wildcard: any text, segment separators included. |
| `(regex)` | A regular expression, alone or after a name (`:id(\\d+)`). |
| `?`, `+`, `*` after a group | The group is optional, repeated one or more times, or repeated zero or more times. |
| `{...}` | A group of fixed text and groups, to apply a modifier to all of it. |
| `\` | Makes the next character literal. |

The examples on this page write patterns in C# strings, where a backslash is itself escaped: the pattern `\d+` is the
C# string `"\\d+"`.

## Named groups

`:name` captures a segment: in the pathname, the text up to the next `/`; in the hostname, the text up to the next
`.`. In the other components, it captures any text. A named group matches at least one character, and fixed text can
follow it in the same segment:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_NamedGroups;format=md-fenced-code-block;dedent=true -->
```csharp
var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id" });

// A named group matches one path segment
Assert.True(pattern.IsMatch("https://example.com/books/123"));
Assert.False(pattern.IsMatch("https://example.com/books/123/reviews"));
Assert.False(pattern.IsMatch("https://example.com/books/"));

// Fixed text can follow a group in the same segment
var formatPattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id.:format" });
var result = formatPattern.Match("https://example.com/books/123.json");
Assert.NotNull(result);
Assert.Equal("123", result.Pathname.Groups["id"]);
Assert.Equal("json", result.Pathname.Groups["format"]);
```
<!-- endref -->

Two groups of the same component cannot have the same name: `UrlPattern.Create` throws a `UrlPatternException`.

## Wildcards

`*` matches any text, including `/` in the pathname and `.` in the hostname. Groups without a name are numbered in
the order they appear in the component, from `"0"`:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_Wildcards;format=md-fenced-code-block;dedent=true -->
```csharp
var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/files/*" });

// A wildcard matches any text, "/" included. Its group is named by its index.
var result = pattern.Match("https://example.com/files/2024/report.pdf");
Assert.NotNull(result);
Assert.Equal("2024/report.pdf", result.Pathname.Groups["0"]);

// A wildcard in the hostname
var subdomains = UrlPattern.Create("https://*.example.com/*");
Assert.True(subdomains.IsMatch("https://api.example.com/v1/users"));
Assert.False(subdomains.IsMatch("https://example.org/v1/users"));
```
<!-- endref -->

## Regular expressions

Parentheses hold a regular expression. After a name, it replaces what the named group matches; alone, it is a group
numbered like a wildcard. `HasRegExpGroups` tells whether a pattern contains one:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_RegexGroups;format=md-fenced-code-block;dedent=true -->
```csharp
// A regular expression constrains what a group matches
var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id(\\d+)" });
Assert.True(pattern.IsMatch("https://example.com/books/123"));
Assert.False(pattern.IsMatch("https://example.com/books/abc"));
Assert.True(pattern.HasRegExpGroups);

// A regular expression without a name is named by its index
var versionPattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/api/(v1|v2)/*" });
var result = versionPattern.Match("https://example.com/api/v2/users");
Assert.NotNull(result);
Assert.Equal("v2", result.Pathname.Groups["0"]);
Assert.Equal("users", result.Pathname.Groups["1"]);

// A group inside a regular expression must be non-capturing
var imagePattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/images/:name([^/]+\\.(?:png|jpg))" });
Assert.True(imagePattern.IsMatch("https://example.com/images/logo.png"));
Assert.False(imagePattern.IsMatch("https://example.com/images/logo.gif"));
Assert.Throws<UrlPatternException>(() => UrlPattern.Create(new UrlPatternInit { Pathname = "/images/:name([^/]+\\.(png|jpg))" }));
```
<!-- endref -->

A regular expression is written in ASCII, cannot be empty, and cannot capture: a group inside it starts with `(?:`.
It runs with the .NET regular expression engine. See [Limitations](08-Limitations.md) before you accept patterns from
users.

## Modifiers

A modifier after a group makes it optional (`?`), repeats it one or more times (`+`), or zero or more times (`*`). In
the pathname, the `/` before the group is part of it, so `/books/:id?` matches `/books` as well as `/books/123`:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_Modifiers;format=md-fenced-code-block;dedent=true -->
```csharp
// "?" makes the group optional, with the "/" before it
var optional = UrlPattern.Create(new UrlPatternInit { Pathname = "/books/:id?" });
Assert.True(optional.IsMatch("https://example.com/books"));
Assert.True(optional.IsMatch("https://example.com/books/123"));
Assert.Null(optional.Match("https://example.com/books")!.Pathname.Groups["id"]);

// "+" repeats the group one or more times
var oneOrMore = UrlPattern.Create(new UrlPatternInit { Pathname = "/files/:path+" });
Assert.False(oneOrMore.IsMatch("https://example.com/files"));
Assert.Equal("a/b/c", oneOrMore.Match("https://example.com/files/a/b/c")!.Pathname.Groups["path"]);

// "*" repeats the group zero or more times
var zeroOrMore = UrlPattern.Create(new UrlPatternInit { Pathname = "/files/:path*" });
Assert.True(zeroOrMore.IsMatch("https://example.com/files"));
Assert.True(zeroOrMore.IsMatch("https://example.com/files/a/b/c"));
```
<!-- endref -->

The value of a repeated group is the whole text it matched, separators included (`a/b/c`). The value of an optional
group that matched nothing is `null`.

## Braces

Braces group fixed text and groups so that a modifier applies to all of them. They capture nothing themselves:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_Braces;format=md-fenced-code-block;dedent=true -->
```csharp
// Braces group text so that a modifier applies to all of it
var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/book{s}?/:id" });
Assert.True(pattern.IsMatch("https://example.com/book/123"));
Assert.True(pattern.IsMatch("https://example.com/books/123"));

var withCategory = UrlPattern.Create(new UrlPatternInit { Pathname = "/products{/:category}?/list" });
Assert.True(withCategory.IsMatch("https://example.com/products/list"));
Assert.Equal("toys", withCategory.Match("https://example.com/products/toys/list")!.Pathname.Groups["category"]);
```
<!-- endref -->

## Escaping special characters

A backslash makes the next character fixed text, for instance to match a `*`, a `:`, a `?` or a parenthesis:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_Escaping;format=md-fenced-code-block;dedent=true -->
```csharp
// A backslash makes a special character literal
var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/search/\\*" });
Assert.True(pattern.IsMatch("https://example.com/search/*"));
Assert.False(pattern.IsMatch("https://example.com/search/books"));
```
<!-- endref -->

## How fixed text is compared

A URL is normalized before it is matched: special characters of the path are percent-encoded, the host name is in
lower case, and the default port of the protocol is removed. The fixed text of a pattern gets the same treatment, so
you write it the way it reads, and it matches the URL in any of its equivalent spellings:

<!-- ref:../../tests/Meziantou.Framework.Uri.Tests/UrlPatternTests.cs;region=UrlPattern_Canonicalization;format=md-fenced-code-block;dedent=true -->
```csharp
// The fixed text of a pattern is canonicalized like the URLs it is matched against
var pattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/café" });
Assert.Equal("/caf%C3%A9", pattern.Pathname);
Assert.True(pattern.IsMatch("https://example.com/café"));
Assert.True(pattern.IsMatch("https://example.com/caf%C3%A9"));

// A group captures the normalized text
var namePattern = UrlPattern.Create(new UrlPatternInit { Pathname = "/:name" });
Assert.Equal("caf%C3%A9", namePattern.Match("https://example.com/café")!.Pathname.Groups["name"]);

// The default port of the protocol is the same as no port
var portPattern = UrlPattern.Create("https://example.com:443/*");
Assert.Equal("", portPattern.Port);
Assert.True(portPattern.IsMatch("https://example.com/books"));
```
<!-- endref -->

- The pattern properties (`Pathname`, `Port`…) return the normalized form.
- A host name written with non-ASCII characters is converted to its ASCII form (`xn--…`). See
  [Limitations](08-Limitations.md).
- The text that a group captures is in the normalized form of the URL: a group that matches `café` in a path
  captures `caf%C3%A9`.

## Next steps

- [Matching URLs](03-MatchingUrls.md): match strings, `Uri` values and components, and read the result.
- [Pattern collections](05-PatternCollections.md): match a URL against several patterns.
