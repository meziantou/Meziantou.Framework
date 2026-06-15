namespace Meziantou.Framework.Yamlish.Tests;

public sealed class YamlishConverterTests
{
    [Fact]
    public void StringConverter() => AssertBuiltInConverter("value");

    [Fact]
    public void CharConverter() => AssertBuiltInConverter('a');

    [Fact]
    public void BooleanConverter() => AssertBuiltInConverter(true);

    [Fact]
    public void ByteConverter() => AssertBuiltInConverter(byte.MaxValue);

    [Fact]
    public void SByteConverter() => AssertBuiltInConverter(sbyte.MinValue);

    [Fact]
    public void Int16Converter() => AssertBuiltInConverter(short.MinValue);

    [Fact]
    public void UInt16Converter() => AssertBuiltInConverter(ushort.MaxValue);

    [Fact]
    public void Int32Converter() => AssertBuiltInConverter(int.MinValue);

    [Fact]
    public void UInt32Converter() => AssertBuiltInConverter(uint.MaxValue);

    [Fact]
    public void Int64Converter() => AssertBuiltInConverter(long.MinValue);

    [Fact]
    public void UInt64Converter() => AssertBuiltInConverter(ulong.MaxValue);

    [Fact]
    public void IntPtrConverter() => AssertBuiltInConverter((nint)(-42));

    [Fact]
    public void UIntPtrConverter() => AssertBuiltInConverter((nuint)42);

    [Fact]
    public void SingleConverter()
    {
        AssertBuiltInConverter(1.25f);
        AssertBuiltInConverter(float.MaxValue);
    }

    [Fact]
    public void DoubleConverter()
    {
        AssertBuiltInConverter(1.25d);
        AssertBuiltInConverter(double.MaxValue);
    }

    [Fact]
    public void DecimalConverter()
    {
        AssertBuiltInConverter(1.25m);
        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<decimal>("invalid"));
    }

    [Fact]
    public void GuidConverter() => AssertBuiltInConverter(Guid.Parse("a5bdf58d-9008-49d3-843d-4227b5604e68"));

    [Fact]
    public void DateTimeConverter()
    {
        var value = new DateTime(2026, 6, 15, 12, 34, 56, 789, DateTimeKind.Utc).AddTicks(1234);

        AssertBuiltInConverter(value);
        Assert.Equal("2026-06-15T12:34:56.7891234Z", YamlishSerializer.Serialize(value));
    }

    [Fact]
    public void DateTimeOffsetConverter()
    {
        var value = new DateTimeOffset(2026, 6, 15, 12, 34, 56, 789, TimeSpan.FromHours(-4)).AddTicks(1234);

        AssertBuiltInConverter(value);
        Assert.Equal("2026-06-15T12:34:56.7891234-04:00", YamlishSerializer.Serialize(value));
    }

    [Fact]
    public void DateOnlyConverter()
    {
        var value = new DateOnly(2026, 6, 15);

        AssertBuiltInConverter(value);
        Assert.Equal("2026-06-15", YamlishSerializer.Serialize(value));
    }

    [Fact]
    public void TimeOnlyConverter()
    {
        var value = new TimeOnly(12, 34, 56, 789).Add(TimeSpan.FromTicks(1234));

        AssertBuiltInConverter(value);
        Assert.Equal("12:34:56.7891234", YamlishSerializer.Serialize(value));
    }

    [Fact]
    public void TimeSpanConverter()
    {
        var value = TimeSpan.FromDays(1) + TimeSpan.FromTicks(1234);

        AssertBuiltInConverter(value);
        Assert.Equal("1.00:00:00.0001234", YamlishSerializer.Serialize(value));
    }

    [Fact]
    public void UriConverter() => AssertBuiltInConverter(new Uri("https://example.com/path?q=value", UriKind.Absolute));

    [Fact]
    public void EnumConverter() => AssertBuiltInConverter(DayOfWeek.Monday);

    [Fact]
    public void NullableConverter() => AssertBuiltInConverter<int?>(42);

    [Fact]
    public void CustomConverter_SerializeAndDeserialize()
    {
        var options = new YamlishSerializerOptions();
        options.Converters.Add(new TemperatureConverter());

        var content = YamlishSerializer.Serialize(new Weather { Temperature = new Temperature(21) }, options);
        var result = YamlishSerializer.Deserialize<Weather>("Temperature: 32 C", options);

        Assert.Equal("Temperature: 21 C", content);
        Assert.Equal(new Temperature(32), result?.Temperature);
    }

    [Fact]
    public void CustomConverters_OverrideBuiltInAndUseRuntimeType()
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
    public void ConverterFactory_SerializeAndDeserialize()
    {
        var options = new YamlishSerializerOptions();
        options.Converters.Add(new WrapperConverterFactory());

        var content = YamlishSerializer.Serialize(new Wrapper<int> { Value = 42 }, options);
        var result = YamlishSerializer.Deserialize<Wrapper<int>>("84", options);

        Assert.Equal("42", content);
        Assert.Equal(84, result?.Value);
    }

    [Fact]
    public void CustomConverters_FirstMatchWinsAndResolutionIsCached()
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

    private static void AssertBuiltInConverter<T>(T value)
    {
        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<T>(content);

        Assert.Equal(value, result);
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
}
