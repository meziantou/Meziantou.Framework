using System.Net;
using Meziantou.Framework.HumanReadable.Converters;

namespace Meziantou.Framework.HumanReadable.Tests;

public sealed class HttpOptionsTests : SerializerTestsBase
{
    private static void AssertSerialization(object value, HumanReadableHttpResponseMessageOptions options, string expected)
    {
        var serializerOptions = new HumanReadableSerializerOptions()
            .AddHttpConverters(new HumanReadableHttpOptions { ResponseMessageOptions = options });

        AssertSerialization(value, serializerOptions, expected);
    }

    private static void AssertSerialization(object value, HumanReadableHttpOptions options, string expected)
    {
        var serializerOptions = new HumanReadableSerializerOptions().AddHttpConverters(options);

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

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RequestMessageFormat = HttpRequestMessageFormat.Full }, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            RequestMessage:
              Method: GET
              RequestUri: http://example.com/foo
              Content: <null>
            """);
    }

    [Fact]
    public void RequestMessage_Full_KeepsNonDefaultProtocolVersion()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
            },
            Content = new StringContent("test"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo")
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            },
        };

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RequestMessageFormat = HttpRequestMessageFormat.Full }, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            RequestMessage:
              Method: GET
              RequestUri: http://example.com/foo
              Version: 2.0
              VersionPolicy: RequestVersionExact
              Content: <null>
            """);
    }

    [Fact]
    public void RequestMessage_Full_ProtocolVersionIsKeptWhenNotOmitted()
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

        var options = new HumanReadableHttpOptions
        {
            RequestMessageOptions = new HumanReadableHttpRequestMessageOptions { OmitProtocolVersion = false },
            ResponseMessageOptions = new HumanReadableHttpResponseMessageOptions { RequestMessageFormat = HttpRequestMessageFormat.Full },
        };

        AssertSerialization(httpContent, options, """
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
    }

    [Fact]
    public void ResponseMessage_ProtocolVersionIsNotHiddenByTheRequestOptions()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Version = HttpVersion.Version20,
            Content = new StringContent("test"),
        };

        var options = new HumanReadableHttpOptions
        {
            RequestMessageOptions = new HumanReadableHttpRequestMessageOptions { OmitProtocolVersion = true },
            ResponseMessageOptions = new HumanReadableHttpResponseMessageOptions { OmitProtocolVersion = false },
        };

        AssertSerialization(httpContent, options, """
            StatusCode: 200 (OK)
            Version: 2.0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
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

    [Fact]
    public void Redact_CSP_Nonce_ReportOnly()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
            },
            Content = new StringContent("test"),
        };
        httpContent.Headers.Add("Content-Security-Policy-Report-Only", "script-src 'nonce-QOlYr5k1Ls3VoNjVQLK5DWFc';");

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RedactContentSecurityPolicyNonce = true }, """
            StatusCode: 200 (OK)
            Headers:
              Content-Security-Policy-Report-Only: script-src 'nonce-[redacted]';
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
    }

    [Fact]
    public void Redact_CSP_Nonce_LeavesOtherHeadersAlone()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Headers =
            {
                Date = DateTimeOffset.UtcNow,
            },
            Content = new StringContent("test"),
        };
        httpContent.Headers.Add("X-Custom", "script-src 'nonce-QOlYr5k1Ls3VoNjVQLK5DWFc';");

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RedactContentSecurityPolicyNonce = true }, """
            StatusCode: 200 (OK)
            Headers:
              X-Custom: script-src 'nonce-QOlYr5k1Ls3VoNjVQLK5DWFc';
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
    }
}
