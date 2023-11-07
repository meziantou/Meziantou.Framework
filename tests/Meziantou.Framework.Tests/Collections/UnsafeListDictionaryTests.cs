using System.Text.Json;
using FluentAssertions;
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

        dict.Should().HaveCount(3); // Allows duplicate values
        dict.Should().ContainKey(1);
        dict.Should().ContainKey(2);
        dict.Should().NotContainKey(4);

        dict[1] = "d";
        dict.Count.Should().Be(3); // Replace existing item

        dict.Keys.Should().Equal([1, 2, 2]);
        dict.Values.Should().Equal(["d", "b", "c"]);

        dict.Clear();
        dict.Count.Should().Be(0);

        dict.AddRange(new KeyValuePair<int, string>[] { new(4, "a"), new(5, "e") });
        dict.Keys.Should().Equal([4, 5]);
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
        json.Should().StartWith("{");
        var deserialized = JsonSerializer.Deserialize<UnsafeListDictionary<int, string>>(json);

        deserialized.Should().Equal(dict);
    }
}
