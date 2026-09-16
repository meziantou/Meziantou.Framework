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
    public void RequestHeadersAreSerializedWithoutExclusionsOrTransformers()
    {
        using var httpContent = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo")
        {
            Headers =
            {
                { "X-Custom", "value" },
            },
        };

        // OmitProtocolVersion is pinned so the expectation does not depend on how the
        // protocol-version properties are filtered.
        var httpOptions = new HumanReadableHttpOptions
        {
            RequestMessageOptions = new HumanReadableHttpRequestMessageOptions { OmitProtocolVersion = false },
        };
        var serializerOptions = new HumanReadableSerializerOptions().AddHttpConverters(httpOptions);

        AssertSerialization(httpContent, serializerOptions, """
            Method: GET
            RequestUri: http://example.com/foo
            Version: 1.1
            VersionPolicy: RequestVersionOrLower
            Headers:
              X-Custom: value
            Content: <null>
            """);
    }

    [Fact]
    public void RequestHeadersAreExcludedWhenConfigured()
    {
        using var httpContent = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo")
        {
            Headers =
            {
                { "X-Custom", "value" },
                { "X-Secret", "value" },
            },
        };

        var httpOptions = new HumanReadableHttpOptions
        {
            RequestMessageOptions = new HumanReadableHttpRequestMessageOptions { OmitProtocolVersion = false },
        };
        httpOptions.RequestMessageOptions.ExcludedHeaderNames.Add("X-Secret");
        var serializerOptions = new HumanReadableSerializerOptions().AddHttpConverters(httpOptions);

        AssertSerialization(httpContent, serializerOptions, """
            Method: GET
            RequestUri: http://example.com/foo
            Version: 1.1
            VersionPolicy: RequestVersionOrLower
            Headers:
              X-Custom: value
            Content: <null>
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

    [Fact]
    public void AddHttpConverters_LastCallReplacesThePreviousOnes()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Content = new StringContent("test"),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo"),
        };
        httpContent.Headers.Add("Server", "Kestrel");
        httpContent.Headers.Add("Content-Security-Policy", "script-src 'nonce-abc'");

        var serializerOptions = new HumanReadableSerializerOptions().AddHttpConverters(new HumanReadableHttpOptions());
        var httpOptions = new HumanReadableHttpOptions();
        httpOptions.ResponseMessageOptions.RequestMessageFormat = HttpRequestMessageFormat.MethodAndUri;
        httpOptions.ResponseMessageOptions.RedactContentSecurityPolicyNonce = true;
        httpOptions.ResponseMessageOptions.ExcludedHeaderNames.Add("Server");
        serializerOptions.AddHttpConverters(httpOptions);

        AssertSerialization(httpContent, serializerOptions, """
            StatusCode: 200 (OK)
            Headers:
              Content-Security-Policy: script-src 'nonce-[redacted]'
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            RequestMessage: GET http://example.com/foo
            """);
    }

    [Fact]
    public void AddHttpConverters_LastCallCanRemoveExclusions()
    {
        using var httpContent = new HttpResponseMessage();
        httpContent.Headers.Add("Server", "Kestrel");

        var firstOptions = new HumanReadableHttpOptions();
        firstOptions.ResponseMessageOptions.ExcludedHeaderNames.Add("Server");
        var serializerOptions = new HumanReadableSerializerOptions().AddHttpConverters(firstOptions);
        serializerOptions.AddHttpConverters(new HumanReadableHttpOptions());

        AssertSerialization(httpContent, serializerOptions, """
            StatusCode: 200 (OK)
            Headers:
              Server: Kestrel
            Content:
            """);
    }

    [Fact]
    public void AddHttpConverters_KeepsAttributesAddedByTheUser()
    {
        using var httpContent = new HttpResponseMessage() { Content = new StringContent("test") };

        var serializerOptions = new HumanReadableSerializerOptions().AddHttpConverters(new HumanReadableHttpOptions());
        serializerOptions.IgnoreMember<HttpResponseMessage>(message => message.StatusCode);
        serializerOptions.AddHttpConverters(new HumanReadableHttpOptions());

        AssertSerialization(httpContent, serializerOptions, """
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
    }

    [Fact]
    public void AddHttpConverters_ChangesMadeAfterTheCallHaveNoEffect()
    {
        using var httpContent = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        httpContent.Headers.Add("X-Secret", "1");

        var httpOptions = new HumanReadableHttpOptions();
        var serializerOptions = new HumanReadableSerializerOptions().AddHttpConverters(httpOptions);
        httpOptions.RequestMessageOptions.ExcludedHeaderNames.Add("X-Secret");

        AssertSerialization(httpContent, serializerOptions, """
            Method: GET
            RequestUri: http://example.com/
            Headers:
              X-Secret: 1
            Content: <null>
            """);
    }

    [Fact]
    public void AddHttpConverters_ReadOnlyOptions()
    {
        var serializerOptions = new HumanReadableSerializerOptions();
        serializerOptions.MakeReadOnly();

        Assert.Throws<InvalidOperationException>(() => serializerOptions.AddHttpConverters(new HumanReadableHttpOptions()));
    }

    [Fact]
    public void ContentHeaders_ExcludedAndTransformed()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Content = new StringContent("test"),
        };
        httpContent.Content.Headers.LastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        httpContent.Content.Headers.TryAddWithoutValidation("X-Signature", "secret");
        httpContent.Content.Headers.TryAddWithoutValidation("X-Other", "value");

        var options = new HumanReadableHttpResponseMessageOptions();
        options.ExcludedHeaderNames.Add("Last-Modified");
        options.ExcludedHeaderNames.Add("X-Signature");
        options.HeaderValueTransformer.Add(new UpperCaseHeaderValueFormatter());

        AssertSerialization(httpContent, options, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: TEXT/PLAIN; CHARSET=UTF-8
                X-Other: VALUE
              Value: test
            """);
    }

    [Fact]
    public void ContentHeaders_AllExcluded()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Content = new StringContent("test"),
        };

        var options = new HumanReadableHttpResponseMessageOptions();
        options.ExcludedHeaderNames.Add("Content-Type");

        AssertSerialization(httpContent, options, """
            StatusCode: 200 (OK)
            Content:
              test
            """);
    }

    [Fact]
    public void ContentHeaders_RequestContent()
    {
        using var httpContent = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("test"),
        };
        httpContent.Content.Headers.TryAddWithoutValidation("X-Signature", "secret");

        var httpOptions = new HumanReadableHttpOptions();
        httpOptions.RequestMessageOptions.ExcludedHeaderNames.Add("X-Signature");

        AssertSerialization(httpContent, httpOptions, """
            Method: POST
            RequestUri: http://example.com/
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
              Value: test
            """);
    }

    [Fact]
    public void ContentHeaders_MultipartParts()
    {
        using var part = new StringContent("a");
        part.Headers.Add("X-Signature", "secret");
        using var httpContent = new HttpResponseMessage()
        {
            Content = new MultipartContent("mixed") { part },
        };

        var options = new HumanReadableHttpResponseMessageOptions();
        options.ExcludedHeaderNames.Add("X-Signature");

        AssertSerialization(httpContent, options, """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: multipart/mixed
              Value:
                - Headers:
                    Content-Type: text/plain; charset=utf-8
                  Value: a
            """);
    }

    [Fact]
    public void Content_ConverterRegisteredByTheUserTakesPrecedence()
    {
        using var httpContent = new HttpResponseMessage()
        {
            Content = new StringContent("test"),
        };

        var serializerOptions = new HumanReadableSerializerOptions();
        serializerOptions.Converters.Add(new StringContentConverter());
        serializerOptions.AddHttpConverters(new HumanReadableHttpOptions());

        AssertSerialization(httpContent, serializerOptions, """
            StatusCode: 200 (OK)
            Content: custom
            """);
    }

    [Fact]
    public void TrailingHeaders_Excluded()
    {
        using var httpContent = new HttpResponseMessage();
        httpContent.TrailingHeaders.Add("X-Checksum", "abc");
        httpContent.TrailingHeaders.Add("X-Other", "value");

        var options = new HumanReadableHttpResponseMessageOptions();
        options.ExcludedHeaderNames.Add("X-Checksum");

        AssertSerialization(httpContent, options, """
            StatusCode: 200 (OK)
            TrailingHeaders:
              X-Other: value
            Content:
            """);
    }

    [Fact]
    public void HeaderValueTransformer_ResponseHeaders()
    {
        using var httpContent = new HttpResponseMessage();
        httpContent.Headers.Add("X-Custom", "value");

        var options = new HumanReadableHttpResponseMessageOptions();
        options.HeaderValueTransformer.Add(new UpperCaseHeaderValueFormatter());

        AssertSerialization(httpContent, options, """
            StatusCode: 200 (OK)
            Headers:
              X-Custom: VALUE
            Content:
            """);
    }

    [Fact]
    public void RequestMessage_ProtocolVersionIsKeptWhenNotOmitted_WhenWritingDefault()
    {
        using var httpContent = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo");

        var serializerOptions = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault }
            .AddHttpConverters(new HumanReadableHttpOptions { RequestMessageOptions = new HumanReadableHttpRequestMessageOptions { OmitProtocolVersion = false } });

        AssertSerialization(httpContent, serializerOptions, """
            Method: GET
            RequestUri: http://example.com/foo
            Version: 1.1
            """);
    }

    [Fact]
    public void RequestMessage_Uri_KeepsEscapedCharacters()
    {
        using var httpContent = new HttpResponseMessage()
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a%20b?q=caf%C3%A9"),
        };

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RequestMessageFormat = HttpRequestMessageFormat.MethodAndUri }, """
            StatusCode: 200 (OK)
            Content:
            RequestMessage: GET http://example.com/a%20b?q=caf%C3%A9
            """);
    }

    [Fact]
    public void RequestMessage_Uri_NullUri()
    {
        using var httpContent = new HttpResponseMessage()
        {
            RequestMessage = new HttpRequestMessage(),
        };

        AssertSerialization(httpContent, new HumanReadableHttpResponseMessageOptions { RequestMessageFormat = HttpRequestMessageFormat.MethodAndUri }, """
            StatusCode: 200 (OK)
            Content:
            RequestMessage: GET
            """);
    }

    [Fact]
    public void MessageOptions_CopyDoesNotShareCollections()
    {
        var options = new HumanReadableHttpResponseMessageOptions();
        options.HeaderValueTransformer.Add(new UpperCaseHeaderValueFormatter());

        var copy = options with { RedactContentSecurityPolicyNonce = true };
        copy.ExcludedHeaderNames.Add("Server");
        copy.HeaderValueTransformer.Clear();

        Assert.DoesNotContain("Server", options.ExcludedHeaderNames);
        Assert.Contains("Date", copy.ExcludedHeaderNames);
        Assert.Contains("date", copy.ExcludedHeaderNames);
        Assert.Single(options.HeaderValueTransformer);
        Assert.True(copy.RedactContentSecurityPolicyNonce);
        Assert.Equal(options.OmitProtocolVersion, copy.OmitProtocolVersion);
    }

    [Fact]
    public void MessageOptions_Equality()
    {
        var options1 = new HumanReadableHttpResponseMessageOptions();
        var options2 = new HumanReadableHttpResponseMessageOptions();
        Assert.Equal(options1, options2);
        Assert.Equal(options1.GetHashCode(), options2.GetHashCode());

        options2.ExcludedHeaderNames.Add("Server");
        Assert.NotEqual(options1, options2);

        Assert.NotEqual<HumanReadableHttpMessageOptions>(new HumanReadableHttpRequestMessageOptions(), new HumanReadableHttpResponseMessageOptions());
    }

    private sealed class UpperCaseHeaderValueFormatter : HttpHeaderValueFormatter
    {
        public override string FormatHeaderValue(string headerName, string headerValue) => headerValue.ToUpperInvariant();
    }

    private sealed class StringContentConverter : HumanReadableConverter<StringContent>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, StringContent? value, HumanReadableSerializerOptions options) => writer.WriteValue("custom");
    }
}
