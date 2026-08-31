using System.Net.Http.Headers;
using Meziantou.Framework.Http;

namespace Meziantou.Framework.Tests.Http;

public sealed class LinkHeaderValueTests
{
    [Fact]
    public void LinkHeaderValue_Parse()
    {
        var result = LinkHeaderValue.Parse("<sample>; rel=abc, <plop>; rel\t=\"d\\\"e;f,\"; title = test title; abc");
        Assert.Collection(result,
            item =>
            {
                Assert.Equal("sample", item.Url);
                Assert.Equal("abc", item.Rel);
            },
            item =>
            {
                Assert.Equal("plop", item.Url);
                Assert.Equal("d\"e;f,", item.Rel);
                Assert.Equal("test title", item.GetParameterValue("title"));
                var abcValue = item.GetParameterValue("abc");
                Assert.NotNull(abcValue);
                Assert.Empty(abcValue);
                Assert.Null(item.GetParameterValue("unknown"));
            });
    }

    [Fact]
    public void HttpResponse_Links()
    {
        var header = new CustomHttpHeaders
        {
            { "link", "<a>;rel=b, <c>; rel=d" },
            { "link", "<e>;rel=f" },
        };

        Assert.HasCount(3, header.EnumerateLinkHeaders());
    }

    [Fact]
    public void GetNextLink()
    {
        Assert.Equal("b", LinkHeaderValue.Parse("<a>; rel=prev, <b>;rel=next").GetLinkUrl("Next"));
    }

    [Theory]
    [InlineData("<https://example.com/2>; rel=next ; title=Page2")]
    [InlineData("<https://example.com/2>; rel=next , <https://example.com/1>; rel=prev")]
    [InlineData("<https://example.com/2>; rel=next\t")]
    [InlineData("<https://example.com/2>; rel=next ")]
    public void UnquotedParameterValue_TrailingWhitespaceIsTrimmed(string header)
    {
        var link = Assert.Single(LinkHeaderValue.Parse(header), l => l.Url is "https://example.com/2");
        Assert.Equal("next", link.Rel);
        Assert.Equal("https://example.com/2", LinkHeaderValue.Parse(header).GetLinkUrl("next"));
    }

    [Fact]
    public void UnquotedParameterValue_InnerWhitespaceIsPreserved()
    {
        var link = Assert.Single(LinkHeaderValue.Parse("<a>; title = test title ; rel=next"));
        Assert.Equal("test title", link.GetParameterValue("title"));
    }

    [Theory]
    [InlineData("rel")]
    [InlineData("REL")]
    [InlineData("Rel")]
    public void GetParameterValue_IsCaseInsensitive(string parameterName)
    {
        var link = Assert.Single(LinkHeaderValue.Parse("<a>; Rel=next; TITLE=hello"));
        Assert.Equal("next", link.GetParameterValue(parameterName));
        Assert.Equal("hello", link.GetParameterValue("Title"));
    }

    [Theory]
    [InlineData("<a>; rel=1, , <b>; rel=2")]
    [InlineData("<a>; rel=1,,<b>; rel=2")]
    [InlineData("<a>; rel=1, <b>; rel=2")]
    public void Parse_IgnoresEmptyListElements(string header)
    {
        var links = LinkHeaderValue.Parse(header);
        Assert.Collection(links,
            item => Assert.Equal("a", item.Url),
            item => Assert.Equal("b", item.Url));
    }

    [Theory]
    [InlineData("garbage, <b>; rel=2")]
    [InlineData(", <b>; rel=2")]
    [InlineData("<a; rel=1, <b>; rel=2")]
    [InlineData("garbage; title=\"a,b\", <b>; rel=2")]
    public void Parse_MalformedElementDoesNotDiscardTheFollowingOnes(string header)
    {
        var link = Assert.Single(LinkHeaderValue.Parse(header));
        Assert.Equal("b", link.Url);
        Assert.Equal("2", link.Rel);
    }

    [Theory]
    [InlineData("<https://example.com/a,b>; rel=next", "https://example.com/a,b")]
    [InlineData("<https://example.com/a,b>; rel=next, <https://example.com/c>; rel=prev", "https://example.com/a,b")]
    public void Parse_KeepsCommasInsideTheTargetUri(string header, string expectedUrl)
    {
        Assert.Equal(expectedUrl, LinkHeaderValue.Parse(header).GetLinkUrl("next"));
    }

    [Theory]
    [InlineData("<a>;\r\n rel=next")]
    [InlineData("<a>;\r\nrel=next")]
    [InlineData("<a>;\n rel=next")]
    [InlineData("<a>;\r\n\trel=next")]
    public void Parse_TreatsCarriageReturnAndLineFeedAsWhiteSpace(string header)
    {
        var link = Assert.Single(LinkHeaderValue.Parse(header));
        Assert.Equal("a", link.Url);
        Assert.Equal("next", link.Rel);
    }

    [Fact]
    public void Parse_TerminatesAValuelessParameterNameAtALineBreak()
    {
        var link = Assert.Single(LinkHeaderValue.Parse("<a>; abc\r\n; def"));
        Assert.Equal(["abc", "def"], link.Parameters.Select(p => p.Key));
    }

    private sealed class CustomHttpHeaders : HttpHeaders
    {
    }
}
