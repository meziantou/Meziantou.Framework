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
    [InlineData("start")]
    [InlineData("http://example.net/relation/other")]
    [InlineData("START")]
    public void GetLink_MatchesOneOfSeveralSpaceSeparatedRelations(string rel)
    {
        // Example from RFC 8288 section 3.5
        var links = LinkHeaderValue.Parse("<http://example.org/>; rel=\"start http://example.net/relation/other\"");
        Assert.Equal("http://example.org/", links.GetLinkUrl(rel));
    }

    [Fact]
    public void GetLink_DoesNotMatchAPartialRelation()
    {
        var links = LinkHeaderValue.Parse("<http://example.org/>; rel=\"start next\"");
        Assert.Null(links.GetLink("star"));
        Assert.Null(links.GetLink("prev"));
    }

    private sealed class CustomHttpHeaders : HttpHeaders
    {
    }
}
