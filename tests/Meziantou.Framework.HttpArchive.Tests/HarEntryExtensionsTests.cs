using System.Net;
using System.Text.Json;

namespace Meziantou.Framework.HttpArchive.Tests;

public sealed class HarEntryExtensionsTests
{
    [Fact]
    public void ToHttpRequestMessage_GetRequest()
    {
        var request = new HarRequest
        {
            Method = "GET",
            Url = "https://example.com/api/data?page=1",
            HttpVersion = "HTTP/1.1",
            Headers =
            [
                new HarHeader { Name = "Host", Value = "example.com" },
                new HarHeader { Name = "Accept", Value = "application/json" },
            ],
        };

        using var message = request.ToHttpRequestMessage();

        Assert.Equal(HttpMethod.Get, message.Method);
        Assert.Equal(new Uri("https://example.com/api/data?page=1"), message.RequestUri);
        Assert.Equal(new Version(1, 1), message.Version);
        Assert.Contains("application/json", message.Headers.GetValues("Accept"));
    }

    [Fact]
    public async Task ToHttpRequestMessage_PostWithBody()
    {
        var request = new HarRequest
        {
            Method = "POST",
            Url = "https://example.com/api/data",
            HttpVersion = "HTTP/2",
            Headers =
            [
                new HarHeader { Name = "Content-Type", Value = "application/json" },
            ],
            PostData = new HarPostData
            {
                MimeType = "application/json",
                Text = "{\"name\":\"test\"}",
            },
        };

        using var message = request.ToHttpRequestMessage();

        Assert.Equal(HttpMethod.Post, message.Method);
        Assert.Equal(new Version(2, 0), message.Version);
        Assert.NotNull(message.Content);
        var body = await message.Content.ReadAsStringAsync();
        Assert.Equal("{\"name\":\"test\"}", body);
    }

    [Fact]
    public void ToHttpResponseMessage_Basic()
    {
        var response = new HarResponse
        {
            Status = 200,
            StatusText = "OK",
            HttpVersion = "HTTP/1.1",
            Headers =
            [
                new HarHeader { Name = "X-Custom", Value = "test-value" },
                new HarHeader { Name = "Content-Type", Value = "text/html" },
            ],
            Content = new HarContent
            {
                Size = 13,
                MimeType = "text/html",
                Text = "<h1>Hello</h1>",
            },
        };

        using var message = response.ToHttpResponseMessage();

        Assert.Equal(HttpStatusCode.OK, message.StatusCode);
        Assert.Equal("OK", message.ReasonPhrase);
        Assert.Equal(new Version(1, 1), message.Version);
        Assert.Contains("test-value", message.Headers.GetValues("X-Custom"));
    }

    [Fact]
    public async Task ToHttpResponseMessage_Base64Content()
    {
        var binaryData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var base64 = Convert.ToBase64String(binaryData);

        var response = new HarResponse
        {
            Status = 200,
            StatusText = "OK",
            HttpVersion = "HTTP/1.1",
            Content = new HarContent
            {
                Size = binaryData.Length,
                MimeType = "image/png",
                Text = base64,
                Encoding = "base64",
            },
        };

        using var message = response.ToHttpResponseMessage();

        Assert.NotNull(message.Content);
        var content = await message.Content.ReadAsByteArrayAsync();
        Assert.Equal(binaryData, content);
    }

    [Theory]
    [InlineData("HTTP/1.0", 1, 0)]
    [InlineData("HTTP/1.1", 1, 1)]
    [InlineData("HTTP/2", 2, 0)]
    [InlineData("HTTP/2.0", 2, 0)]
    [InlineData("h2", 2, 0)]
    [InlineData("h2c", 2, 0)]
    [InlineData("HTTP/3", 3, 0)]
    [InlineData("HTTP/3.0", 3, 0)]
    [InlineData("h3", 3, 0)]
    [InlineData("unknown", 1, 1)]
    public void HttpVersionMapping(string httpVersion, int expectedMajor, int expectedMinor)
    {
        var request = new HarRequest
        {
            Method = "GET",
            Url = "https://example.com",
            HttpVersion = httpVersion,
        };

        using var message = request.ToHttpRequestMessage();

        Assert.Equal(new Version(expectedMajor, expectedMinor), message.Version);
    }

    [Fact]
    public void ToHttpRequestMessage_FromEntry()
    {
        var entry = new HarEntry
        {
            Request = new HarRequest
            {
                Method = "GET",
                Url = "https://example.com",
                HttpVersion = "HTTP/1.1",
            },
        };

        using var message = entry.ToHttpRequestMessage();

        Assert.Equal(HttpMethod.Get, message.Method);
    }

    [Fact]
    public void ToHttpResponseMessage_FromEntry()
    {
        var entry = new HarEntry
        {
            Response = new HarResponse
            {
                Status = 404,
                StatusText = "Not Found",
                HttpVersion = "HTTP/1.1",
                Content = new HarContent
                {
                    MimeType = "text/plain",
                    Text = "Not Found",
                },
            },
        };

        using var message = entry.ToHttpResponseMessage();

        Assert.Equal(HttpStatusCode.NotFound, message.StatusCode);
    }

    [Fact]
    public void PostData_TryGetRawData_InvalidBase64()
    {
        var postData = new HarPostData
        {
            Text = "not!valid!base64",
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [HarPostDataExtensions.DefaultEncodingExtensionName] = JsonDocument.Parse("\"base64\"").RootElement.Clone(),
            },
        };

        Assert.False(postData.TryGetRawData(out var rawData));
        Assert.Null(rawData);
    }

    [Fact]
    public void PostData_TryGetRawData_UnknownEncoding()
    {
        var postData = new HarPostData
        {
            Text = "48656c6c6f",
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [HarPostDataExtensions.DefaultEncodingExtensionName] = JsonDocument.Parse("\"hex\"").RootElement.Clone(),
            },
        };

        Assert.False(postData.TryGetRawData(out var rawData));
        Assert.Null(rawData);
    }

    [Fact]
    public void PostData_TryGetRawData_CustomEncodingExtensionName()
    {
        var binaryData = new byte[] { 0x00, 0xFF };
        var postData = new HarPostData
        {
            Text = Convert.ToBase64String(binaryData),
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["_encoding"] = JsonDocument.Parse("\"base64\"").RootElement.Clone(),
            },
        };

        Assert.True(postData.TryGetRawData(out var rawData, "_encoding"));
        Assert.Equal(binaryData, rawData);
    }

    [Fact]
    public void PostData_TryGetRawData_Utf8()
    {
        var postData = new HarPostData
        {
            Text = "test",
        };

        Assert.True(postData.TryGetRawData(out var rawData));
        Assert.Equal("test"u8.ToArray(), rawData);
    }

    [Fact]
    public void PostData_TryGetRawData_Base64EncodingExtension()
    {
        var binaryData = new byte[] { 0x00, 0x01, 0x7F, 0x80, 0xFF };
        var postData = new HarPostData
        {
            Text = Convert.ToBase64String(binaryData),
            ExtensionData = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [HarPostDataExtensions.DefaultEncodingExtensionName] = JsonDocument.Parse("\"base64\"").RootElement.Clone(),
            },
        };

        Assert.True(postData.TryGetRawData(out var rawData));
        Assert.Equal(binaryData, rawData);
    }

    [Fact]
    public void PostData_TryGetRawData_NoText()
    {
        var postData = new HarPostData();

        Assert.False(postData.TryGetRawData(out var rawData));
        Assert.Null(rawData);
    }

    [Fact]
    public void RealWorldHar_EveryEntryConvertsWithoutThrowing()
    {
        var document = LoadChromeHar();

        Assert.HasCount(6, document.Log.Entries);

        foreach (var entry in document.Log.Entries)
        {
            using var request = entry.ToHttpRequestMessage();
            using var response = entry.ToHttpResponseMessage();

            Assert.NotNull(request.RequestUri);
            Assert.NotNull(response.Content);
        }
    }

    [Fact]
    public void RealWorldHar_ContentTypeIsNeverDuplicated()
    {
        var document = LoadChromeHar();

        foreach (var entry in document.Log.Entries)
        {
            using var response = entry.ToHttpResponseMessage();
            if (response.Content.Headers.TryGetValues("Content-Type", out var values))
            {
                Assert.Single(values);
            }
        }
    }

    [Fact]
    public void ToHttpResponseMessage_MimeTypeWithCharset()
    {
        var response = new HarResponse
        {
            Status = 200,
            HttpVersion = "http/2.0",
            Content = new HarContent { MimeType = "text/html; charset=utf-8", Text = "<h1>Hi</h1>" },
        };

        using var message = response.ToHttpResponseMessage();

        Assert.Equal("text/html; charset=utf-8", Assert.Single(message.Content.Headers.GetValues("Content-Type")));
    }

    [Fact]
    public void ToHttpResponseMessage_EmptyMimeType()
    {
        var response = new HarResponse
        {
            Status = 304,
            HttpVersion = "http/2.0",
            Content = new HarContent { MimeType = "" },
        };

        using var message = response.ToHttpResponseMessage();

        Assert.False(message.Content.Headers.Contains("Content-Type"));
    }

    [Fact]
    public void ToHttpResponseMessage_HeaderWinsOverMimeType()
    {
        var response = new HarResponse
        {
            Status = 200,
            HttpVersion = "http/2.0",
            Headers = [new HarHeader { Name = "Content-Type", Value = "application/json; charset=utf-8" }],
            Content = new HarContent { MimeType = "application/json", Text = "{}" },
        };

        using var message = response.ToHttpResponseMessage();

        Assert.Equal("application/json; charset=utf-8", Assert.Single(message.Content.Headers.GetValues("Content-Type")));
    }

    [Fact]
    public async Task ToHttpResponseMessage_InvalidBase64FallsBackToRawText()
    {
        var response = new HarResponse
        {
            Status = 200,
            HttpVersion = "http/2.0",
            Content = new HarContent { MimeType = "image/png", Text = "not!valid!base64", Encoding = "base64" },
        };

        using var message = response.ToHttpResponseMessage();

        Assert.Equal("not!valid!base64", await message.Content.ReadAsStringAsync());
    }

    [Fact]
    public void ToHttpRequestMessage_PostDataMimeTypeWithCharset()
    {
        var request = new HarRequest
        {
            Method = "POST",
            Url = "https://example.com/login",
            HttpVersion = "http/2.0",
            PostData = new HarPostData { MimeType = "application/x-www-form-urlencoded;charset=UTF-8", Text = "a=1" },
        };

        using var message = request.ToHttpRequestMessage();

        Assert.NotNull(message.Content);
        Assert.Single(message.Content.Headers.GetValues("Content-Type"));
        Assert.Equal("application/x-www-form-urlencoded", message.Content.Headers.ContentType?.MediaType);
        Assert.Equal("UTF-8", message.Content.Headers.ContentType?.CharSet);
    }

    [Fact]
    public void ToHttpResponseMessage_DoesNotReplayRecordedContentLength()
    {
        var response = new HarResponse
        {
            Status = 200,
            HttpVersion = "http/2.0",
            Headers =
            [
                new HarHeader { Name = "Content-Length", Value = "648" },
                new HarHeader { Name = "Content-Type", Value = "text/plain" },
            ],
            Content = new HarContent { MimeType = "text/plain", Text = "hello" },
        };

        using var message = response.ToHttpResponseMessage();

        Assert.Equal(5, message.Content.Headers.ContentLength);
        Assert.Equal("5", Assert.Single(message.Content.Headers.GetValues("Content-Length")));
    }

    [Fact]
    public void ToHttpResponseMessage_DoesNotReplayContentEncoding()
    {
        var response = new HarResponse
        {
            Status = 200,
            HttpVersion = "http/2.0",
            Headers = [new HarHeader { Name = "Content-Encoding", Value = "gzip" }],
            Content = new HarContent { MimeType = "text/plain", Text = "already decoded" },
        };

        using var message = response.ToHttpResponseMessage();

        Assert.False(message.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact]
    public async Task RealWorldHar_GzippedEntryHasConsistentContentLength()
    {
        var document = LoadChromeHar();
        var entry = document.Log.Entries[0];

        Assert.Equal("648", entry.Response.Headers.Single(h => string.Equals(h.Name, "content-length", StringComparison.OrdinalIgnoreCase)).Value);

        using var message = entry.ToHttpResponseMessage();
        var body = await message.Content.ReadAsByteArrayAsync();

        Assert.Equal(body.Length, message.Content.Headers.ContentLength);
        Assert.False(message.Content.Headers.Contains("Content-Encoding"));
    }

    [Fact]
    public async Task ToHttpRequestMessage_RecordedContentLengthDoesNotBreakSending()
    {
        var request = new HarRequest
        {
            Method = "POST",
            Url = "https://example.com/upload",
            HttpVersion = "http/1.1",
            Headers = [new HarHeader { Name = "Content-Length", Value = "99999" }],
            PostData = new HarPostData { MimeType = "text/plain", Text = "abc" },
        };

        using var message = request.ToHttpRequestMessage();

        Assert.NotNull(message.Content);
        var body = await message.Content.ReadAsByteArrayAsync();
        Assert.Equal(body.Length, message.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task ToHttpRequestMessage_RebuildsBodyFromParams()
    {
        var request = new HarRequest
        {
            Method = "POST",
            Url = "https://example.com/login",
            HttpVersion = "http/2.0",
            PostData = new HarPostData
            {
                MimeType = "application/x-www-form-urlencoded;charset=UTF-8",
                Params =
                [
                    new HarPostDataParameter { Name = "username", Value = "someone%40example.com" },
                    new HarPostDataParameter { Name = "remember", Value = "on" },
                ],
            },
        };

        using var message = request.ToHttpRequestMessage();

        Assert.NotNull(message.Content);
        Assert.Equal("username=someone%40example.com&remember=on", await message.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ToHttpRequestMessage_TextWinsOverParams()
    {
        var request = new HarRequest
        {
            Method = "POST",
            Url = "https://example.com/login",
            HttpVersion = "http/2.0",
            PostData = new HarPostData
            {
                MimeType = "application/x-www-form-urlencoded",
                Text = "a=1&b=2",
                Params = [new HarPostDataParameter { Name = "ignored", Value = "x" }],
            },
        };

        using var message = request.ToHttpRequestMessage();

        Assert.NotNull(message.Content);
        Assert.Equal("a=1&b=2", await message.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ToHttpRequestMessage_MultipartParamsProduceAnEmptyBody()
    {
        var request = new HarRequest
        {
            Method = "POST",
            Url = "https://example.com/upload",
            HttpVersion = "http/2.0",
            PostData = new HarPostData
            {
                MimeType = "multipart/form-data; boundary=----WebKitFormBoundaryABC",
                Params = [new HarPostDataParameter { Name = "file", FileName = "a.txt", ContentType = "text/plain" }],
            },
        };

        using var message = request.ToHttpRequestMessage();

        Assert.NotNull(message.Content);
        Assert.Empty(await message.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public void ToHttpRequestMessage_KeepsContentHeadersWhenBodyWasNotCaptured()
    {
        var request = new HarRequest
        {
            Method = "POST",
            Url = "https://example.com/upload",
            HttpVersion = "http/2.0",
            Headers = [new HarHeader { Name = "Content-Type", Value = "application/octet-stream" }],
        };

        using var message = request.ToHttpRequestMessage();

        Assert.NotNull(message.Content);
        Assert.Equal("application/octet-stream", message.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void ToHttpRequestMessage_NoContentHeadersLeavesContentNull()
    {
        var request = new HarRequest
        {
            Method = "GET",
            Url = "https://example.com/",
            HttpVersion = "http/2.0",
            Headers = [new HarHeader { Name = "Accept", Value = "*/*" }],
        };

        using var message = request.ToHttpRequestMessage();

        Assert.Null(message.Content);
    }

    [Fact]
    public async Task RealWorldHar_FormPostKeepsItsBody()
    {
        var document = LoadChromeHar();
        var entry = document.Log.Entries.Single(e => e.Request.Url.EndsWith("/login", StringComparison.Ordinal));

        Assert.Null(entry.Request.PostData!.Text);

        using var message = entry.ToHttpRequestMessage();

        Assert.NotNull(message.Content);
        Assert.Equal("username=someone%40example.com&password=redacted&remember=on", await message.Content.ReadAsStringAsync());
    }

    private static HarDocument LoadChromeHar()
    {
        using var stream = typeof(HarEntryExtensionsTests).Assembly.GetManifestResourceStream("files/chrome-devtools.har")!;
        return HarDocument.Parse(stream);
    }
}
