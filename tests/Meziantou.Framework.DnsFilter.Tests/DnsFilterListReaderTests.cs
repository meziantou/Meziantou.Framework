using System.Net;
using System.Text.RegularExpressions;

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
        Assert.Contains(DnsFilterQueryType.AAAA, rules[0].AllowedDnsTypes!);
    }

    [Fact]
    public void ParseAdBlock_DnsTypeModifier_Excluded()
    {
        var text = "||example.com^$dnstype=~AAAA";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].ExcludedDnsTypes);
        Assert.Contains(DnsFilterQueryType.AAAA, rules[0].ExcludedDnsTypes!);
    }

    [Fact]
    public void ParseAdBlock_DnsTypeModifier_Multiple()
    {
        var text = "||example.com^$dnstype=A|AAAA";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].AllowedDnsTypes);
        Assert.Equal(2, rules[0].AllowedDnsTypes!.Count);
        Assert.Contains(DnsFilterQueryType.A, rules[0].AllowedDnsTypes!);
        Assert.Contains(DnsFilterQueryType.AAAA, rules[0].AllowedDnsTypes!);
    }

    [Fact]
    public void ParseAdBlock_DenyAllowModifier()
    {
        var text = "*$denyallow=example.com|example.org";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].DenyAllowDomains);
        Assert.Equal(2, rules[0].DenyAllowDomains!.Count);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_IpAddress()
    {
        var text = "||example.com^$dnsrewrite=1.2.3.4";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.NoError, rules[0].Rewrite!.ResponseCode);
        Assert.Equal(DnsFilterQueryType.A, rules[0].Rewrite!.RecordType);
        Assert.Equal("1.2.3.4", rules[0].Rewrite!.Value);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_IPv6()
    {
        var text = "||example.com^$dnsrewrite=::1";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterQueryType.AAAA, rules[0].Rewrite!.RecordType);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_Nxdomain()
    {
        var text = "||example.com^$dnsrewrite=NXDOMAIN";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.NameError, rules[0].Rewrite!.ResponseCode);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_Refused()
    {
        var text = "||example.com^$dnsrewrite=REFUSED";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.Refused, rules[0].Rewrite!.ResponseCode);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_FullSyntax()
    {
        var text = "||example.com^$dnsrewrite=NOERROR;A;1.2.3.4";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        Assert.Single(rules);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.NoError, rules[0].Rewrite!.ResponseCode);
        Assert.Equal(DnsFilterQueryType.A, rules[0].Rewrite!.RecordType);
        Assert.Equal("1.2.3.4", rules[0].Rewrite!.Value);
    }

    [Fact]
    public void ParseAdBlock_ClientModifier_IpAddress()
    {
        var text = "||example.com^$client=192.168.1.1";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        var spec = Assert.Single(Assert.Single(rules).ClientSpecs!);
        Assert.False(spec.IsExclusion);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), spec.Address);
        Assert.Null(spec.Network);
        Assert.Null(spec.Name);
    }

    [Fact]
    public void ParseAdBlock_ClientModifier_Cidr()
    {
        var text = "||example.com^$client=192.168.0.0/24";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        var spec = Assert.Single(Assert.Single(rules).ClientSpecs!);
        Assert.False(spec.IsExclusion);
        Assert.Equal(IPNetwork.Parse("192.168.0.0/24"), spec.Network);
        Assert.Null(spec.Address);
        Assert.Null(spec.Name);
    }

    [Fact]
    public void ParseAdBlock_ClientModifier_Name()
    {
        var text = "||example.com^$client='Frank\\'s laptop'";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        var spec = Assert.Single(Assert.Single(rules).ClientSpecs!);
        Assert.False(spec.IsExclusion);
        Assert.Equal("Frank's laptop", spec.Name);
    }

    [Fact]
    public void ParseAdBlock_ClientModifier_Exclusion()
    {
        var rules = DnsFilterListReader.Parse("||example.com^$client=~192.168.1.1", DnsFilterListFormat.AdBlock);

        var spec = Assert.Single(Assert.Single(rules).ClientSpecs!);
        Assert.True(spec.IsExclusion);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), spec.Address);
    }

    [Fact]
    public void ParseAdBlock_ClientModifier_EscapedPipeInQuotedName()
    {
        var rules = DnsFilterListReader.Parse(@"||example.com^$client='Bob\|s PC'", DnsFilterListFormat.AdBlock);

        var spec = Assert.Single(Assert.Single(rules).ClientSpecs!);
        Assert.Equal("Bob|s PC", spec.Name);
    }

    [Fact]
    public void ParseAdBlock_CtagModifier()
    {
        var text = "||example.com^$ctag=device_phone|device_pc";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        var tagSpec = Assert.Single(rules).TagSpec!;
        Assert.Equal(["device_phone", "device_pc"], tagSpec.IncludedTags);
        Assert.Null(tagSpec.ExcludedTags);
    }

    [Fact]
    public void ParseAdBlock_CtagModifier_Exclusion()
    {
        var text = "||example.com^$ctag=~device_phone";

        var rules = DnsFilterListReader.Parse(text, DnsFilterListFormat.AdBlock);

        var tagSpec = Assert.Single(rules).TagSpec!;
        Assert.Equal(["device_phone"], tagSpec.ExcludedTags);
        Assert.Null(tagSpec.IncludedTags);
    }

    [Fact]
    public void ParseAdBlock_CtagModifier_MixedInclusionAndExclusion()
    {
        var rules = DnsFilterListReader.Parse("||example.com^$ctag=user_child|~device_pc", DnsFilterListFormat.AdBlock);

        var tagSpec = Assert.Single(rules).TagSpec!;
        Assert.Equal(["user_child"], tagSpec.IncludedTags);
        Assert.Equal(["device_pc"], tagSpec.ExcludedTags);
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

        var rule = Assert.Single(rules);
        Assert.NotNull(rule.Pattern);
        Assert.True(rule.Pattern.IsMatch("xadsy.example.com"));
        Assert.False(rule.Pattern.IsMatch("example.com"));
        Assert.False(rule.Pattern.IsMatch("ads.example.org"));
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
        Assert.Contains(DnsFilterQueryType.AAAA, rules[0].AllowedDnsTypes!);
        Assert.NotNull(rules[0].Rewrite);
        Assert.Equal(DnsFilterRewriteResponseCode.Refused, rules[0].Rewrite!.ResponseCode);
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

    [Theory]
    [InlineData("|")]
    [InlineData("||")]
    [InlineData("@@")]
    [InlineData("@@|")]
    [InlineData("@@||")]
    [InlineData("|$important")]
    [InlineData("@@|$important")]
    [InlineData("||^")]
    [InlineData("^")]
    [InlineData("||$dnsrewrite=1.2.3.4")]
    public void ParseAdBlock_DegeneratePattern_IsSkippedWithoutThrowing(string line)
    {
        var rules = DnsFilterListReader.Parse(line, DnsFilterListFormat.AdBlock);

        Assert.Empty(rules);
    }

    [Fact]
    public void ParseAdBlock_MalformedLine_DoesNotDiscardTheRestOfTheList()
    {
        var text = """
            ||ads1.example.com^
            |
            ||ads2.example.com^
            """;

        var result = DnsFilterListReader.ParseWithDiagnostics(text, DnsFilterListFormat.AdBlock);

        Assert.Equal(2, result.Rules.Count);
        Assert.Equal(2, Assert.Single(result.Diagnostics).LineNumber);
    }

    [Theory]
    [InlineData("||example.com^$third-party", "third-party")]
    [InlineData("||example.com^$script,image", "script")]
    [InlineData("||example.com^$domain=other.org", "domain=other.org")]
    [InlineData("@@||example.com^$app=com.example", "app=com.example")]
    [InlineData("||example.com^$importnat", "importnat")]
    public void ParseAdBlock_UnsupportedModifier_DiscardsTheRule(string line, string expectedDetail)
    {
        var result = DnsFilterListReader.ParseWithDiagnostics(line, DnsFilterListFormat.AdBlock);

        Assert.Empty(result.Rules);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DnsFilterParseError.UnsupportedModifier, diagnostic.Error);
        Assert.Equal(expectedDetail, diagnostic.Detail);
    }

    [Theory]
    [InlineData("||example.com^$dnstype=NOTATYPE")]
    [InlineData("||example.com^$dnstype=")]
    [InlineData("||example.com^$client=192.168.1.0/33")]
    [InlineData("||example.com^$client=")]
    [InlineData("||example.com^$ctag=")]
    [InlineData("||example.com^$denyallow=not a domain")]
    [InlineData("||example.com^$dnsrewrite=NOERROR;A;not-an-ip")]
    [InlineData("||example.com^$dnsrewrite=BOGUSCODE;A;1.2.3.4")]
    public void ParseAdBlock_InvalidModifierValue_DiscardsTheRule(string line)
    {
        var result = DnsFilterListReader.ParseWithDiagnostics(line, DnsFilterListFormat.AdBlock);

        Assert.Empty(result.Rules);
        Assert.Equal(DnsFilterParseError.InvalidModifierValue, Assert.Single(result.Diagnostics).Error);
    }

    [Theory]
    [InlineData("SVCB", DnsFilterQueryType.SVCB)]
    [InlineData("CAA", DnsFilterQueryType.CAA)]
    [InlineData("65", DnsFilterQueryType.HTTPS)]
    [InlineData("TYPE64", DnsFilterQueryType.SVCB)]
    public void ParseAdBlock_DnsType_AcceptsModernAndNumericTypes(string token, DnsFilterQueryType expected)
    {
        var rules = DnsFilterListReader.Parse($"||example.com^$dnstype={token}", DnsFilterListFormat.AdBlock);

        Assert.Equal([expected], Assert.Single(rules).AllowedDnsTypes);
    }

    [Fact]
    public void ParseAdBlock_WildcardInsideDomainAnchor_ProducesAPattern()
    {
        var rules = DnsFilterListReader.Parse("||*.example.com^", DnsFilterListFormat.AdBlock);

        var rule = Assert.Single(rules);
        Assert.Null(rule.DomainSuffix);
        Assert.NotNull(rule.Pattern);
        Assert.True(rule.Pattern.IsMatch("sub.example.com"));
    }

    [Fact]
    public void ParseAdBlock_RegexWithEscapedSlash_IsParsed()
    {
        var rules = DnsFilterListReader.Parse(@"/^ads\/x.*\.example\.com$/", DnsFilterListFormat.AdBlock);

        Assert.NotNull(Assert.Single(rules).Pattern);
    }

    [Theory]
    [InlineData("/[unclosed/")]
    [InlineData("/abc")]
    public void ParseAdBlock_InvalidRegex_IsReported(string line)
    {
        var result = DnsFilterListReader.ParseWithDiagnostics(line, DnsFilterListFormat.AdBlock);

        Assert.Empty(result.Rules);
        Assert.Equal(DnsFilterParseError.InvalidRegex, Assert.Single(result.Diagnostics).Error);
    }

    [Fact]
    public void ParseAdBlock_DoubleSeparator_IsStripped()
    {
        var rules = DnsFilterListReader.Parse("||example.com^^", DnsFilterListFormat.AdBlock);

        Assert.Equal("example.com", Assert.Single(rules).DomainSuffix);
    }

    [Fact]
    public void ParseAdBlock_UnbalancedQuote_DoesNotSwallowLaterModifiers()
    {
        var rules = DnsFilterListReader.Parse("||example.com^$client=Franks',important", DnsFilterListFormat.AdBlock);

        Assert.True(Assert.Single(rules).IsImportant);
    }

    [Fact]
    public void ParseAdBlock_QuotedValueContainingComma_IsOneModifier()
    {
        var rules = DnsFilterListReader.Parse("||example.com^$client='a,b',important", DnsFilterListFormat.AdBlock);

        var rule = Assert.Single(rules);
        Assert.True(rule.IsImportant);
        Assert.Equal("a,b", Assert.Single(rule.ClientSpecs!).Name);
    }

    [Theory]
    [InlineData("REFUSED", DnsFilterRewriteResponseCode.Refused)]
    [InlineData("NXDOMAIN", DnsFilterRewriteResponseCode.NameError)]
    [InlineData("SERVFAIL", DnsFilterRewriteResponseCode.ServerFailure)]
    public void ParseAdBlock_DnsRewrite_ResponseCodeKeywords(string keyword, DnsFilterRewriteResponseCode expected)
    {
        var rules = DnsFilterListReader.Parse($"||example.com^$dnsrewrite={keyword}", DnsFilterListFormat.AdBlock);

        Assert.Equal(expected, Assert.Single(rules).Rewrite!.ResponseCode);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_BareIntegerIsNotAnIPAddress()
    {
        // IPAddress.TryParse("1234") succeeds and yields 0.0.4.210, which would silently become a
        // bogus A record; it must be treated as a domain-shaped value instead.
        var result = DnsFilterListReader.ParseWithDiagnostics("||example.com^$dnsrewrite=1234", DnsFilterListFormat.AdBlock);

        var rule = Assert.Single(result.Rules);
        Assert.Equal(DnsFilterQueryType.CNAME, rule.Rewrite!.RecordType);
        Assert.Equal("1234", rule.Rewrite.Value);
    }

    [Fact]
    public void ParseAdBlock_DnsRewrite_ValueKeepsSemicolons()
    {
        var rules = DnsFilterListReader.Parse("||example.com^$dnsrewrite=NOERROR;TXT;a;b", DnsFilterListFormat.AdBlock);

        Assert.Equal("a;b", Assert.Single(rules).Rewrite!.Value);
    }

    [Fact]
    public void DetectFormat_SingleDollarSignDoesNotFlipAHostsList()
    {
        var text = """
            # Title: my hosts
            0.0.0.0 ads.example.com
            0.0.0.0 scam.example.com # win $1000
            0.0.0.0 tracker.example.org
            """;

        var result = DnsFilterListReader.ParseWithDiagnostics(text);

        Assert.Equal(DnsFilterListFormat.Hosts, result.Format);
        Assert.Equal(3, result.Rules.Count);
    }

    [Fact]
    public void DetectFormat_MinorityAdBlockLinesDoNotFlipAHostsList()
    {
        var text = """
            0.0.0.0 a.example.com
            0.0.0.0 b.example.com
            0.0.0.0 c.example.com
            ||d.example.com^
            """;

        var result = DnsFilterListReader.ParseWithDiagnostics(text);

        Assert.Equal(DnsFilterListFormat.Hosts, result.Format);
        Assert.Equal(3, result.Rules.Count);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void ParseAdBlock_HostsLineIsRejectedRatherThanBecomingADeadRule()
    {
        var result = DnsFilterListReader.ParseWithDiagnostics("0.0.0.0 ads.example.com", DnsFilterListFormat.AdBlock);

        Assert.Empty(result.Rules);
        Assert.Equal(DnsFilterParseError.InvalidPattern, Assert.Single(result.Diagnostics).Error);
    }

    [Fact]
    public void ParseDomainsOnly_AdBlockHeaderIsNotTreatedAsADomain()
    {
        var result = DnsFilterListReader.ParseWithDiagnostics("""
            !Title: my list
            ads.example.com
            """);

        Assert.Equal("ads.example.com", Assert.Single(result.Rules).ExactDomain);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Parse_InternationalizedDomain_IsConvertedToPunycode()
    {
        var rules = DnsFilterListReader.Parse("||пример.рф^", DnsFilterListFormat.AdBlock);

        Assert.Equal("xn--e1afmkfd.xn--p1ai", Assert.Single(rules).DomainSuffix);
    }

    [Theory]
    [InlineData(@"^139\.45\.197\.2(4[0-9]|5[0-4]):", "139.45.197.2")]
    [InlineData(@"^ads\.example\.com$", "ads.example.com")]
    [InlineData("^abc", "abc")]
    [InlineData(@"^ab\d+", null)]
    [InlineData("^abc|def", null)]
    [InlineData(@"^(a|c)\.[0-9a-f]{56}\.com$", null)]
    [InlineData("^ab", null)]
    [InlineData("^abcd*e", "abc")]
    public void GetRegexLiteralPrefix_ExtractsOnlyMandatoryPrefixes(string source, string? expected)
    {
        Assert.Equal(expected, DnsFilterListReader.GetRegexLiteralPrefix(source));
    }

    [Fact]
    public void GetRegexLiteralPrefix_NeverRejectsANameTheRegexWouldMatch()
    {
        // The prefilter is only sound if every string the regex matches contains the literal.
        string[] sources = [@"^139\.45\.197\.2(4[0-9]|5[0-4])", @"^ads\.example\.com$", "^abcd*e"];
        string[] samples = ["139.45.197.244", "ads.example.com", "abce", "abcddde", "nope.example.org"];

        foreach (var source in sources)
        {
            var prefix = DnsFilterListReader.GetRegexLiteralPrefix(source);
            if (prefix is null)
                continue;

            var regex = new Regex(source, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            foreach (var sample in samples)
            {
                if (regex.IsMatch(sample))
                {
                    Assert.Contains(prefix, sample);
                }
            }
        }
    }

    [Fact]
    public void ParseWithDiagnostics_ReportsLineNumbersAndText()
    {
        var text = """
            ||good.example.com^
            ||bad.example.com^$third-party
            """;

        var result = DnsFilterListReader.ParseWithDiagnostics(text, DnsFilterListFormat.AdBlock);

        Assert.Single(result.Rules);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(2, diagnostic.LineNumber);
        Assert.Equal("||bad.example.com^$third-party", diagnostic.Line);
    }
}
