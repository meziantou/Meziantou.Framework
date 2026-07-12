# Meziantou.Framework.RobotsTxt

`Meziantou.Framework.RobotsTxt` parses `robots.txt` files (RFC 9309), including Google-style wildcard extensions (`*` and trailing `$`).

It supports:

- `User-agent`, `Allow`, `Disallow`, `Crawl-delay`, and `Sitemap`
- user-agent resolution (exact match preferred over `*`)
- path authorization checks
- lenient parsing with non-fatal parse errors

```csharp
using Meziantou.Framework.RobotsTxt;

var robots = RobotsFile.Parse("""
User-agent: *
Disallow: /private/
Allow: /private/public/
Crawl-delay: 1.5

Sitemap: https://example.com/sitemap.xml
""");

var allowed = robots.IsAllowed("Googlebot", "/private/public/page"); // true
var blocked = robots.IsAllowed("Googlebot", "/private/secret"); // false
var delay = robots.GetCrawlDelay("Googlebot"); // 00:00:01.5000000
var sitemaps = robots.Sitemaps;
```

Path rules support wildcards:

```csharp
var robots = RobotsFile.Parse("""
User-agent: *
Disallow: /*.php$
Allow: /index.php
""");

robots.IsAllowed("AnyBot", "/index.php"); // true
robots.IsAllowed("AnyBot", "/admin.php"); // false
```

Invalid or unsupported lines do not throw. They are reported in `ParseErrors`:

```csharp
var robots = RobotsFile.Parse("""
User-agent: *
Host: example.com
this line has no colon
""");

foreach (var error in robots.ParseErrors)
{
    Console.WriteLine($"{error.LineNumber}: {error.Kind} => {error.Line}");
}
```
