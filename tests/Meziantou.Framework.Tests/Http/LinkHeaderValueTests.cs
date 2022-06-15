using System.Net.Http.Headers;
using FluentAssertions;
using Meziantou.Framework.Http;
using Xunit;

namespace Meziantou.Framework.Tests.Http;

public sealed class LinkHeaderValueTests
{
    [Fact]
    public void LinkHeaderValue_Parse()
    {
        var result = LinkHeaderValue.Parse("<sample>; rel=abc, <plop>; rel\t=\"d\\\"e;f,\"; title = test title; abc");
        result.Should().SatisfyRespectively(
            item =>
            {
                item.Url.Should().Be("sample");
                item.Rel.Should().Be("abc");
            },
            item =>
            {
                item.Url.Should().Be("plop");
                item.Rel.Should().Be("d\"e;f,");
                item.GetParameterValue("title").Should().Be("test title");
                item.GetParameterValue("abc").Should().BeEmpty();
                item.GetParameterValue("unknown").Should().BeNull();
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

        header.ParseLinkHeaders().Should().HaveCount(3);
    }

    private sealed class CustomHttpHeaders : HttpHeaders
    {
    }
}
