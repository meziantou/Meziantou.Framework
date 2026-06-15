namespace Meziantou.Framework.Yamlish.Tests;

public sealed class YamlishSerializerTests
{
    [Fact]
    public void Options_DefaultValues()
    {
        var options = new YamlishSerializerOptions();

        Assert.False(options.IgnoreReadOnlyFields);
        Assert.False(options.IgnoreReadOnlyProperties);
        Assert.False(options.IncludeFields);
        Assert.Equal(' ', options.IndentCharacter);
        Assert.Equal(2, options.IndentSize);
        Assert.Equal(Environment.NewLine, options.NewLine);
        Assert.Equal(YamlishObjectCreationHandling.Replace, options.PreferredObjectCreationHandling);
        Assert.True(options.AllowDuplicateProperties);
    }

    [Fact]
    public void Serialize_UsesCSharpNamesByDefault()
    {
        var result = YamlishSerializer.Serialize(new DefaultNamesProduct { Id = "abc", IsAvailable = true, Price = 12.5m });

        Assert.Equal("""
            Id: abc
            IsAvailable: true
            Price: 12.5
            """, result);
    }

    [Fact]
    public void Serialize_UsesSnakeCasePolicyAndAttributes()
    {
        var options = new YamlishSerializerOptions { PropertyNamingPolicy = YamlishNamingPolicy.SnakeCaseLower };

        var result = YamlishSerializer.Serialize(new Product { Id = "abc", IsAvailable = true, Ignored = "secret" }, options);

        Assert.Contains("product_id: abc", result, StringComparison.Ordinal);
        Assert.Contains("is_available: true", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Ignored", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Options_AddAttribute_OverridesDeclaredAttributes()
    {
        var options = new YamlishSerializerOptions();
        options.AddAttribute(typeof(Product), nameof(Product.Id), new YamlishPropertyNameAttribute("id_from_options"));
        options.AddAttribute(typeof(Product), nameof(Product.Ignored), new YamlishIgnoreAttribute { Condition = YamlishIgnoreCondition.Never });
        options.AddAttribute<Product>(product => product.Price, new YamlishIgnoreAttribute());

        var content = YamlishSerializer.Serialize(new Product { Id = "abc", Ignored = "value" }, options);
        var result = YamlishSerializer.Deserialize<Product>("id_from_options: def\nIgnored: deserialized", options);

        Assert.Contains("id_from_options: abc", content, StringComparison.Ordinal);
        Assert.Contains("Ignored: value", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Price", content, StringComparison.Ordinal);
        Assert.Equal("def", result?.Id);
        Assert.Equal("deserialized", result?.Ignored);
    }

    [Fact]
    public void Options_AreReadOnlyAndMetadataIsCachedAfterFirstUse()
    {
        var predicateEvaluationCount = 0;
        var options = new YamlishSerializerOptions();
        options.AddPropertyAttribute(property =>
        {
            predicateEvaluationCount++;
            return false;
        }, new YamlishIgnoreAttribute());

        YamlishSerializer.Serialize(new DefaultNamesProduct(), options);
        var countAfterFirstUse = predicateEvaluationCount;
        YamlishSerializer.Serialize(new DefaultNamesProduct(), options);
        YamlishSerializer.Deserialize<DefaultNamesProduct>("Id: abc", options);

        Assert.True(options.IsReadOnly);
        Assert.True(countAfterFirstUse > 0);
        Assert.Equal(countAfterFirstUse, predicateEvaluationCount);
        Assert.Throws<InvalidOperationException>(() => options.IncludeFields = true);
        Assert.Throws<InvalidOperationException>(() => options.IndentCharacter = '\t');
        Assert.Throws<InvalidOperationException>(() => options.NewLine = "\n");
        Assert.Throws<InvalidOperationException>(() => options.AllowDuplicateProperties = false);
        Assert.Throws<InvalidOperationException>(() => options.AddAttribute(typeof(DefaultNamesProduct), nameof(DefaultNamesProduct.Id), new YamlishIgnoreAttribute()));
    }

    [Fact]
    public void Serialize_IgnoreReadOnlyProperties()
    {
        var value = new ReadOnlyMembers();

        Assert.Contains("ReadOnlyProperty: property", YamlishSerializer.Serialize(value), StringComparison.Ordinal);
        Assert.DoesNotContain("ReadOnlyProperty", YamlishSerializer.Serialize(value, new YamlishSerializerOptions { IgnoreReadOnlyProperties = true }), StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_IgnoreReadOnlyFields()
    {
        var value = new ReadOnlyMembers();

        Assert.Contains("ReadOnlyField: field", YamlishSerializer.Serialize(value, new YamlishSerializerOptions { IncludeFields = true }), StringComparison.Ordinal);
        Assert.DoesNotContain("ReadOnlyField", YamlishSerializer.Serialize(value, new YamlishSerializerOptions { IncludeFields = true, IgnoreReadOnlyFields = true }), StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeAndDeserialize_IncludeFields()
    {
        var options = new YamlishSerializerOptions { IncludeFields = true };

        var content = YamlishSerializer.Serialize(new FieldValue { Value = "serialized" }, options);
        var result = YamlishSerializer.Deserialize<FieldValue>("Value: deserialized", options);

        Assert.Equal("Value: serialized", content);
        Assert.Equal("deserialized", result?.Value);
    }

    [Fact]
    public void Serialize_UsesIndentCharacterAndIndentSize()
    {
        var options = new YamlishSerializerOptions { IndentCharacter = '\t', IndentSize = 3 };

        var content = YamlishSerializer.Serialize(new NestedValue { Value = new StringValue { Value = "first\nsecond" } }, options);
        var result = YamlishSerializer.Deserialize<NestedValue>(content, options);

        Assert.Equal("Value:\n\t\t\tValue: |-\n\t\t\t\t\t\tfirst\n\t\t\t\t\t\tsecond", content);
        Assert.Equal("first\nsecond", result?.Value?.Value);
    }

    [Fact]
    public void Serialize_UsesNewLine()
    {
        var options = new YamlishSerializerOptions { NewLine = "\r\n" };

        var content = YamlishSerializer.Serialize(new DefaultNamesProduct { Id = "abc", IsAvailable = true }, options);

        Assert.Equal("Id: abc\r\nIsAvailable: true\r\nPrice: 0", content);
    }

    [Fact]
    public void Deserialize_PreferredObjectCreationHandling_ReplaceByDefault()
    {
        var value = YamlishSerializer.Deserialize<ObjectCreationValue>("""
            Values: [new]
            SettableValues: [new]
            EnumerableValues: [new]
            Lookup:
              existing: 2
              new: 3
            Dimensions:
              Width: 10
            """);

        Assert.NotNull(value);
        Assert.Equal(["initial"], value.Values);
        Assert.Equal(["new"], value.SettableValues);
        Assert.Equal(["new"], value.EnumerableValues);
        Assert.Equal(1, value.Lookup["existing"]);
        Assert.False(value.Lookup.ContainsKey("new"));
        Assert.Equal(1, value.Dimensions.Width);
    }

    [Fact]
    public void Deserialize_PreferredObjectCreationHandling_Populate()
    {
        var options = new YamlishSerializerOptions { PreferredObjectCreationHandling = YamlishObjectCreationHandling.Populate };

        var value = YamlishSerializer.Deserialize<ObjectCreationValue>("""
            Values: [new]
            SettableValues: [new]
            EnumerableValues: [new]
            Lookup:
              existing: 2
              new: 3
            Dimensions:
              Width: 10
            """, options);

        Assert.NotNull(value);
        Assert.Equal(["initial", "new"], value.Values);
        Assert.Equal(["initial", "new"], value.SettableValues);
        Assert.Equal(["new"], value.EnumerableValues);
        Assert.Equal(2, value.Lookup["existing"]);
        Assert.Equal(3, value.Lookup["new"]);
        Assert.Equal(10, value.Dimensions.Width);
    }

    [Fact]
    public void Deserialize_AllowsDuplicatePropertiesByDefault()
    {
        var value = YamlishSerializer.Deserialize<StringValue>("Value: first\nValue: second");

        Assert.Equal("second", value?.Value);
    }

    [Fact]
    public void Deserialize_AllowDuplicatePropertiesFalse_Throws()
    {
        var options = new YamlishSerializerOptions { AllowDuplicateProperties = false };

        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<StringValue>("Value: first\nValue: second", options));
    }

    [Fact]
    public void Serialize_DuplicateProperties_Throws()
    {
        Assert.Throws<ArgumentException>(() => YamlishSerializer.Serialize(new DuplicatePropertyNames()));
    }

    [Fact]
    public void Serialize_DefaultIgnoreCondition_WhenWritingDefault()
    {
        var options = new YamlishSerializerOptions { DefaultIgnoreCondition = YamlishIgnoreCondition.WhenWritingDefault };

        var result = YamlishSerializer.Serialize(new IgnoreConditions(), options);

        Assert.Equal("Never: 0", result);
    }

    [Fact]
    public void Serialize_IgnoreAttributeConditions()
    {
        var value = new IgnoreConditions
        {
            Always = "value",
            WhenWritingDefault = 1,
            WhenWritingNull = "value",
        };

        var result = YamlishSerializer.Serialize(value);

        Assert.Equal("""
            Never: 0
            WhenWritingDefault: 1
            WhenWritingNull: value
            """, result);
    }

    [Fact]
    public void Serialize_IgnoreAttributeConditions_Fields()
    {
        var options = new YamlishSerializerOptions { IncludeFields = true };

        var result = YamlishSerializer.Serialize(new IgnoreConditionFields
        {
            Never = 0,
            Always = "value",
            WhenWritingDefault = 0,
            WhenWritingNull = null,
        }, options);

        Assert.Equal("Never: 0", result);
    }

    [Fact]
    public void Options_AddFieldAttribute_OverridesDeclaredAttribute()
    {
        var options = new YamlishSerializerOptions { IncludeFields = true };
        options.AddFieldAttribute(field => field.Name == nameof(IgnoreConditionFields.Always), new YamlishIgnoreAttribute { Condition = YamlishIgnoreCondition.Never });

        var result = YamlishSerializer.Serialize(new IgnoreConditionFields { Always = "value" }, options);

        Assert.Equal("""
            Never: 0
            Always: value
            """, result);
    }

    [Fact]
    public void Serialize_DefaultIgnoreCondition_Never_NullThrows()
    {
        var options = new YamlishSerializerOptions { DefaultIgnoreCondition = YamlishIgnoreCondition.Never };

        Assert.Throws<InvalidOperationException>(() => YamlishSerializer.Serialize(new StringValue(), options));
    }

    [Fact]
    public void Deserialize_AlwaysIgnoreConditionIsIgnored()
    {
        var result = YamlishSerializer.Deserialize<IgnoreConditions>("""
            Never: 1
            Always: value
            WhenWritingDefault: 2
            WhenWritingNull: value
            """);

        Assert.NotNull(result);
        Assert.Equal(1, result.Never);
        Assert.Null(result.Always);
        Assert.Equal(2, result.WhenWritingDefault);
        Assert.Equal("value", result.WhenWritingNull);
    }

    [Fact]
    public void Deserialize_ConvertsScalarsAndNestedValues()
    {
        var result = YamlishSerializer.Deserialize<Product>("""
            product_id: abc
            IsAvailable: true
            Price: 12.5
            Tags: [new, sale]
            Dimensions:
              Width: 10
              Height: 20
            """);

        Assert.NotNull(result);
        Assert.Equal("abc", result.Id);
        Assert.True(result.IsAvailable);
        Assert.Equal(12.5m, result.Price);
        Assert.Equal(["new", "sale"], result.Tags);
        Assert.Equal(10, result.Dimensions?.Width);
        Assert.Equal(20, result.Dimensions?.Height);
    }

    [Fact]
    public void SerializeAndDeserialize_Dictionary()
    {
        var value = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["one"] = 1,
            ["two"] = 2,
        };

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<Dictionary<string, int>>(content);

        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain value")]
    [InlineData(" leading and trailing ")]
    [InlineData("quote: \"; slash: \\; tab: \t")]
    [InlineData("first\nsecond")]
    [InlineData("first\n")]
    public void SerializeAndDeserialize_String_RoundTripsWithoutDocumentTrailingNewLine(string value)
    {
        var content = YamlishSerializer.Serialize(new StringValue { Value = value });
        var result = YamlishSerializer.Deserialize<StringValue>(content);

        Assert.False(content.EndsWith("\n", StringComparison.Ordinal));
        Assert.Equal(value, result?.Value);
    }

    [Theory]
    [InlineData("Value: plain value", "plain value")]
    [InlineData("Value: \" leading and trailing \"", " leading and trailing ")]
    [InlineData("Value: 'it''s literal'", "it's literal")]
    [InlineData("Value: |-\n  first\n  second\n", "first\nsecond")]
    [InlineData("Value: |\n  first\n  second\n", "first\nsecond\n")]
    [InlineData("Value: |+\n  first\n  second\n\n", "first\nsecond\n\n")]
    [InlineData("Value: >-\n  first\n  second\n", "first second")]
    [InlineData("Value: >\n  first\n  second\n", "first second\n")]
    [InlineData("Value: >+\n  first\n  second\n\n", "first second\n\n")]
    public void Deserialize_StringValues(string content, string expected)
    {
        var result = YamlishSerializer.Deserialize<StringValue>(content);

        Assert.Equal(expected, result?.Value);
    }

    private sealed class Product
    {
        [YamlishPropertyName("product_id")]
        public string? Id { get; set; }

        public bool IsAvailable { get; set; }

        public decimal Price { get; set; }

        public List<string>? Tags { get; set; }

        public Dimensions? Dimensions { get; set; }

        [YamlishIgnore]
        public string? Ignored { get; set; }
    }

    private sealed class DefaultNamesProduct
    {
        public string? Id { get; set; }

        public bool IsAvailable { get; set; }

        public decimal Price { get; set; }
    }

    private sealed class Dimensions
    {
        public int Width { get; set; }

        public int Height { get; set; }
    }

    private sealed class StringValue
    {
        public string? Value { get; set; }
    }

    private sealed class NestedValue
    {
        public StringValue? Value { get; set; }
    }

    private sealed class ObjectCreationValue
    {
        public List<string> Values { get; } = ["initial"];

        public List<string> SettableValues { get; set; } = ["initial"];

        public IEnumerable<string> EnumerableValues { get; set; } = ["initial"];

        public Dictionary<string, int> Lookup { get; } = new(StringComparer.Ordinal)
        {
            ["existing"] = 1,
        };

        public Dimensions Dimensions { get; } = new() { Width = 1 };
    }

    private sealed class DuplicatePropertyNames
    {
        [YamlishPropertyName("Value")]
        public string First { get; set; } = "first";

        [YamlishPropertyName("Value")]
        public string Second { get; set; } = "second";
    }

    private sealed class IgnoreConditions
    {
        [YamlishIgnore(Condition = YamlishIgnoreCondition.Never)]
        public int Never { get; set; }

        [YamlishIgnore]
        public string? Always { get; set; }

        [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingDefault)]
        public int WhenWritingDefault { get; set; }

        [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingNull)]
        public string? WhenWritingNull { get; set; }
    }

#pragma warning disable CA1051
    private sealed class FieldValue
    {
        public string? Value;
    }

    private sealed class ReadOnlyMembers
    {
        public readonly string ReadOnlyField = "field";

        public string ReadOnlyProperty { get; } = "property";
    }

    private sealed class IgnoreConditionFields
    {
        [YamlishIgnore(Condition = YamlishIgnoreCondition.Never)]
        public int Never;

        [YamlishIgnore]
        public string? Always;

        [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingDefault)]
        public int WhenWritingDefault;

        [YamlishIgnore(Condition = YamlishIgnoreCondition.WhenWritingNull)]
        public string? WhenWritingNull;
    }
#pragma warning restore CA1051
}
