using System;
using System.Linq;
using Tomlyn.Serialization;

namespace Tomlyn.Tests;

public class NewApiAttributeContractTests
{
    [Fact]
    public void ExportedTomlAttributes_HaveUniqueTypeNames()
    {
        var duplicateNames = typeof(TomlSerializer).Assembly
            .GetExportedTypes()
            .Where(static type => type.Name.StartsWith("Toml", StringComparison.Ordinal) &&
                                  type.Name.EndsWith("Attribute", StringComparison.Ordinal))
            .GroupBy(static type => type.Name, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        Assert.Empty(duplicateNames);
    }

    [Fact]
    public void ExportedTomlAttributes_InheritTomlAttribute()
    {
        var nonDerivedAttributes = typeof(TomlSerializer).Assembly
            .GetExportedTypes()
            .Where(static type => type != typeof(TomlAttribute) &&
                                  type.IsSubclassOf(typeof(Attribute)) &&
                                  type.Name.StartsWith("Toml", StringComparison.Ordinal) &&
                                  type.Name.EndsWith("Attribute", StringComparison.Ordinal))
            .Where(static type => !typeof(TomlAttribute).IsAssignableFrom(type))
            .Select(static type => type.FullName)
            .ToArray();

        Assert.Empty(nonDerivedAttributes);
    }

    [Fact]
    public void TomlPropertyNameAttribute_IsOnlyAvailableFromSerializationNamespace()
    {
        var exportedTypes = typeof(TomlSerializer).Assembly
            .GetExportedTypes()
            .Where(static type => type.Name == nameof(TomlPropertyNameAttribute))
            .Select(static type => type.FullName)
            .OrderBy(static fullName => fullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Tomlyn.Serialization.TomlPropertyNameAttribute" }, exportedTypes);
    }

    [Fact]
    public void FormattingAttributes_RejectInvalidEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlTableArrayStyleAttribute((TomlTableArrayStyle)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlInlineTableAttribute((TomlInlineTablePolicy)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlStringStyleAttribute((TomlStringStyle)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlMappingOrderAttribute((TomlMappingOrderPolicy)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlDottedKeyHandlingAttribute((TomlDottedKeyHandling)42));
    }
}
