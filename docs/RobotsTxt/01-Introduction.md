---
title: Introduction
description: Parse robots.txt files following RFC 9309 and check whether a crawler may fetch a path.
---

`Meziantou.Framework.RobotsTxt` parses `robots.txt` files as described by
[RFC 9309](https://www.rfc-editor.org/rfc/rfc9309.html), including `*` wildcards and `$` end anchors in paths. It
answers the questions a crawler asks before fetching a page: may this user agent fetch this
path, how long should it wait between requests, and where are the sitemaps.

## What you can do with it

- **Check a path** for a user agent with `IsAllowed`, from a path string or a `Uri`. See
  [Getting started](02-GettingStarted.md).
- **Find the group** of directives that applies to a crawler: an exact user-agent match wins over the `*` group, and
  groups that name the same agent are merged. See [User-agent groups](03-UserAgentGroups.md).
- **Match paths** with `Allow` and `Disallow` rules, `*` wildcards and `$` anchors: the longest matching rule wins.
  See [Path rules](04-PathRules.md).
- **Read the crawl delay and the sitemaps** declared by the file.
- **Audit a file**: invalid lines never throw; they are reported with their line number. See
  [Parse errors](05-ParseErrors.md).

## Supported directives

| Directive | Scope | Meaning |
| --- | --- | --- |
| `User-agent` | Group | Starts a group, or adds an agent to the group being read. |
| `Allow` | Group | The paths that match may be fetched. |
| `Disallow` | Group | The paths that match may not be fetched. An empty value allows everything. |
| `Crawl-delay` | Group | The number of seconds to wait between requests, decimals allowed. |
| `Sitemap` | File | The URL of a sitemap, wherever it appears in the file. |

Directive names are case-insensitive, and `#` starts a comment that runs to the end of the line. Any other directive
is reported as a parse error and skipped.

## Namespace

Every type is in the [Meziantou.Framework.RobotsTxt](cref:Meziantou.Framework.RobotsTxt) namespace: `RobotsFile`, `RobotsGroup`, `RobotsRule`,
`RobotsRuleKind`, `RobotsParseError` and `RobotsParseErrorKind`.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and check your first path.
