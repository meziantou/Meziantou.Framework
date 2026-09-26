using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Tests;

public class TestTransformedStringCache
{
    [Fact]
    public void GetRunsTransformationCallback()
    {
        var cache = new TransformedStringCache(static s => "callback-" + s);

        Assert.Equal("callback-foo", cache.Get("foo"));
        Assert.Equal("callback-bar", cache.Get("bar"));
        Assert.Equal("callback-baz", cache.Get("baz"));
    }

    [Fact]
    public void CachesTransformedInstance()
    {
        var cache = new TransformedStringCache(static s => "callback-" + s);

        string transformedBar = cache.Get("bar");
        Assert.Same(transformedBar, cache.Get("bar"));

        string transformedFoo = cache.Get("foo".AsSpan());
        Assert.Same(transformedFoo, cache.Get("foo"));

        Assert.Same(cache.Get("baz"), cache.Get("baz".AsSpan()));

        Assert.Same(transformedBar, cache.Get("bar"));
        Assert.Same(transformedFoo, cache.Get("foo"));
        Assert.Same(transformedBar, cache.Get("bar".AsSpan()));
        Assert.Same(transformedFoo, cache.Get("foo".AsSpan()));
    }

    [Fact]
    public void DoesNotCacheEmptyInputs()
    {
        var cache = new TransformedStringCache(static s => new string('a', 4));

        string cached = cache.Get("");
        string cached2 = cache.Get("");
        string cached3 = cache.Get(ReadOnlySpan<char>.Empty);

        Assert.Equal("aaaa", cached);
        Assert.Equal(cached, cached2);
        Assert.Equal(cached, cached3);

        Assert.NotSame(cached, cached2);
        Assert.NotSame(cached, cached3);
        Assert.NotSame(cached2, cached3);
    }

    [Theory]
    [InlineData(TransformedStringCache.InputLengthLimit, true)]
    [InlineData(TransformedStringCache.InputLengthLimit + 1, false)]
    public void DoesNotCacheLongInputs(int length, bool shouldBeCached)
    {
        var cache = new TransformedStringCache(static s => "callback-" + s);

        string input = new string('a', length);

        string cached = cache.Get(input);
        string cached2 = cache.Get(input);

        Assert.Equal("callback-" + input, cached);
        Assert.Equal(cached, cached2);

        if (shouldBeCached)
        {
            Assert.Same(cached, cached2);
        }
        else
        {
            Assert.NotSame(cached, cached2);
        }
    }

    [Fact]
    public void CachesAtMostNEntriesPerCharacter()
    {
        var cache = new TransformedStringCache(static s => "callback-" + s);

        int limit = TransformedStringCache.MaxEntriesPerCharacter;

        string[] a = Enumerable.Range(1, limit + 1).Select(i => $"a{i}").ToArray();
        string[] cachedAs = a.Select(a => cache.Get(a)).ToArray();

        for (int i = 0; i < limit; i++)
        {
            Assert.Same(cachedAs[i], cache.Get(a[i]));
        }

        Assert.NotSame(cachedAs[limit], cache.Get(a[limit]));

        Assert.Same(cache.Get("b1"), cache.Get("b1"));
    }
}
