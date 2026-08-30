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
