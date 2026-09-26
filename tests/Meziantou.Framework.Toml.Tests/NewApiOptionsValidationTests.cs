using System;
using System.Text.Json.Serialization;

namespace Meziantou.Framework.Toml.Tests;

public sealed class NewApiOptionsValidationTests
{
    [Fact]
    public void RootValueKeyName_Empty_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                RootValueKeyName = "",
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void RootValueKeyName_InvalidSurrogate_Throws()
    {
        var invalid = "\uD800";
        var ex = Assert.Throws<ArgumentException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                RootValueKeyName = invalid,
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void RootValueWrapping_DoesNotExpandDottedRootKey()
    {
        var options = TomlSerializerOptions.Default with
        {
            RootValueHandling = TomlRootValueHandling.WrapInRootKey,
            RootValueKeyName = "a.b",
            DottedKeyHandling = TomlDottedKeyHandling.Expand,
        };

        var toml = TomlSerializer.Serialize(1, options);

        Assert.Contains("\"a.b\"", toml);
        Assert.DoesNotContain("[a]", toml);

        var value = TomlSerializer.Deserialize<int>(toml, options);
        Assert.Equal(1, value);
    }

    [Fact]
    public void PreferredObjectCreationHandling_InvalidValue_Throws()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                PreferredObjectCreationHandling = (TomlObjectCreationHandling)99,
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void StringStylePreferences_Null_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                StringStylePreferences = null!,
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void PolymorphismOptions_Null_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                PolymorphismOptions = null!,
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void Converters_Null_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
        {
            _ = TomlSerializerOptions.Default with
            {
                Converters = null!,
            };
        });

        Assert.Equal("value", ex!.ParamName);
    }

    [Fact]
    public void GetTypeInfo_UsesReflection()
    {
        var typeInfo = TomlSerializerOptions.Default.GetTypeInfo<GetTypeInfoModel>();

        Assert.Equal(typeof(GetTypeInfoModel), typeInfo.Type);
        Assert.Same(TomlSerializerOptions.Default, typeInfo.Options);
        Assert.Equal("Name = \"a\"", TomlSerializer.Serialize(new GetTypeInfoModel { Name = "a" }, typeInfo).Trim());
        Assert.Equal("b", TomlSerializer.Deserialize("Name = \"b\"", typeInfo)!.Name);
    }

    [Fact]
    public void GetTypeInfo_UsesTypeInfoResolver()
    {
        var options = TomlSerializerOptions.Default with { TypeInfoResolver = TestTomlSerializerContext.Default };

        var typeInfo = options.GetTypeInfo<GeneratedPerson>();

        Assert.Equal(typeof(GeneratedPerson), typeInfo.Type);
        Assert.Equal("Ada", TomlSerializer.Deserialize("name = \"Ada\"", typeInfo)!.Name);
    }

    [Fact]
    public void GetTypeInfo_BuiltInType()
    {
        var options = TomlSerializerOptions.Default with { RootValueHandling = TomlRootValueHandling.WrapInRootKey };

        var typeInfo = options.GetTypeInfo<int?>();

        Assert.Equal(typeof(int?), typeInfo.Type);
        Assert.Equal(42, TomlSerializer.Deserialize("value = 42", typeInfo));
    }

    [Fact]
    public void GetTypeInfo_Unsupported_Throws()
    {
        Assert.Throws<TomlException>(() => TomlSerializerOptions.Default.GetTypeInfo<Dictionary<int, string>>());
    }

    [Fact]
    public void TryGetTypeInfo_Supported()
    {
        Assert.True(TomlSerializerOptions.Default.TryGetTypeInfo<GetTypeInfoModel>(out var typeInfo));
        Assert.Equal(typeof(GetTypeInfoModel), typeInfo.Type);
    }

    [Fact]
    public void TryGetTypeInfo_Unsupported()
    {
        Assert.False(TomlSerializerOptions.Default.TryGetTypeInfo<Dictionary<int, string>>(out var typeInfo));
        Assert.Null(typeInfo);
    }

    [Theory]
    [InlineData(TomlIgnoreCondition.Always)]
    [InlineData(TomlIgnoreCondition.WhenWriting)]
    [InlineData(TomlIgnoreCondition.WhenReading)]
    public void DefaultIgnoreCondition_OtherThanNullOrDefault_Throws(TomlIgnoreCondition condition)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TomlSerializerOptions { DefaultIgnoreCondition = condition });
    }

    [Fact]
    public void TryGetTypeInfo_TypeReflectionCannotHandle_ReturnsFalse()
    {
        Assert.False(TomlSerializerOptions.Default.TryGetTypeInfo<Action>(out var delegateTypeInfo));
        Assert.Null(delegateTypeInfo);
        Assert.False(TomlSerializerOptions.Default.TryGetTypeInfo<Func<int>>(out _));
        Assert.True(TomlSerializerOptions.Default.TryGetTypeInfo<GetTypeInfoModel>(out _));
    }

    private sealed class GetTypeInfoModel
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void Options_CacheMetadataAcrossCalls()
    {
        var options = new TomlSerializerOptions();

        var first = options.GetTypeInfo<System.Collections.Generic.List<int>>();

        Assert.Same(first, options.GetTypeInfo<System.Collections.Generic.List<int>>());
        Assert.NotSame(first, (options with { MaxDepth = 10 }).GetTypeInfo<System.Collections.Generic.List<int>>());
        Assert.Throws<TomlException>(() => TomlSerializer.Deserialize<System.Collections.Generic.List<int>>("value = [\"x\"]\n", options with { RootValueHandling = TomlRootValueHandling.WrapInRootKey }));
        Assert.Equal([1, 2], TomlSerializer.Deserialize<System.Collections.Generic.List<int>>("value = [1, 2]\n", options with { RootValueHandling = TomlRootValueHandling.WrapInRootKey })!);
    }
}
