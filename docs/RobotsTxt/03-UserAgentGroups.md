---
title: User-agent groups
description: Find the group of directives that applies to a crawler, and how the parser splits a file into groups.
---

A `robots.txt` file is a list of groups. Each group starts with one or more `User-agent` lines and holds the `Allow`,
`Disallow` and `Crawl-delay` directives that apply to those agents. [IsAllowed](cref:Meziantou.Framework.RobotsTxt.RobotsFile.IsAllowed) and [GetCrawlDelay](cref:Meziantou.Framework.RobotsTxt.RobotsFile.GetCrawlDelay) first select the
group of the user agent, then read it.

## Select the group of a crawler

[GetGroup](cref:Meziantou.Framework.RobotsTxt.RobotsFile.GetGroup) returns the group that applies to a user agent:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_UserAgentGroups;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: *
    Disallow: /

    User-agent: MyCrawler
    User-agent: OtherCrawler
    Disallow: /private/
    Crawl-delay: 2
    """);

// The exact match wins over the catch-all group, case-insensitively
Assert.True(robots.IsAllowed("mycrawler", "/products"));
Assert.False(robots.IsAllowed("mycrawler", "/private/data"));
Assert.Equal(TimeSpan.FromSeconds(2), robots.GetCrawlDelay("OtherCrawler"));

// Any other agent gets the catch-all group
Assert.False(robots.IsAllowed("SomeBot", "/products"));
Assert.Null(robots.GetCrawlDelay("SomeBot"));

// The user agent is a product token, not a full User-Agent header value
Assert.False(robots.IsAllowed("MyCrawler/2.1 (+https://example.com/bot)", "/products"));

var group = robots.GetGroup("MyCrawler");
Assert.NotNull(group);
Assert.Equal(["MyCrawler", "OtherCrawler"], group.UserAgents);
```
<!-- endref -->

- A group that names the user agent wins over the `*` group, wherever they are in the file. The comparison is
  case-insensitive.
- The `*` group applies to every agent that no group names.
- The user agent must equal a `User-agent` value: pass the product token of your crawler (`MyCrawler`), not the full
  `User-Agent` header (`MyCrawler/2.1 (+https://example.com/bot)`), which matches no named group.
- [UserAgents](cref:Meziantou.Framework.RobotsTxt.RobotsGroup.UserAgents) lists the agents a group applies to, and [Rules](cref:Meziantou.Framework.RobotsTxt.RobotsGroup.Rules) and [CrawlDelay](cref:Meziantou.Framework.RobotsTxt.RobotsGroup.CrawlDelay) hold its directives.

[RobotsGroup.Matches](cref:Meziantou.Framework.RobotsTxt.RobotsGroup.Matches) tells whether a group applies to a user agent, counting `*` as a match. To find the group that
[IsAllowed](cref:Meziantou.Framework.RobotsTxt.RobotsGroup.IsAllowed) uses, call [GetGroup](cref:Meziantou.Framework.RobotsTxt.RobotsFile.GetGroup): it is the one that prefers the exact match.

## No matching group

When no group names the agent and the file has no `*` group, [GetGroup](cref:Meziantou.Framework.RobotsTxt.RobotsFile.GetGroup) returns `null` and every path is allowed:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_NoMatchingGroup;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: OtherCrawler
    Disallow: /
    """);

// No group applies to MyCrawler: everything is allowed
Assert.Null(robots.GetGroup("MyCrawler"));
Assert.True(robots.IsAllowed("MyCrawler", "/anything"));
```
<!-- endref -->

An empty file, or one with only comments, allows everything for the same reason.

## Groups that name the same agent

When several groups name the selected agent, their rules are merged, as RFC 9309 requires:

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_MergedGroups;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    User-agent: MyCrawler
    Disallow: /a/

    User-agent: MyCrawler
    Disallow: /b/
    """);

// Both groups name MyCrawler: their rules are merged
Assert.HasCount(2, robots.Groups);
var group = robots.GetGroup("MyCrawler");
Assert.NotNull(group);
Assert.HasCount(2, group.Rules);
Assert.False(robots.IsAllowed("MyCrawler", "/a/page"));
Assert.False(robots.IsAllowed("MyCrawler", "/b/page"));
```
<!-- endref -->

[Groups](cref:Meziantou.Framework.RobotsTxt.RobotsFile.Groups) still holds the groups as they appear in the file. The merged group that [GetGroup](cref:Meziantou.Framework.RobotsTxt.RobotsFile.GetGroup) returns is a new
instance: it lists the agents of every merged group, their rules in file order, and the first `Crawl-delay` of the
merged groups. When a single group matches, [GetGroup](cref:Meziantou.Framework.RobotsTxt.RobotsFile.GetGroup) returns that instance of [Groups](cref:Meziantou.Framework.RobotsTxt.RobotsFile.Groups). The `*` groups are merged
the same way.

## Where a group starts and ends

<!-- ref:../../tests/Meziantou.Framework.RobotsTxt.Tests/RobotsFileTests.cs;region=RobotsFile_GroupBoundaries;format=md-fenced-code-block;dedent=true -->
```csharp
var robots = RobotsFile.Parse("""
    Disallow: /ignored/

    User-agent: MyCrawler
    # A comment line does not end the group
    Disallow: /private/
    User-agent: OtherCrawler
    Disallow: /tmp/
    """);

// The rule before the first User-agent line belongs to no group
Assert.True(robots.IsAllowed("MyCrawler", "/ignored/page"));

// A User-agent line after rules starts a new group
Assert.HasCount(2, robots.Groups);
Assert.False(robots.IsAllowed("MyCrawler", "/private/page"));
Assert.True(robots.IsAllowed("MyCrawler", "/tmp/page"));
```
<!-- endref -->

- Consecutive `User-agent` lines share one group.
- A `User-agent` line that follows an `Allow`, `Disallow` or `Crawl-delay` line starts a new group.
- A blank line ends the group. A line that holds only a comment does not, so a comment between a `User-agent` line
  and its rules keeps them together.
- Rules before the first `User-agent` line belong to no group and are ignored.
- `Sitemap` lines and unknown directives do not end a group.
