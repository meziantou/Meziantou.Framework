using System;
using System.Linq;
using Meziantou.Framework.Toml.Serialization;

namespace Meziantou.Framework.Toml.Tests;

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

        Assert.Equal(new[] { "Meziantou.Framework.Toml.Serialization.TomlPropertyNameAttribute" }, exportedTypes);
    }

    [Fact]
    public void FormattingAttributes_RejectInvalidEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlTableArrayStyleAttribute((TomlTableArrayStyle)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlInlineTableAttribute((TomlInlineTablePolicy)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlStringStyleAttribute((TomlStringStyle)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlMappingOrderAttribute((TomlMappingOrderPolicy)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlDottedKeyHandlingAttribute((TomlDottedKeyHandling)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlStringStyleAttribute(TomlStringStyle.Basic) { PreferLiteralWhenNoEscapes = (TomlBooleanPreference)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlStringStyleAttribute(TomlStringStyle.Basic) { AllowHexEscapes = (TomlBooleanPreference)42 });
    }

    [Theory]
    [InlineData(typeof(InvalidIgnoreCondition))]
    [InlineData(typeof(InvalidUnknownDerivedTypeHandling))]
    [InlineData(typeof(InvalidHexEscapes))]
    [InlineData(typeof(InvalidMappingOrder))]
    [InlineData(typeof(InvalidInlineTable))]
    public void ReflectionMetadata_AttributeWithAnUndefinedValue_IsAConfigurationError(Type type)
    {
        var exception = Assert.Throws<TomlException>(() => TomlSerializer.TryDeserialize("A = 1\n", type, out _));

        Assert.Contains("has an undefined value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetTypeInfo_AttributeWithAnUndefinedValue_ReturnsFalse()
    {
        Assert.False(TomlSerializerOptions.Default.TryGetTypeInfo<InvalidMappingOrder>(out _));
        Assert.False(TomlSerializerOptions.Default.TryGetTypeInfo<InvalidInlineTable>(out _));
    }

    [TomlMappingOrder((TomlMappingOrderPolicy)42)]
    private sealed class InvalidMappingOrder
    {
        public int A { get; set; }
    }

    private sealed class InvalidInlineTable
    {
        [TomlInlineTable((TomlInlineTablePolicy)42)]
        public int A { get; set; }
    }

    private sealed class InvalidIgnoreCondition
    {
        [TomlIgnore(Condition = (TomlIgnoreCondition)42)]
        public int A { get; set; }
    }

    [TomlPolymorphic(UnknownDerivedTypeHandling = (TomlUnknownDerivedTypeHandling)42)]
    [TomlDerivedType(typeof(InvalidUnknownDerivedTypeHandlingDerived), "d")]
    private class InvalidUnknownDerivedTypeHandling
    {
        public int A { get; set; }
    }

    private sealed class InvalidUnknownDerivedTypeHandlingDerived : InvalidUnknownDerivedTypeHandling
    {
    }

    private sealed class InvalidHexEscapes
    {
        [TomlStringStyle(TomlStringStyle.Basic, AllowHexEscapes = (TomlBooleanPreference)42)]
        public string? A { get; set; }
    }

    [Fact]
    public void BehaviorAttributes_RejectInvalidEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlObjectCreationHandlingAttribute((TomlObjectCreationHandling)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlUnmappedMemberHandlingAttribute((TomlUnmappedMemberHandling)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlIgnoreAttribute { Condition = (TomlIgnoreCondition)42 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlPolymorphicAttribute { UnknownDerivedTypeHandling = (TomlUnknownDerivedTypeHandling)(-2) });
        Assert.Equal(TomlUnknownDerivedTypeHandling.Unspecified, new TomlPolymorphicAttribute { UnknownDerivedTypeHandling = TomlUnknownDerivedTypeHandling.Unspecified }.UnknownDerivedTypeHandling);
    }
}
