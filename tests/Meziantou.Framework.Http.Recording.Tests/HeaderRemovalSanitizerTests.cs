using Xunit;

namespace Meziantou.Framework.Http.Recording.Tests;

public sealed class HeaderRemovalSanitizerTests
{
    [Fact]
    public void Sanitize_RemovesSpecifiedHeaders()
    {
        var sanitizer = new HeaderRemovalSanitizer("Authorization", "Cookie");
        var entry = new HttpRecordingEntry
        {
            Method = "GET",
            RequestUri = "https://example.com",
            StatusCode = 200,
            RequestHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = ["Bearer token"],
                ["Accept"] = ["application/json"],
                ["Cookie"] = ["session=abc"],
            },
            ResponseHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Set-Cookie"] = ["session=xyz"],
                ["Content-Type"] = ["text/plain"],
            },
        };

        sanitizer.Sanitize(entry);

        Assert.NotNull(entry.RequestHeaders);
        Assert.False(entry.RequestHeaders.ContainsKey("Authorization"));
        Assert.False(entry.RequestHeaders.ContainsKey("Cookie"));
        Assert.True(entry.RequestHeaders.ContainsKey("Accept"));

        Assert.NotNull(entry.ResponseHeaders);
        Assert.True(entry.ResponseHeaders.ContainsKey("Content-Type"));
        Assert.True(entry.ResponseHeaders.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public void Sanitize_CaseInsensitive()
    {
        var sanitizer = new HeaderRemovalSanitizer("authorization");
        var entry = new HttpRecordingEntry
        {
            Method = "GET",
            RequestUri = "https://example.com",
            StatusCode = 200,
            RequestHeaders = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = ["Bearer token"],
            },
        };

        sanitizer.Sanitize(entry);

        Assert.NotNull(entry.RequestHeaders);
        Assert.False(entry.RequestHeaders.ContainsKey("Authorization"));
    }

    [Fact]
    public void Sanitize_NullHeaders_DoesNotThrow()
    {
        var sanitizer = new HeaderRemovalSanitizer("Authorization");
        var entry = new HttpRecordingEntry
        {
            Method = "GET",
            RequestUri = "https://example.com",
            StatusCode = 200,
        };

        sanitizer.Sanitize(entry);
    }
}
