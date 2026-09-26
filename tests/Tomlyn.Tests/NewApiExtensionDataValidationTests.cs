using System.Collections.Generic;
using Tomlyn.Serialization;

namespace Tomlyn.Tests;

public sealed class NewApiExtensionDataValidationTests
{
    private sealed class MultipleExtensionDataMembers
    {
        [TomlExtensionData]
        public Dictionary<string, object?> Extra { get; set; } = new();

        [TomlExtensionData]
        public Dictionary<string, object?> Extra2 { get; set; } = new();
    }

    private sealed class InvalidExtensionDataType
    {
        [TomlExtensionData]
        public Dictionary<int, object?> Extra { get; set; } = new();
    }

    [Fact]
    public void ExtensionData_MultipleMembers_ThrowsTomlException()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new MultipleExtensionDataMembers()));
    }

    [Fact]
    public void ExtensionData_InvalidDictionaryType_ThrowsTomlException()
    {
        Assert.Throws<TomlException>(() => TomlSerializer.Serialize(new InvalidExtensionDataType()));
    }
}

