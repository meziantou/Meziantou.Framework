---
title: Path rules
description: How Allow and Disallow rules match a path, with wildcards, end anchors and precedence.
---

Inside a group, `Allow` and `Disallow` rules decide which paths a crawler may fetch. A path that no rule matches is
allowed.

## The longest rule wins

When several rules match a path, the one with the longest pattern wins, wherever it is in the group. When an `Allow`
and a `Disallow` rule of the same length match, `Allow` wins:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsGroup_RulePrecedence;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Disallow: /docs/
    Allow: /docs/public/
    Allow: /shop
    Disallow: /shop
    """);

// The longest matching rule wins
Assert.False(robots.IsAllowed("MyCrawler", "/docs/internal"));
Assert.True(robots.IsAllowed("MyCrawler", "/docs/public/guide"));

// Allow wins over a Disallow of the same length
Assert.True(robots.IsAllowed("MyCrawler", "/shop/cart"));

// A path that no rule matches is allowed
Assert.True(robots.IsAllowed("MyCrawler", "/about"));
```
<!-- endref -->

The length is the length of the pattern as written in the file, wildcards included.

## Prefixes, case and query strings

A pattern without wildcards matches the paths that start with it:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_PathMatching;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Disallow: /search
    Disallow: /*?sessionid=
    """);

// A rule is a prefix of the path, compared case-sensitively
Assert.False(robots.IsAllowed("MyCrawler", "/search"));
Assert.False(robots.IsAllowed("MyCrawler", "/search-results"));
Assert.True(robots.IsAllowed("MyCrawler", "/Search"));

// The query string is part of what the rules match
Assert.False(robots.IsAllowed("MyCrawler", "/products?sessionid=42"));
Assert.False(robots.IsAllowed("MyCrawler", new Uri("https://example.com/products?sessionid=42")));
Assert.True(robots.IsAllowed("MyCrawler", new Uri("https://example.com/products")));
```
<!-- endref -->

- `Disallow: /search` blocks `/search-results` too. End the pattern with `/` or `$` to block less.
- The comparison is case-sensitive and character by character: the parser does not decode percent-encoded characters,
  so write the path as your crawler requests it.
- The query string is part of the path. With a `Uri`, [IsAllowed](cref:Meziantou.Framework.RobotsTxt.RobotsFile.IsAllowed) checks its path and query; with a relative `Uri`, it
  checks the original string.

## Wildcards and end anchors

`*` matches any sequence of characters, including none, and a `$` at the end of a pattern anchors it to the end of the
path:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_Wildcards;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Disallow: /*.php$
    Allow: /index.php
    """);

Assert.True(robots.IsAllowed("AnyBot", "/index.php"));
Assert.False(robots.IsAllowed("AnyBot", "/admin.php"));
```
<!-- endref -->

`/*.php$` blocks the paths that end with `.php`, and the longer `Allow: /index.php` lets one of them through. Only a
`$` at the end of a pattern is an anchor.

## Allow everything

An empty `Disallow` value allows everything:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsGroup_EmptyDisallow;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Disallow:
    """);

// An empty Disallow allows everything
Assert.True(robots.IsAllowed("MyCrawler", "/"));
Assert.True(robots.IsAllowed("MyCrawler", "/any/page"));
```
<!-- endref -->

To block everything, write `Disallow: /`.

## Read the rules

[RobotsGroup.Rules](cref:Meziantou.Framework.RobotsTxt.RobotsGroup.Rules) lists the rules of a group in file order. Each [RobotsRule](cref:Meziantou.Framework.RobotsTxt.RobotsRule) has a [Kind](cref:Meziantou.Framework.RobotsTxt.RobotsRule.Kind) (`Allow` or `Disallow`),
the pattern as written in [Value](cref:Meziantou.Framework.RobotsTxt.RobotsRule.Value), and a [Matches](cref:Meziantou.Framework.RobotsTxt.RobotsRule.Matches) method that tests a path against the pattern alone:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_InspectRules;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Disallow: /private/
    Allow: /private/public/
    """);

var rules = robots.Groups[0].Rules.Select(rule => $"{rule.Kind}: {rule.Value}");
Assert.Equal(["Disallow: /private/", "Allow: /private/public/"], rules);

var rule = robots.Groups[0].Rules[0];
Assert.True(rule.Matches("/private/data"));
```
<!-- endref -->

[RobotsGroup.IsAllowed](cref:Meziantou.Framework.RobotsTxt.RobotsGroup.IsAllowed) applies the precedence above to the rules of one group, and [RobotsFile.IsAllowed](cref:Meziantou.Framework.RobotsTxt.RobotsFile.IsAllowed) selects the
group of the user agent first.
