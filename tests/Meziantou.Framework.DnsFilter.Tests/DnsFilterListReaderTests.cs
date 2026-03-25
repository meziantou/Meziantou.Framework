using Xunit;

namespace Meziantou.Framework.DnsFilter.Tests;

public sealed class DnsFilterListReaderTests
{
    [Fact]
    public void ParseHostsFormat_BasicEntries()
    {
        var text = """
            # Comment line
            0.0.0.0 ads.example.com
            127.0.0.1 tracking.example.org
            """;

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.Hosts);

        Assert.Equal(2, rules.Count);
        Assert.Equal("ads.example.com", rules[0].ExactDomain);
        Assert.Equal(DnsFilterAction.Block, rules[0].Action);
        Assert.Equal("tracking.example.org", rules[1].ExactDomain);
    }

    [Fact]
    public void ParseHostsFormat_SkipsLocalhost()
    {
        var text = """
            127.0.0.1 localhost
            127.0.0.1 localhost.localdomain
            0.0.0.0 ads.example.com
            """;

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.Hosts);

        Assert.Single(rules);
        Assert.Equal("ads.example.com", rules[0].ExactDomain);
    }

    [Fact]
    public void ParseHostsFormat_InlineComments()
    {
        var text = "0.0.0.0 ads.example.com # block this";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.Hosts);

        Assert.Single(rules);
        Assert.Equal("ads.example.com", rules[0].ExactDomain);
    }

    [Fact]
    public void ParseHostsFormat_MultipleDomainsPerLine()
    {
        var text = "0.0.0.0 ads.example.com tracking.example.com";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.Hosts);

        Assert.Equal(2, rules.Count);
        Assert.Equal("ads.example.com", rules[0].ExactDomain);
        Assert.Equal("tracking.example.com", rules[1].ExactDomain);
    }

    [Fact]
    public void ParseHostsFormat_EmptyLines()
    {
        var text = """

            0.0.0.0 ads.example.com

            0.0.0.0 tracking.example.com

            """;

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.Hosts);

        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void ParseHostsFormat_TrailingDot()
    {
        var text = "0.0.0.0 ads.example.com.";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.Hosts);

        Assert.Single(rules);
        Assert.Equal("ads.example.com", rules[0].ExactDomain);
    }

    [Fact]
    public void ParseDomainsOnly_BasicEntries()
    {
        var text = """
            # Block list
            ads.example.com
            tracking.example.org
            """;

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.DomainsOnly);

        Assert.Equal(2, rules.Count);
        Assert.Equal("ads.example.com", rules[0].ExactDomain);
        Assert.Equal("tracking.example.org", rules[1].ExactDomain);
    }

    [Fact]
    public void ParseDomainsOnly_InlineComments()
    {
        var text = "ads.example.com # block this";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.DomainsOnly);

        Assert.Single(rules);
        Assert.Equal("ads.example.com", rules[0].ExactDomain);
    }

    [Fact]
    public void ParseDomainsOnly_EmptyAndCommentLines()
    {
        var text = """
            # comment
            ads.example.com

            # another comment
            tracking.example.com
            """;

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.DomainsOnly);

        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void ParseAdBlock_DomainWithSubdomainMatching()
    {
        var text = "||ads.example.com^";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.Equal("ads.example.com", rules[0].DomainSuffix);
        Assert.Equal(DnsFilterAction.Block, rules[0].Action);
    }

    [Fact]
    public void ParseAdBlock_ExceptionRule()
    {
        var text = "@@||example.com^";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.Equal("example.com", rules[0].DomainSuffix);
        Assert.Equal(DnsFilterAction.Allow, rules[0].Action);
    }

    [Fact]
    public void ParseAdBlock_ImportantModifier()
    {
        var text = "||example.com^$important";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.True(rules[0].IsImportant);
    }

    [Fact]
    public void ParseAdBlock_BadFilterModifier()
    {
        var text = "||example.com^$badfilter";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.True(rules[0].IsBadFilter);
    }

    [Fact]
    public void ParseAdBlock_DnsTypeModifier_Allowed()
    {
        var text = "||example.com^$dnstype=AAAA";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].AllowedDnsTypes);
        Assert.Contains(DnsFilterQueryType.AAAA, rules[0].AllowedDnsTypes);
    }

    [Fact]
    public void ParseAdBlock_DnsTypeModifier_Excluded()
    {
        var text = "||example.com^$dnstype=~AAAA";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].ExcludedDnsTypes);
        Assert.Contains(DnsFilterQueryType.AAAA, rules[0].ExcludedDnsTypes);
    }

    [Fact]
    public void ParseAdBlock_DnsTypeModifier_Multiple()
    {
        var text = "||example.com^$dnstype=A|AAAA";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].AllowedDnsTypes);
        Assert.Equal(2, rules[0].AllowedDnsTypes.Count);
        Assert.Contains(DnsFilterQueryType.A, rules[0].AllowedDnsTypes);
        Assert.Contains(DnsFilterQueryType.AAAA, rules[0].AllowedDnsTypes);
    }

    [Fact]
    public void ParseAdBlock_DenyAllowModifier()
    {
        var text = "*$denyallow=example.com|example.org";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].DenyAllowDomains);
        Assert.Equal(2, rules[0].DenyAllowDomains.Count);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_IpAddress()
    {
        var text = "||example.com^$dnsrewrite=1.2.3.4";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.NoError, rules[0].Rewrite.ResponseCode);
        Assert.Equal(DnsFilterQueryType.A, rules[0].Rewrite.RecordType);
        Assert.Equal("1.2.3.4", rules[0].Rewrite.Value);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_IPv6()
    {
        var text = "||example.com^$dnsrewrite=::1";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterQueryType.AAAA, rules[0].Rewrite.RecordType);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_Nxdomain()
    {
        var text = "||example.com^$dnsrewrite=NXDOMAIN";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.NameError, rules[0].Rewrite.ResponseCode);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_Refused()
    {
        var text = "||example.com^$dnsrewrite=REFUSED";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.Refused, rules[0].Rewrite.ResponseCode);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_FullSyntax()
    {
        var text = "||example.com^$dnsrewrite=NOERROR;A;1.2.3.4";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.NoError, rules[0].Rewrite.ResponseCode);
        Assert.Equal(DnsFilterQueryType.A, rules[0].Rewrite.RecordType);
        Assert.Equal("1.2.3.4", rules[0].Rewrite.Value);
    }

    [Fact]
    public void ParseAdBlock_ClientModifier_IpAddress()
    {
        var text = "||example.com^$client=192.168.1.1";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
    }

    [Fact]
    public void ParseAdBlock_ClientModifier_Cidr()
    {
        var text = "||example.com^$client=192.168.0.0/24";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
    }

    [Fact]
    public void ParseAdBlock_ClientModifier_Name()
    {
        var text = "||example.com^$client='Frank\\'s laptop'";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
    }

    [Fact]
    public void ParseAdBlock_CtagModifier()
    {
        var text = "||example.com^$ctag=device_phone|device_pc";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
    }

    [Fact]
    public void ParseAdBlock_CtagModifier_Exclusion()
    {
        var text = "||example.com^$ctag=~device_phone";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
    }

    [Fact]
    public void ParseAdBlock_RegexPattern()
    {
        var text = "/ads[0-9]+\\.example\\.com/";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Pattern);
    }

    [Fact]
    public void ParseAdBlock_WildcardPattern()
    {
        var text = "*ads*.example.com^";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Pattern);
    }

    [Fact]
    public void ParseAdBlock_Comments()
    {
        var text = """
            ! AdBlock comment
            # Hash comment
            [Adblock Plus 2.0]
            ||ads.example.com^
            """;

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.Equal("ads.example.com", rules[0].DomainSuffix);
    }

    [Fact]
    public void ParseAdBlock_PlainDomain()
    {
        var text = "example.com^";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.Equal("example.com", rules[0].ExactDomain);
    }

    [Fact]
    public void ParseAdBlock_MultipleModifiers()
    {
        var text = "||example.com^$important,dnstype=AAAA,dnsrewrite=REFUSED";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.True(rules[0].IsImportant);
        Assert.NotNull(rules[0].AllowedDnsTypes);
        Assert.Contains(DnsFilterQueryType.AAAA, rules[0].AllowedDnsTypes);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.Refused, rules[0].Rewrite.ResponseCode);
    }

    [Fact]
    public void AutoDetect_HostsFormat()
    {
        var lines = new List<string>
        {
            "# Comment",
            "0.0.0.0 ads.example.com",
            "127.0.0.1 tracking.example.com",
        };

        var format = DnsFilterListReader.DetectFormat(lines);

        Assert.Equal(DnsFilterListFormat.Hosts, format);
    }

    [Fact]
    public void AutoDetect_DomainsOnlyFormat()
    {
        var lines = new List<string>
        {
            "# Block list",
            "ads.example.com",
            "tracking.example.org",
        };

        var format = DnsFilterListReader.DetectFormat(lines);

        Assert.Equal(DnsFilterListFormat.DomainsOnly, format);
    }

    [Fact]
    public void AutoDetect_AdBlockFormat()
    {
        var lines = new List<string>
        {
            "! Title: My Filter",
            "||ads.example.com^",
            "@@||allowed.example.com^",
        };

        var format = DnsFilterListReader.DetectFormat(lines);

        Assert.Equal(DnsFilterListFormat.AdBlock, format);
    }

    [Fact]
    public void Parse_EmptyInput()
    {
        var rules = DnsFilterListReader.Parse("");

        Assert.Empty(rules);
    }

    [Fact]
    public void Parse_NullReader_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DnsFilterListReader.Parse((TextReader)null!));
    }

    [Fact]
    public void Parse_NullString_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DnsFilterListReader.Parse((string)null!));
    }
}
