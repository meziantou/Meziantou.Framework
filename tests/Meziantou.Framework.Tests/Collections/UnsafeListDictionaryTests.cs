using System.Text.Json;
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections;

public class UnsafeListDictionaryTests
{
    [Fact]
    public void TestDictionary()
    {
        UnsafeListDictionary<int, string> dict = new()
        {
            { 1, "a" },
            { 2, "b" },
            { 2, "c" },
        };

        Assert.Equal(3, dict.Count); // Allows duplicate values
        Assert.Contains(1, (IReadOnlyDictionary<int, string>)dict);
        Assert.Contains(2, (IReadOnlyDictionary<int, string>)dict);
        Assert.DoesNotContain(4, (IReadOnlyDictionary<int, string>)dict);

        dict[1] = "d";
        Assert.Equal(3, dict.Count); // Replace existing item
        Assert.Equal([1, 2, 2], dict.Keys);
        Assert.Equal(["d", "b", "c"], dict.Values);

        dict.Clear();
        Assert.Empty(dict);

        dict.AddRange(new KeyValuePair<int, string>[] { new(4, "a"), new(5, "e") });
        Assert.Equal([4, 5], dict.Keys);
    }

    [Fact]
    public void JsonSerializable()
    {
        UnsafeListDictionary<int, string> dict = new()
        {
            { 1, "a" },
            { 2, "b" },
            { 3, "c" },
        };

        var json = JsonSerializer.Serialize(dict);
        Assert.StartsWith(['{'], json, StringComparison.Ordinal);
        var deserialized = JsonSerializer.Deserialize<UnsafeListDictionary<int, string>>(json);
        Assert.Equal(dict, deserialized);
    }
}
