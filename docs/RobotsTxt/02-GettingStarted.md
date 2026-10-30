---
title: Getting started
description: Install the package, parse a robots.txt file and check whether a crawler may fetch a path.
---

## Install the package

Add the `Meziantou.Framework.RobotsTxt` package to a project:

```bash
dotnet add package Meziantou.Framework.RobotsTxt
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.RobotsTxt;`.

## Parse a file and check a path

[RobotsFile.Parse](cref:Meziantou.Framework.RobotsTxt.RobotsFile.Parse) reads the content of a `robots.txt` file. [IsAllowed](cref:Meziantou.Framework.RobotsTxt.RobotsFile.IsAllowed) then tells whether a user agent may fetch a
path, [GetCrawlDelay](cref:Meziantou.Framework.RobotsTxt.RobotsFile.GetCrawlDelay) returns the delay that applies to it, and [Sitemaps](cref:Meziantou.Framework.RobotsTxt.RobotsFile.Sitemaps) lists the sitemap URLs of the file:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_Parse;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Disallow: /private/
    Allow: /private/public/
    Crawl-delay: 1.5

    Sitemap: https://example.com/sitemap.xml
    """);

Assert.True(robots.IsAllowed("Googlebot", "/private/public/page"));
Assert.False(robots.IsAllowed("Googlebot", "/private/secret"));
Assert.Equal(TimeSpan.FromSeconds(1.5), robots.GetCrawlDelay("Googlebot"));
Assert.Equal(["https://example.com/sitemap.xml"], robots.Sitemaps);
```
<!-- endref -->

- The user agent is the product token of your crawler, such as `Googlebot`, not the full `User-Agent` header. See
  [User-agent groups](03-UserAgentGroups.md).
- The path includes the query string, if any. [IsAllowed](cref:Meziantou.Framework.RobotsTxt.RobotsFile.IsAllowed) also accepts a `Uri`, and then checks its path and query.
- [GetCrawlDelay](cref:Meziantou.Framework.RobotsTxt.RobotsFile.GetCrawlDelay) returns `null` when the group has no `Crawl-delay` directive. The library does not wait for you:
  your crawler applies the delay.

## Parse from a stream

[RobotsFile.ParseAsync](cref:Meziantou.Framework.RobotsTxt.RobotsFile.ParseAsync) reads a `Stream` or a `TextReader`, such as the body of the response to a request for
`/robots.txt`:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_ParseStream;format=md-fenced-code-block;dedent=true -->
```csharp
using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""
    User-agent: *
    Disallow: /admin/
    """));

var robots = await RobotsFile.ParseAsync(stream);

Assert.False(robots.IsAllowed("MyCrawler", "/admin/users"));
```
<!-- endref -->

A stream is read as UTF-8 unless you pass another `Encoding`; a byte order mark selects the encoding it denotes. The
stream is left open. Lines end at `\r`, `\n` or `\r\n`.

The library parses the content only. Downloading the file, and deciding what to do when the server answers with an
error or does not answer at all, is up to you; RFC 9309 describes what a crawler should do in each case.

## Next steps

- [User-agent groups](03-UserAgentGroups.md): which group of directives applies to your crawler.
- [Path rules](04-PathRules.md): how `Allow` and `Disallow` rules match a path.
- [Parse errors](05-ParseErrors.md): find the lines the parser skipped.
