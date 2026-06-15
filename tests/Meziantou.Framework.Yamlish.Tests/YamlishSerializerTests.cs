namespace Meziantou.Framework.Yamlish.Tests;

public sealed class YamlishSerializerTests
{
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
        Assert.Throws<InvalidOperationException>(() => options.AddAttribute(typeof(DefaultNamesProduct), nameof(DefaultNamesProduct.Id), new YamlishIgnoreAttribute()));
    }

    [Fact]
    public void Options_Converters_SerializeAndDeserialize()
    {
        var options = new YamlishSerializerOptions();
        options.Converters.Add(new TemperatureConverter());

        var content = YamlishSerializer.Serialize(new Weather { Temperature = new Temperature(21) }, options);
        var result = YamlishSerializer.Deserialize<Weather>("Temperature: 32 C", options);

        Assert.Equal("Temperature: 21 C", content);
        Assert.Equal(new Temperature(32), result?.Temperature);
    }

    [Fact]
    public void Options_Converters_OverrideBuiltInAndUseRuntimeType()
    {
        var options = new YamlishSerializerOptions();
        options.Converters.Add(new HexInt32Converter());
        options.Converters.Add(new TemperatureConverter());

        var integer = YamlishSerializer.Serialize(42, options);
        var model = YamlishSerializer.Serialize(new ObjectValue { Value = new Temperature(21) }, options);

        Assert.Equal("0x2A", integer);
        Assert.Equal("Value: 21 C", model);
    }

    [Fact]
    public void Options_ConverterFactory_SerializeAndDeserialize()
    {
        var options = new YamlishSerializerOptions();
        options.Converters.Add(new WrapperConverterFactory());

        var content = YamlishSerializer.Serialize(new Wrapper<int> { Value = 42 }, options);
        var result = YamlishSerializer.Deserialize<Wrapper<int>>("84", options);

        Assert.Equal("42", content);
        Assert.Equal(84, result?.Value);
    }

    [Fact]
    public void Options_Converters_FirstMatchWinsAndResolutionIsCached()
    {
        var first = new TrackingTemperatureConverter("first");
        var second = new TrackingTemperatureConverter("second");
        var options = new YamlishSerializerOptions();
        options.Converters.Add(first);
        options.Converters.Add(second);

        Assert.Equal("first", YamlishSerializer.Serialize(new Temperature(1), options));
        Assert.Equal("first", YamlishSerializer.Serialize(new Temperature(2), options));

        Assert.Equal(1, first.CanConvertCallCount);
        Assert.Equal(0, second.CanConvertCallCount);
        Assert.True(options.Converters.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => options.Converters.Add(new TemperatureConverter()));
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

    [Fact]
    public void Deserialize_InvalidScalar_Throws()
    {
        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<Product>("Price: invalid"));
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

    private sealed class Weather
    {
        public Temperature Temperature { get; set; }
    }

    private sealed class ObjectValue
    {
        public object? Value { get; set; }
    }

    private readonly record struct Temperature(int Value);

    private sealed class TemperatureConverter : YamlishConverter<Temperature>
    {
        public override Temperature Read(YamlishNode node, YamlishSerializerOptions options)
        {
            var value = Assert.IsType<YamlishScalar>(node).Value;
            return new Temperature(int.Parse(value.AsSpan(0, value.Length - 2), CultureInfo.InvariantCulture));
        }

        public override YamlishNode Write(Temperature value, YamlishSerializerOptions options)
        {
            return new YamlishScalar($"{value.Value.ToString(CultureInfo.InvariantCulture)} C");
        }
    }

    private sealed class HexInt32Converter : YamlishConverter<int>
    {
        public override int Read(YamlishNode node, YamlishSerializerOptions options)
        {
            return int.Parse(Assert.IsType<YamlishScalar>(node).Value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        public override YamlishNode Write(int value, YamlishSerializerOptions options)
        {
            return new YamlishScalar($"0x{value.ToString("X", CultureInfo.InvariantCulture)}");
        }
    }

    private sealed class TrackingTemperatureConverter(string value) : YamlishConverter
    {
        private readonly string _value = value;

        public int CanConvertCallCount { get; private set; }

        public override bool CanConvert(Type typeToConvert)
        {
            CanConvertCallCount++;
            return typeToConvert == typeof(Temperature);
        }

        public override object? Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options) => throw new NotSupportedException();

        public override YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options) => new YamlishScalar(_value);
    }

    private sealed class Wrapper<T>
    {
        public T? Value { get; set; }
    }

    private sealed class WrapperConverterFactory : YamlishConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(Wrapper<>);

        public override YamlishConverter CreateConverter(Type typeToConvert, YamlishSerializerOptions options)
        {
            return (YamlishConverter)Activator.CreateInstance(typeof(WrapperConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
        }
    }

    private sealed class WrapperConverter<T> : YamlishConverter<Wrapper<T>>
    {
        public override Wrapper<T> Read(YamlishNode node, YamlishSerializerOptions options)
        {
            var value = Assert.IsType<YamlishScalar>(node).Value;
            return new Wrapper<T> { Value = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture) };
        }

        public override YamlishNode Write(Wrapper<T> value, YamlishSerializerOptions options)
        {
            return new YamlishScalar(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
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
