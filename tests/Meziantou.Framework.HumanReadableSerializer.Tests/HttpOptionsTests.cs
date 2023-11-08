using Meziantou.Framework.HumanReadable.Converters;
using Xunit;

namespace Meziantou.Framework.HumanReadable.Tests;

public sealed class HttpOptionsTests : SerializerTestsBase
{
    private static void AssertSerialization(object value, HumanReadableHttpResponseMessageOptions options, string expected)
    {
        var serializerOptions = new HumanReadableSerializerOptions()
            .AddHttpConverters(new HumanReadableHttpOptions { ResponseMessageOptions = options });

        AssertSerialization(value, serializerOptions, expected);
    }

    [Fact]
    public void RequestMessage_Full()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
            },
            Content = new StringContent("test"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo"),
        };

#if NET472
        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RequestMessageFormat = HttpRequestMessageFormat.Full }, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            RequestMessage:
              Method: GET
              RequestUri: http://example.com/foo
              Version: 1.1
              Content: <null>
            """);
#else
        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RequestMessageFormat = HttpRequestMessageFormat.Full }, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            RequestMessage:
              Method: GET
              RequestUri: http://example.com/foo
              Version: 1.1
              VersionPolicy: RequestVersionOrLower
              Content: <null>
            """);
#endif
    }
    [Fact]
    public void RequestMessage_Uri()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
            },
            Content = new StringContent("test"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo"),
        };

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RequestMessageFormat = HttpRequestMessageFormat.MethodAndUri }, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            RequestMessage: GET http://example.com/foo
            """);
    }

    [Fact]
    public void RequestMessage_NotSerialized()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
            },
            Content = new StringContent("test"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo"),
        };

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { }, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
    }

    [Fact]
    public void RemoveEmptyHeaders()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
            },
            Content = new StringContent("test"),
        };

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { }, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
    }

    [Fact]
    public void RemoveExcludedHeaders()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
                Location = new Uri("http://example.com"),
            },
            Content = new StringContent("test"),
        };

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { }, """
            StatusCode: 200 (OK)
            Headers:
              Location: http://example.com/
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
    }

    [Fact]
    public void Redact_CSP_Nonce()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
                Location = new Uri("http://example.com"),
            },
            Content = new StringContent("test"),
        };
        httpContent.Headers.Add("Content-Security-Policy", "default-src 'self';style-src 'self' 'nonce-QOlYr5k1Ls3VoNjVQLK5DWFc';script-src 'nonce-QOlYr5k1Ls3VoNjVQLK5DWFc';");

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RedactContentSecurityPolicyNonce = true }, """
            StatusCode: 200 (OK)
            Headers:
              Location: http://example.com/
              Content-Security-Policy: default-src 'self';style-src 'self' 'nonce-[redacted]';script-src 'nonce-[redacted]';
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
    }
}
