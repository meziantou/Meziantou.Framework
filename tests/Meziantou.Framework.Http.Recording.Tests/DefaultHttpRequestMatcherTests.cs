namespace Meziantou.Framework.Http.Recording.Tests;

public sealed class DefaultHttpRequestMatcherTests
{
    private readonly DefaultHttpRequestMatcher _matcher = DefaultHttpRequestMatcher.Instance;

    [Fact]
    public void SameMethodAndUrl_SameFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api/test", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api/test", StatusCode = 404 };

        Assert.Equal(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void DifferentMethods_DifferentFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api/test", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/api/test", StatusCode = 200 };

        Assert.NotEqual(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void QueryParamOrder_DoesNotAffectFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api?a=1&b=2", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api?b=2&a=1", StatusCode = 200 };

        Assert.Equal(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void DifferentQueryParams_DifferentFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api?a=1", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api?a=2", StatusCode = 200 };

        Assert.NotEqual(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void MethodIsCaseInsensitive()
    {
        var entry1 = new HttpRecordingEntry { Method = "get", RequestUri = "https://example.com/api", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api", StatusCode = 200 };

        Assert.Equal(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void HostIsCaseInsensitive()
    {
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://Example.Com/api", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api", StatusCode = 200 };

        Assert.Equal(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void DifferentPaths_DifferentFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api/a", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api/b", StatusCode = 200 };

        Assert.NotEqual(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void NonDefaultPort_IncludedInFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com:8443/api", StatusCode = 200 };

        Assert.NotEqual(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void NoQueryString_ProducesStableFingerprint()
    {
        var entry = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api", StatusCode = 200 };

        var fp1 = _matcher.ComputeFingerprint(entry);
        var fp2 = _matcher.ComputeFingerprint(entry);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void UserInfo_IsIgnoredInFingerprint()
    {
        // Credentials are stripped when a request is captured, so a hand-written recording that still carries them
        // must match the same request coming through the handler.
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://example.com/api", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://user:password@example.com/api", StatusCode = 200 };

        Assert.Equal(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void DifferentUserInfo_SameFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://user:password1@example.com/api", StatusCode = 200 };
        var entry2 = new HttpRecordingEntry { Method = "GET", RequestUri = "https://user:password2@example.com/api", StatusCode = 200 };

        Assert.Equal(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void DifferentRequestBody_DifferentFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/graphql", StatusCode = 200, RequestBody = """{"query":"getUser"}"""u8.ToArray() };
        var entry2 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/graphql", StatusCode = 200, RequestBody = """{"query":"deleteAll"}"""u8.ToArray() };

        Assert.NotEqual(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void SameRequestBody_SameFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/graphql", StatusCode = 200, RequestBody = "payload"u8.ToArray() };
        var entry2 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/graphql", StatusCode = 200, RequestBody = "payload"u8.ToArray() };

        Assert.Equal(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void EmptyAndMissingRequestBody_SameFingerprint()
    {
        var entry1 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/api", StatusCode = 200, RequestBody = [] };
        var entry2 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/api", StatusCode = 200 };

        Assert.Equal(_matcher.ComputeFingerprint(entry1), _matcher.ComputeFingerprint(entry2));
    }

    [Fact]
    public void IgnoringRequestBody_DifferentBodies_SameFingerprint()
    {
        var matcher = DefaultHttpRequestMatcher.IgnoringRequestBody;
        var entry1 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/api", StatusCode = 200, RequestBody = "a"u8.ToArray() };
        var entry2 = new HttpRecordingEntry { Method = "POST", RequestUri = "https://example.com/api", StatusCode = 200, RequestBody = "b"u8.ToArray() };

        Assert.Equal(matcher.ComputeFingerprint(entry1), matcher.ComputeFingerprint(entry2));
    }
}
