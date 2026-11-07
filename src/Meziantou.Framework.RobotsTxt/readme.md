# Meziantou.Framework.RobotsTxt

`Meziantou.Framework.RobotsTxt` parses `robots.txt` files (RFC 9309), including Google-style wildcard extensions (`*` and trailing `$`).

It supports:

- `User-agent`, `Allow`, `Disallow`, `Crawl-delay`, and `Sitemap`
- user-agent resolution (exact match preferred over `*`)
- path authorization checks
- lenient parsing with non-fatal parse errors

The types are in the `Meziantou.Framework.RobotsTxt` namespace.

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

Path rules support wildcards:

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

Invalid or unsupported lines do not throw. They are reported in `ParseErrors`:

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
