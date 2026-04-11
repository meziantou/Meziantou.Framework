using System.Net;
using Xunit;

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
}
