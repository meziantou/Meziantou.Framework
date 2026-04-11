using Xunit;

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
}
