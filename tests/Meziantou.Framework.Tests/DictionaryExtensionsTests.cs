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

    [Fact]
    public void GetOrAddFactory_ThrowingFactoryDoesNotAddTheKey()
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        Assert.Throws<InvalidOperationException>(() => dict.GetOrAdd("key", _ => throw new InvalidOperationException()));
        Assert.False(dict.ContainsKey("key"));

        Assert.Equal("recovered", dict.GetOrAdd("key", _ => "recovered"));
        Assert.Equal("recovered", dict["key"]);
    }

    [Fact]
    public void GetOrAddFactory_StoresTheValueWhenTheFactoryMutatesTheDictionary()
    {
        var dict = new Dictionary<int, string>();

        var result = dict.GetOrAdd(0, _ =>
        {
            for (var i = 1; i <= 64; i++)
            {
                dict[i] = "filler";
            }

            return "computed";
        });

        Assert.Equal("computed", result);
        Assert.Equal("computed", dict[0]);
    }

    [Fact]
    public void TryUpdateFactory_StoresTheValueWhenTheFactoryMutatesTheDictionary()
    {
        var dict = new Dictionary<int, string>
        {
            [0] = "initial",
        };

        var result = dict.TryUpdate(0, (_, _) =>
        {
            for (var i = 1; i <= 64; i++)
            {
                dict[i] = "filler";
            }

            return "updated";
        });

        Assert.True(result);
        Assert.Equal("updated", dict[0]);
    }
}
