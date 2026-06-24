using System.Text;

namespace Meziantou.Framework.RobotsTxt.Tests;

public sealed class RobotsFileTests
{
    // -------------------------------------------------------------------------
    // Parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_EmptyContent_ReturnsEmptyFile()
    {
        var robots = RobotsFile.Parse("");

        Assert.Empty(robots.Groups);
        Assert.Empty(robots.Sitemaps);
    }

    [Fact]
    public void Parse_CommentsOnly_ReturnsEmptyFile()
    {
        var robots = RobotsFile.Parse("# This is a comment\n# Another comment");

        Assert.Empty(robots.Groups);
    }

    [Fact]
    public void Parse_SingleGroup_BasicStructure()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /private/
            Allow: /public/
            """);

        var group = Assert.Single(robots.Groups);
        Assert.Equal(["*"], group.UserAgents);
        Assert.Equal(2, group.Rules.Count);
        Assert.Equal(RobotsRuleKind.Disallow, group.Rules[0].Kind);
        Assert.Equal("/private/", group.Rules[0].Value);
        Assert.Equal(RobotsRuleKind.Allow, group.Rules[1].Kind);
        Assert.Equal("/public/", group.Rules[1].Value);
    }

    [Fact]
    public void Parse_MultipleGroups_SeparatedByBlankLine()
    {
        var robots = RobotsFile.Parse("""
            User-agent: Googlebot
            Allow: /

            User-agent: BadBot
            Disallow: /
            """);

        Assert.Equal(2, robots.Groups.Count);
        Assert.Equal(["Googlebot"], robots.Groups[0].UserAgents);
        Assert.Equal(["BadBot"], robots.Groups[1].UserAgents);
    }

    [Fact]
    public void Parse_MultipleUserAgentsInGroup()
    {
        var robots = RobotsFile.Parse("""
            User-agent: Googlebot
            User-agent: Bingbot
            Disallow: /secret/
            """);

        var group = Assert.Single(robots.Groups);
        Assert.Equal(["Googlebot", "Bingbot"], group.UserAgents);
    }

    [Fact]
    public void Parse_CrawlDelay()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /
            Crawl-delay: 10
            """);

        var group = Assert.Single(robots.Groups);
        Assert.Equal(TimeSpan.FromSeconds(10), group.CrawlDelay);
    }

    [Fact]
    public void Parse_CrawlDelayDecimal()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow:
            Crawl-delay: 0.5
            """);

        Assert.Equal(TimeSpan.FromSeconds(0.5), robots.Groups[0].CrawlDelay);
    }

    [Fact]
    public void Parse_Sitemaps()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow:

            Sitemap: https://example.com/sitemap.xml
            Sitemap: https://example.com/sitemap2.xml
            """);

        Assert.Equal(["https://example.com/sitemap.xml", "https://example.com/sitemap2.xml"], robots.Sitemaps);
    }

    [Fact]
    public void Parse_InlineCommentStripped()
    {
        var robots = RobotsFile.Parse("""
            User-agent: * # all bots
            Disallow: /private/ # secret area
            """);

        var group = Assert.Single(robots.Groups);
        Assert.Equal("*", group.UserAgents[0]);
        Assert.Equal("/private/", group.Rules[0].Value);
    }

    [Fact]
    public void Parse_UnknownDirectivesIgnored()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /
            Host: example.com
            UnknownDirective: value
            """);

        var group = Assert.Single(robots.Groups);
        Assert.Single(group.Rules);
    }

    [Fact]
    public void Parse_MalformedLinesIgnored()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            this line has no colon
            Disallow: /secret/
            """);

        Assert.Single(robots.Groups[0].Rules);
        Assert.Equal("/secret/", robots.Groups[0].Rules[0].Value);
    }

    [Fact]
    public void Parse_CaseInsensitiveDirectives()
    {
        var robots = RobotsFile.Parse("""
            USER-AGENT: *
            DISALLOW: /
            ALLOW: /public/
            """);

        Assert.Single(robots.Groups);
        Assert.Equal(2, robots.Groups[0].Rules.Count);
    }

    [Fact]
    public void Parse_TrailingGroupWithoutBlankLine()
    {
        // File without trailing newline / blank line should still produce a group.
        var robots = RobotsFile.Parse("User-agent: *\nDisallow: /");

        Assert.Single(robots.Groups);
    }

    // -------------------------------------------------------------------------
    // GetGroup
    // -------------------------------------------------------------------------

    [Fact]
    public void GetGroup_ExactMatchPreferredOverCatchAll()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /all/

            User-agent: Googlebot
            Disallow: /google/
            """);

        var group = robots.GetGroup("Googlebot");
        Assert.NotNull(group);
        Assert.Contains("Googlebot", group.UserAgents);
    }

    [Fact]
    public void GetGroup_FallsBackToCatchAll()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /all/
            """);

        var group = robots.GetGroup("UnknownBot");
        Assert.NotNull(group);
        Assert.Contains("*", group.UserAgents);
    }

    [Fact]
    public void GetGroup_ReturnsNullWhenNoMatch()
    {
        var robots = RobotsFile.Parse("""
            User-agent: Googlebot
            Disallow: /
            """);

        Assert.Null(robots.GetGroup("SomeOtherBot"));
    }

    [Fact]
    public void GetGroup_UserAgentMatchIsCaseInsensitive()
    {
        var robots = RobotsFile.Parse("""
            User-agent: Googlebot
            Disallow: /
            """);

        Assert.NotNull(robots.GetGroup("googlebot"));
        Assert.NotNull(robots.GetGroup("GOOGLEBOT"));
    }

    // -------------------------------------------------------------------------
    // IsAllowed
    // -------------------------------------------------------------------------

    [Fact]
    public void IsAllowed_NoMatchingGroup_AllowsByDefault()
    {
        var robots = RobotsFile.Parse("""
            User-agent: Googlebot
            Disallow: /
            """);

        Assert.True(robots.IsAllowed("SomeOtherBot", "/anything"));
    }

    [Fact]
    public void IsAllowed_EmptyDisallow_AllowsAll()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow:
            """);

        Assert.True(robots.IsAllowed("AnyBot", "/private/page"));
    }

    [Fact]
    public void IsAllowed_DisallowAll_DisallowsEverything()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /
            """);

        Assert.False(robots.IsAllowed("AnyBot", "/any/path"));
    }

    [Fact]
    public void IsAllowed_MoreSpecificRuleWins()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /secret/
            Allow: /secret/public/
            """);

        Assert.False(robots.IsAllowed("Bot", "/secret/private"));
        Assert.True(robots.IsAllowed("Bot", "/secret/public/page"));
    }

    [Fact]
    public void IsAllowed_AllowWinsOnTie()
    {
        // Same-length Allow and Disallow — Allow should win.
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /page
            Allow: /page
            """);

        Assert.True(robots.IsAllowed("Bot", "/page"));
    }

    [Fact]
    public void IsAllowed_Uri_UsesPathAndQuery()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow: /private/
            """);

        Assert.False(robots.IsAllowed("Bot", new Uri("https://example.com/private/secret")));
        Assert.True(robots.IsAllowed("Bot", new Uri("https://example.com/public/page")));
    }

    // -------------------------------------------------------------------------
    // GetCrawlDelay
    // -------------------------------------------------------------------------

    [Fact]
    public void GetCrawlDelay_ReturnsValueForMatchingGroup()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow:
            Crawl-delay: 5
            """);

        Assert.Equal(TimeSpan.FromSeconds(5), robots.GetCrawlDelay("AnyBot"));
    }

    [Fact]
    public void GetCrawlDelay_ReturnsNullWhenNoCrawlDelay()
    {
        var robots = RobotsFile.Parse("""
            User-agent: *
            Disallow:
            """);

        Assert.Null(robots.GetCrawlDelay("AnyBot"));
    }

    [Fact]
    public void GetCrawlDelay_ReturnsNullWhenNoMatchingGroup()
    {
        var robots = RobotsFile.Parse("""
            User-agent: Googlebot
            Crawl-delay: 3
            """);

        Assert.Null(robots.GetCrawlDelay("UnknownBot"));
    }

    // -------------------------------------------------------------------------
    // RobotsRule.Matches — wildcard patterns
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/private/", "/private/page", true)]
    [InlineData("/private/", "/public/page", false)]
    [InlineData("/private/", "/private/", true)]
    [InlineData("/", "/anything", true)]
    [InlineData("", "/anything", true)]        // empty pattern prefix-matches everything; semantic (allow-all) is handled in IsAllowed
    [InlineData("/fish", "/fish", true)]
    [InlineData("/fish", "/fish.html", true)]  // prefix match
    [InlineData("/fish", "/fishing", true)]
    [InlineData("/fish", "/Fish.asp", false)]  // case-sensitive
    public void RobotsRule_Matches_PrefixPatterns(string pattern, string path, bool expected)
    {
        var rule = new RobotsRule(RobotsRuleKind.Disallow, pattern);
        Assert.Equal(expected, rule.Matches(path));
    }

    [Theory]
    [InlineData("/fish*", "/fish", true)]
    [InlineData("/fish*", "/fishing/resource", true)]
    [InlineData("/fish*", "/Fish.html", false)]
    [InlineData("/*.php", "/index.php", true)]
    [InlineData("/*.php", "/index.php?q=1", true)]
    [InlineData("/*.php", "/index.aspx", false)]
    [InlineData("/private*page", "/private-some-page", true)]
    [InlineData("/private*page", "/private-other", false)]
    public void RobotsRule_Matches_WildcardStar(string pattern, string path, bool expected)
    {
        var rule = new RobotsRule(RobotsRuleKind.Disallow, pattern);
        Assert.Equal(expected, rule.Matches(path));
    }

    [Theory]
    [InlineData("/fish$", "/fish", true)]
    [InlineData("/fish$", "/fish.html", false)]   // $ anchors → no extension allowed
    [InlineData("/fish$", "/fish/", false)]
    [InlineData("/*.php$", "/index.php", true)]
    [InlineData("/*.php$", "/index.php?q=1", false)] // query string breaks $ anchor
    public void RobotsRule_Matches_EndAnchor(string pattern, string path, bool expected)
    {
        var rule = new RobotsRule(RobotsRuleKind.Disallow, pattern);
        Assert.Equal(expected, rule.Matches(path));
    }

    // -------------------------------------------------------------------------
    // Async parsing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParseAsync_Stream_Works()
    {
        const string content = "User-agent: *\nDisallow: /\n";
        using var stream = new System.IO.MemoryStream(Encoding.UTF8.GetBytes(content));

        var robots = await RobotsFile.ParseAsync(stream);

        Assert.Single(robots.Groups);
        Assert.False(robots.IsAllowed("Bot", "/anything"));
    }

    [Fact]
    public async Task ParseAsync_TextReader_Works()
    {
        const string content = "User-agent: *\nDisallow: /secret/\n";
        using var reader = new System.IO.StringReader(content);

        var robots = await RobotsFile.ParseAsync(reader);

        Assert.False(robots.IsAllowed("Bot", "/secret/page"));
        Assert.True(robots.IsAllowed("Bot", "/public/page"));
    }
}
