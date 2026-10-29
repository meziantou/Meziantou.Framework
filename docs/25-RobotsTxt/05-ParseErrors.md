---
title: Parse errors
description: Find the lines of a robots.txt file that the parser skipped, and why.
---

The parser is lenient: it never throws on the content of a file. A line it cannot use is skipped, and recorded in
`ParseErrors` so you can audit the file:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_ParseErrors;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Host: example.com
    this line has no colon
    """);

var errors = robots.ParseErrors.Select(error => $"{error.LineNumber}: {error.Kind} => {error.Line}");
Assert.Equal(["2: UnknownDirective => Host: example.com", "3: MalformedLine => this line has no colon"], errors);
```
<!-- endref -->

Each `RobotsParseError` has the 1-based `LineNumber`, the raw text of the `Line` (comment and whitespace included),
and the `Kind` of problem:

| Kind | Cause |
| --- | --- |
| `MalformedLine` | The line has no `:` between a directive and its value. |
| `UnknownDirective` | The directive is not `User-agent`, `Allow`, `Disallow`, `Crawl-delay` or `Sitemap`. |
| `InvalidCrawlDelay` | The value of `Crawl-delay` is not a non-negative number. |

`ParseErrors` is empty when every line is valid. Blank lines and comments are not errors.

## Invalid crawl delays

A `Crawl-delay` value is a number of seconds, with an optional decimal part (`1.5`). A value that is not a
non-negative number, or that is too large for a `TimeSpan`, is reported and ignored:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_InvalidCrawlDelay;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Crawl-delay: soon
    """);

Assert.Null(robots.GetCrawlDelay("MyCrawler"));
var error = Assert.Single(robots.ParseErrors);
Assert.Equal(RobotsParseErrorKind.InvalidCrawlDelay, error.Kind);
Assert.Equal(2, error.LineNumber);
```
<!-- endref -->

The line still belongs to the group, so a `User-agent` line after it starts a new group, as after a valid delay.
