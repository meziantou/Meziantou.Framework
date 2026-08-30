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

    [Fact]
    public void Parse_ThrowsArgumentNullExceptionOnNullArguments()
    {
        Assert.Equal("httpResponse", Assert.Throws<ArgumentNullException>(() => LinkHeaderValue.Parse((HttpResponseMessage)null!)).ParamName);
        Assert.Equal("headers", Assert.Throws<ArgumentNullException>(() => LinkHeaderValue.Parse((HttpHeaders)null!)).ParamName);
        Assert.Equal("value", Assert.Throws<ArgumentNullException>(() => LinkHeaderValue.Parse((string)null!)).ParamName);
    }

    [Fact]
    public void Parse_ReadsLinkHeadersFromAResponseMessage()
    {
        using var response = new HttpResponseMessage();
        response.Headers.Add("Link", "<https://example.com/2>; rel=\"next\"");

        Assert.Equal("https://example.com/2", LinkHeaderValue.Parse(response).GetLinkUrl("next"));
    }

    private sealed class CustomHttpHeaders : HttpHeaders
    {
    }
}
