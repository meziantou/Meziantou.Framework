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
        Assert.DoesNotContain("Authorization", entry.RequestHeaders);
        Assert.DoesNotContain("Cookie", entry.RequestHeaders);
        Assert.Contains("Accept", entry.RequestHeaders);

        Assert.NotNull(entry.ResponseHeaders);
        Assert.Contains("Content-Type", entry.ResponseHeaders);
        Assert.Contains("Set-Cookie", entry.ResponseHeaders);
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
        Assert.DoesNotContain("Authorization", entry.RequestHeaders);
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
