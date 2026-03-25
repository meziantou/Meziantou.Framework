using System.Net;
using Xunit;

namespace Meziantou.Framework.DnsFilter.Tests;

public sealed class DnsFilterEngineTests
{
    [Fact]
    public void Evaluate_ExactDomainMatch_Blocked()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("0.0.0.0 ads.example.com", DnsFilterListFormat.Hosts);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("ads.example.com");

        Assert.True(result.IsMatched);
        Assert.Equal(DnsFilterAction.Block, result.Action);
    }

    [Fact]
    public void Evaluate_ExactDomainMatch_CaseInsensitive()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("0.0.0.0 ADS.Example.COM", DnsFilterListFormat.Hosts);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("ads.example.com");

        Assert.True(result.IsMatched);
    }

    [Fact]
    public void Evaluate_NoMatch_ReturnsNotMatched()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("0.0.0.0 ads.example.com", DnsFilterListFormat.Hosts);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("safe.example.com");

        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Evaluate_SubdomainMatch_WithSuffixRule()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("sub.example.com");

        Assert.True(result.IsMatched);
        Assert.Equal(DnsFilterAction.Block, result.Action);
    }

    [Fact]
    public void Evaluate_ExactMatch_WithSuffixRule()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");

        Assert.True(result.IsMatched);
    }

    [Fact]
    public void Evaluate_DeepSubdomain_WithSuffixRule()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("a.b.c.example.com");

        Assert.True(result.IsMatched);
    }

    [Fact]
    public void Evaluate_SuffixRule_DoesNotMatchPartialDomain()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("notexample.com");

        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Evaluate_ExceptionRule_AllowsBlockedDomain()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("""
            ||example.com^
            @@||safe.example.com^
            """, DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var blockedResult = engine.Evaluate("ads.example.com");
        Assert.True(blockedResult.IsMatched);
        Assert.Equal(DnsFilterAction.Block, blockedResult.Action);

        var allowedResult = engine.Evaluate("safe.example.com");
        Assert.True(allowedResult.IsMatched);
        Assert.Equal(DnsFilterAction.Allow, allowedResult.Action);
    }

    [Fact]
    public void Evaluate_ImportantBlock_OverridesException()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("""
            ||example.com^$important
            @@||example.com^
            """, DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");

        Assert.True(result.IsMatched);
        Assert.Equal(DnsFilterAction.Block, result.Action);
    }

    [Fact]
    public void Evaluate_ImportantAllow_OverridesNormalBlock()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("""
            ||example.com^
            @@||example.com^$important
            """, DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");

        Assert.True(result.IsMatched);
        Assert.Equal(DnsFilterAction.Allow, result.Action);
    }

    [Fact]
    public void Evaluate_DnsType_MatchesSpecificType()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$dnstype=AAAA", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var aaaaResult = engine.Evaluate("example.com", DnsFilterQueryType.AAAA);
        Assert.True(aaaaResult.IsMatched);

        var aResult = engine.Evaluate("example.com", DnsFilterQueryType.A);
        Assert.False(aResult.IsMatched);
    }

    [Fact]
    public void Evaluate_DnsType_ExcludesSpecificType()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$dnstype=~AAAA", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var aResult = engine.Evaluate("example.com", DnsFilterQueryType.A);
        Assert.True(aResult.IsMatched);

        var aaaaResult = engine.Evaluate("example.com", DnsFilterQueryType.AAAA);
        Assert.False(aaaaResult.IsMatched);
    }

    [Fact]
    public void Evaluate_DenyAllow_ExcludesDomain()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("*$denyallow=example.com", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var blockedResult = engine.Evaluate("ads.other.com");
        Assert.True(blockedResult.IsMatched);
        Assert.Equal(DnsFilterAction.Block, blockedResult.Action);

        var allowedResult = engine.Evaluate("example.com");
        Assert.False(allowedResult.IsMatched);
    }

    [Fact]
    public void Evaluate_DenyAllow_ExcludesSubdomain()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("*$denyallow=example.com", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("sub.example.com");
        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Evaluate_DnsRewrite_ReturnsRewriteInfo()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$dnsrewrite=1.2.3.4", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");

        Assert.True(result.IsMatched);
        Assert.NotNull(result.Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.NoError, result.Rewrite.ResponseCode);
        Assert.Equal(DnsFilterQueryType.A, result.Rewrite.RecordType);
        Assert.Equal("1.2.3.4", result.Rewrite.Value);
    }

    [Fact]
    public void Evaluate_BadFilter_DisablesRule()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("""
            ||example.com^
            ||example.com^$badfilter
            """, DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");

        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Evaluate_RegexPattern_Matches()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("/ads[0-9]+\\.example\\.com/", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var matchResult = engine.Evaluate("ads123.example.com");
        Assert.True(matchResult.IsMatched);

        var noMatchResult = engine.Evaluate("safe.example.com");
        Assert.False(noMatchResult.IsMatched);
    }

    [Fact]
    public void Evaluate_WildcardPattern_Matches()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("ad*.example.com^", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var matchResult = engine.Evaluate("ads.example.com");
        Assert.True(matchResult.IsMatched);

        var noMatchResult = engine.Evaluate("safe.example.com");
        Assert.False(noMatchResult.IsMatched);
    }

    [Fact]
    public void Evaluate_Client_IpAddress_Matches()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$client=192.168.1.1", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var matchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Address = IPAddress.Parse("192.168.1.1") });
        Assert.True(matchResult.IsMatched);

        var noMatchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Address = IPAddress.Parse("10.0.0.1") });
        Assert.False(noMatchResult.IsMatched);
    }

    [Fact]
    public void Evaluate_Client_Cidr_Matches()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$client=192.168.1.0/24", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var matchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Address = IPAddress.Parse("192.168.1.50") });
        Assert.True(matchResult.IsMatched);

        var noMatchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Address = IPAddress.Parse("10.0.0.1") });
        Assert.False(noMatchResult.IsMatched);
    }

    [Fact]
    public void Evaluate_Client_Name_Matches()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$client=MyLaptop", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var matchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Name = "MyLaptop" });
        Assert.True(matchResult.IsMatched);

        var noMatchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Name = "OtherDevice" });
        Assert.False(noMatchResult.IsMatched);
    }

    [Fact]
    public void Evaluate_Client_Exclusion()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$client=~192.168.1.1", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var excludedResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Address = IPAddress.Parse("192.168.1.1") });
        Assert.False(excludedResult.IsMatched);

        var matchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Address = IPAddress.Parse("10.0.0.1") });
        Assert.True(matchResult.IsMatched);
    }

    [Fact]
    public void Evaluate_Client_NoClientInfo_DoesNotMatch()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$client=192.168.1.1", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");
        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Evaluate_Ctag_Matches()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$ctag=device_phone", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var matchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Tags = ["device_phone", "os_android"] });
        Assert.True(matchResult.IsMatched);

        var noMatchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Tags = ["device_pc"] });
        Assert.False(noMatchResult.IsMatched);
    }

    [Fact]
    public void Evaluate_Ctag_Exclusion()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$ctag=~device_phone", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var matchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Tags = ["device_pc"] });
        Assert.True(matchResult.IsMatched);

        var noMatchResult = engine.Evaluate("example.com", DnsFilterQueryType.A, new DnsClientInfo { Tags = ["device_phone"] });
        Assert.False(noMatchResult.IsMatched);
    }

    [Fact]
    public void Evaluate_Ctag_NoTags_DoesNotMatch()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$ctag=device_phone", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");
        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Evaluate_TrailingDot_Normalized()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("0.0.0.0 ads.example.com", DnsFilterListFormat.Hosts);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("ads.example.com.");

        Assert.True(result.IsMatched);
    }

    [Fact]
    public void Evaluate_CaseInsensitiveQuery()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("0.0.0.0 ads.example.com", DnsFilterListFormat.Hosts);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("ADS.EXAMPLE.COM");

        Assert.True(result.IsMatched);
    }

    [Fact]
    public void Evaluate_EmptyDomain_ReturnsNotMatched()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("0.0.0.0 ads.example.com", DnsFilterListFormat.Hosts);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("");

        Assert.False(result.IsMatched);
    }

    [Fact]
    public void Reload_ReplacesRules()
    {
        var ruleSet1 = new DnsFilterRuleSet();
        ruleSet1.AddFromList("0.0.0.0 old.example.com", DnsFilterListFormat.Hosts);
        var engine = new DnsFilterEngine(ruleSet1);

        Assert.True(engine.Evaluate("old.example.com").IsMatched);
        Assert.False(engine.Evaluate("new.example.com").IsMatched);

        var ruleSet2 = new DnsFilterRuleSet();
        ruleSet2.AddFromList("0.0.0.0 new.example.com", DnsFilterListFormat.Hosts);
        engine.Reload(ruleSet2);

        Assert.False(engine.Evaluate("old.example.com").IsMatched);
        Assert.True(engine.Evaluate("new.example.com").IsMatched);
    }

    [Fact]
    public void Evaluate_MultipleListsCombined()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("0.0.0.0 ads.example.com", DnsFilterListFormat.Hosts);
        ruleSet.AddFromList("||tracking.example.com^", DnsFilterListFormat.AdBlock);
        ruleSet.AddFromList("malware.example.com", DnsFilterListFormat.DomainsOnly);
        var engine = new DnsFilterEngine(ruleSet);

        Assert.True(engine.Evaluate("ads.example.com").IsMatched);
        Assert.True(engine.Evaluate("tracking.example.com").IsMatched);
        Assert.True(engine.Evaluate("sub.tracking.example.com").IsMatched);
        Assert.True(engine.Evaluate("malware.example.com").IsMatched);
        Assert.False(engine.Evaluate("safe.example.com").IsMatched);
    }

    [Fact]
    public void Evaluate_HostsRule_DoesNotMatchSubdomain()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("0.0.0.0 example.com", DnsFilterListFormat.Hosts);
        var engine = new DnsFilterEngine(ruleSet);

        Assert.True(engine.Evaluate("example.com").IsMatched);
        Assert.False(engine.Evaluate("sub.example.com").IsMatched);
    }

    [Fact]
    public void Evaluate_NullDomain_Throws()
    {
        var ruleSet = new DnsFilterRuleSet();
        var engine = new DnsFilterEngine(ruleSet);

        Assert.Throws<ArgumentNullException>(() => engine.Evaluate(null!));
    }

    [Fact]
    public void Constructor_NullRuleSet_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DnsFilterEngine(null!));
    }

    [Fact]
    public void Reload_NullRuleSet_Throws()
    {
        var ruleSet = new DnsFilterRuleSet();
        var engine = new DnsFilterEngine(ruleSet);

        Assert.Throws<ArgumentNullException>(() => engine.Reload(null!));
    }

    [Fact]
    public void Evaluate_PriorityOrder_ImportantBlockBeatsImportantAllow()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("""
            ||example.com^$important
            @@||example.com^$important
            """, DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");

        Assert.True(result.IsMatched);
        Assert.Equal(DnsFilterAction.Block, result.Action);
    }

    [Fact]
    public void Evaluate_DnsRewrite_NxdomainFullSyntax()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("||example.com^$dnsrewrite=NXDOMAIN;;", DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");

        Assert.True(result.IsMatched);
        Assert.NotNull(result.Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.NameError, result.Rewrite.ResponseCode);
    }

    [Fact]
    public void Evaluate_BadFilter_WithModifiers()
    {
        var ruleSet = new DnsFilterRuleSet();
        ruleSet.AddFromList("""
            ||example.com^$important
            ||example.com^$important,badfilter
            """, DnsFilterListFormat.AdBlock);
        var engine = new DnsFilterEngine(ruleSet);

        var result = engine.Evaluate("example.com");

        Assert.False(result.IsMatched);
    }
}
