using System;
using System.Collections.Generic;
using System.Linq;
using Meziantou.Framework.Toml.Model;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiUntypedModelTests
{
    [Fact]
    public void DeserializeTomlTable_PreservesTableArraysAsTomlTableArray()
    {
        var toml = """
            [[statuses]]
            id = 1
            """;

        var table = TomlSerializer.Deserialize<TomlTable>(toml);

        Assert.NotNull(table);
        Assert.True(table!.TryGetValue("statuses", out var statuses));
        Assert.IsAssignableTo<TomlTableArray>(statuses);
        Assert.HasCount(1, (TomlTableArray)statuses!);
    }

    [Fact]
    public void DeserializeTomlTable_TableArrayChildTablesAttachToLastElement()
    {
        var toml = """
            [[statuses]]
            id = 1

            [statuses.metadata]
            result_type = "recent"
            """;

        var table = TomlSerializer.Deserialize<TomlTable>(toml);

        Assert.NotNull(table);
        Assert.True(table!.TryGetValue("statuses", out var statuses));
        Assert.IsAssignableTo<TomlTableArray>(statuses);
        var tableArray = (TomlTableArray)statuses!;
        Assert.HasCount(1, tableArray);
        Assert.True(tableArray[0].TryGetValue("metadata", out var metadata));
        Assert.IsAssignableTo<TomlTable>(metadata);
        Assert.Equal("recent", ((TomlTable)metadata!)["result_type"]);
    }

    // A small table searches a list, and a large one a dictionary
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void TomlTable_KeyValuePairContainsAndRemove_CompareValuesByEquality(int count)
    {
        var table = new TomlTable();
        for (var i = 0; i < count; i++)
        {
            table[$"k{i}"] = (long)i;
        }

        ICollection<KeyValuePair<string, object>> collection = table;

        Assert.Contains(new KeyValuePair<string, object>("k0", 0L), collection);
        Assert.DoesNotContain(new KeyValuePair<string, object>("k0", 1L), collection);
        Assert.False(collection.Remove(new KeyValuePair<string, object>("k0", 1L)));
        Assert.True(collection.Remove(new KeyValuePair<string, object>("k0", 0L)));
        Assert.DoesNotContain("k0", table.Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void TomlTable_NullKey_Throws(int count)
    {
        var table = new TomlTable();
        for (var i = 0; i < count; i++)
        {
            table[$"k{i}"] = (long)i;
        }

        Assert.Throws<ArgumentNullException>(() => table.Add(null!, 1L));
        Assert.Throws<ArgumentNullException>(() => table[null!] = 1L);
        Assert.Throws<ArgumentNullException>(() => table[null!]);
        Assert.Throws<ArgumentNullException>(() => table.ContainsKey(null!));
        Assert.Throws<ArgumentNullException>(() => table.TryGetValue(null!, out _));
        Assert.Throws<ArgumentNullException>(() => table.Remove(null!));
        Assert.HasCount(count, table);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void TomlTable_AddFirst_KeepsEveryKeyReachable(int count)
    {
        var table = new TomlTable();
        for (var i = 0; i < count; i++)
        {
            table[$"k{i}"] = (long)i;
        }

        table.AddFirst("first", -1L);

        Assert.Equal("first", table.Keys.First());
        Assert.HasCount(count + 1, table);
        Assert.Equal(-1L, table["first"]);
        for (var i = 0; i < count; i++)
        {
            Assert.Equal((long)i, table[$"k{i}"]);
        }
    }

    [Fact]
    public void TomlArray_Null_IsHandledLikeAnyOtherItem()
    {
        var array = new TomlArray { 1L, null };
        object? nullItem = null;

        Assert.Contains(nullItem, array);
        Assert.Equal(1, array.IndexOf(null));
        Assert.True(array.Remove(null));
        Assert.DoesNotContain(nullItem, array);
    }
}
