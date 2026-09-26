using System.Collections;
using System.Collections.Generic;

namespace Tomlyn.Tests;

public sealed class NewApiNonStringDictionaryKeyTests
{
    private sealed class NonStringKeyDictionaryModel
    {
        public Dictionary<int, long> Map { get; set; } = new();
    }

    [Fact]
    public void Serialize_NonStringKeyDictionary_ThrowsTomlException()
    {
        var model = new NonStringKeyDictionaryModel
        {
            Map = new Dictionary<int, long> { [1] = 2 },
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(model));
    }

    [Fact]
    public void Serialize_NonGenericHashtable_ThrowsTomlException()
    {
        var table = new Hashtable
        {
            ["a"] = 1,
        };

        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(table, typeof(Hashtable)));
    }
}

