using Xunit;

namespace Meziantou.Framework.Tests;
public sealed class DictionaryExtensionsTests
{
    [Fact]
    public void GetOrAddTest()
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        var value = dict.GetOrAdd("key", 42);
        Assert.Equal(42, value);
        Assert.Equal(42, dict["key"]);

        value = dict.GetOrAdd("key", 43);
        Assert.Equal(42, value);
        Assert.Equal(42, dict["key"]);
    }

    [Fact]
    public void GetOrAddFactoryTest()
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        var value = dict.GetOrAdd("key", _ => 42);
        Assert.Equal(42, value);
        Assert.Equal(42, dict["key"]);

        value = dict.GetOrAdd("key", _ => 43);
        Assert.Equal(42, value);
        Assert.Equal(42, dict["key"]);
    }

    [Fact]
    public void TryUpdateTest()
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        dict["key"] = 42;
        var result = dict.TryUpdate("key", 43);
        Assert.True(result);
        Assert.Equal(43, dict["key"]);
        result = dict.TryUpdate("key2", 43);
        Assert.False(result);
        Assert.Equal(43, dict["key"]);
    }

    [Fact]
    public void TryUpdateFactoryTest()
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        dict["key"] = 42;
        var result = dict.TryUpdate("key", (_, value) => value + 1);
        Assert.True(result);
        Assert.Equal(43, dict["key"]);

        result = dict.TryUpdate("key2", (_, value) => value + 1);
        Assert.False(result);
        Assert.False(dict.TryGetValue("key2", out _));
        Assert.Equal(43, dict["key"]);
    }
}
