using Tomlyn.Model;

namespace Tomlyn.Tests;

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
}
