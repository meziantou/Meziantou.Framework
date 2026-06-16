using System.Collections;
using System.Collections.Specialized;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using Meziantou.Xunit;

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
    public void ByteArrayConverter()
    {
        byte[] value = [1, 2, 3];

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<byte[]>(content);

        Assert.Equal("AQID", content);
        Assert.Equal(value, result);
    }

    [Fact]
    public void BigIntegerConverter() => AssertBuiltInConverter(BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture));

    [Fact]
    public void BitArrayConverter()
    {
        var value = new BitArray(4);
        value[1] = true;

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<BitArray>(content);

        Assert.Equal("0100", content);
        Assert.Equal([false, true, false, false], result?.Cast<bool>());
    }

    [Fact]
    public void BitVector32Converter()
    {
        var value = new BitVector32(2);

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<BitVector32>(content);

        Assert.Equal("00000000000000000000000000000010", content);
        Assert.Equal(value.Data, result.Data);
    }

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
    public void HalfConverter() => AssertBuiltInConverter((Half)0.5);

    [Fact]
    public void Int128Converter() => AssertBuiltInConverter(Int128.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture));

    [Fact]
    public void UInt128Converter() => AssertBuiltInConverter(UInt128.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture));

    [Fact]
    public void ComplexConverter()
    {
        var value = new Complex(12, 34);

        AssertBuiltInConverter(value);
        Assert.Equal("<12; 34>", YamlishSerializer.Serialize(value));
    }

    [Fact]
    public void CultureInfoConverter()
    {
        if (!Meziantou.Xunit.TestEnvironment.IsGlobalizationInvariant())
        {
            AssertBuiltInConverter(CultureInfo.GetCultureInfo("en-US"));
        }

        var content = YamlishSerializer.Serialize(CultureInfo.InvariantCulture);
        var result = YamlishSerializer.Deserialize<CultureInfo>(content);

        Assert.Equal("Invariant Language (Invariant Country)", content);
        Assert.Same(CultureInfo.InvariantCulture, result);
    }

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
    public void UtcDateTimeConverters()
    {
        const string Content = "2000-01-01T12:00:00.0000000Z";

        var dateTime = YamlishSerializer.Deserialize<DateTime>(Content);
        var dateTimeOffset = YamlishSerializer.Deserialize<DateTimeOffset>(Content);

        Assert.Equal(new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc), dateTime);
        Assert.Equal(DateTimeKind.Utc, dateTime.Kind);
        Assert.Equal(Content, YamlishSerializer.Serialize(dateTime));
        Assert.Equal(new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero), dateTimeOffset);
        Assert.Equal(TimeSpan.Zero, dateTimeOffset.Offset);
        Assert.Equal(Content, YamlishSerializer.Serialize(dateTimeOffset));
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
    public void DBNullConverter()
    {
        AssertBuiltInConverter(DBNull.Value);
        Assert.Equal("null", YamlishSerializer.Serialize(DBNull.Value));
    }

    [Fact]
    public void HttpMethodConverter() => AssertBuiltInConverter(HttpMethod.Patch);

    [Fact]
    public void HttpStatusCodeConverter()
    {
        AssertBuiltInConverter(HttpStatusCode.NotFound);
        Assert.Equal("404 (NotFound)", YamlishSerializer.Serialize(HttpStatusCode.NotFound));
    }

    [Fact]
    public void MediaTypeHeaderValueConverter() => AssertBuiltInConverter(MediaTypeHeaderValue.Parse("application/json; charset=utf-8"));

    [Fact]
    public void MemoryByteConverter()
    {
        var value = new Memory<byte>([1, 2, 3]);

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<Memory<byte>>(content);

        Assert.Equal("AQID", content);
        Assert.Equal(value.ToArray(), result.ToArray());
    }

    [Fact]
    public void ReadOnlyMemoryByteConverter()
    {
        var value = new ReadOnlyMemory<byte>([1, 2, 3]);

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<ReadOnlyMemory<byte>>(content);

        Assert.Equal("AQID", content);
        Assert.Equal(value.ToArray(), result.ToArray());
    }

    [Fact]
    public void IPAddressConverter() => AssertBuiltInConverter(IPAddress.Parse("192.168.1.42"));

    [Fact]
    public void IPNetworkConverter() => AssertBuiltInConverter(IPNetwork.Parse("192.168.1.0/24"));

    [Fact]
    public void StringBuilderConverter()
    {
        var content = YamlishSerializer.Serialize(new StringBuilder("value"));
        var result = YamlishSerializer.Deserialize<StringBuilder>(content);

        Assert.Equal("value", content);
        Assert.Equal("value", result?.ToString());
    }

    [Fact]
    public void TypeConverter()
    {
        var content = YamlishSerializer.Serialize(typeof(Dictionary<string, int>));
        var result = YamlishSerializer.Deserialize<Type>(content);

        Assert.Equal(typeof(Dictionary<string, int>), result);
    }

    [Fact]
    public void VersionConverter() => AssertBuiltInConverter(new Version(1, 2, 3, 4));

    [Fact]
    public void StringWriterConverter()
    {
        using var value = new StringWriter(CultureInfo.InvariantCulture);
        value.Write("value");

        var content = YamlishSerializer.Serialize(value);
        using var result = YamlishSerializer.Deserialize<StringWriter>(content);

        Assert.Equal("value", content);
        Assert.Equal("value", result?.ToString());
    }

    [Fact]
    public void UnixDomainSocketEndPointConverter()
    {
        var value = new UnixDomainSocketEndPoint("/var/run/dummy.sock");

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<UnixDomainSocketEndPoint>(content);

        Assert.Equal(value.ToString(), result?.ToString());
    }

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
        options.Converters.Insert(0, new HexInt32Converter());
        options.Converters.Add(new TemperatureConverter());

        var integer = YamlishSerializer.Serialize(42, options);
        var model = YamlishSerializer.Serialize(new ObjectValue { Value = new Temperature(21) }, options);

        Assert.Equal("0x2A", integer);
        Assert.Equal("Value: 21 C", model);
    }

    [Fact]
    public void DefaultConverters_AreExposedAndCanBeRemoved()
    {
        var options = new YamlishSerializerOptions();
        var converter = Assert.Single(options.Converters, converter => converter.CanConvert(typeof(int)));

        Assert.True(options.Converters.Remove(converter));
        Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<int>("42", options));
    }

    [Fact]
    public void ConverterList_OrderControlsPrecedence()
    {
        var options = new YamlishSerializerOptions();
        options.Converters.Insert(0, new HexInt32Converter());

        Assert.Equal("0x2A", YamlishSerializer.Serialize(42, options));
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

#if NET11_0_OR_GREATER
    [Fact]
    public void CSharpUnionConverter_ObjectCase()
    {
        CSharpPet value = new CSharpDog("Rex");

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<CSharpPet>(content);

        Assert.Equal("""
            $type: CSharpDog
            Value:
              Name: Rex
            """, content, ignoreLineEndingDifferences: true);
        var dog = Assert.IsType<CSharpDog>(result.Value);
        Assert.Equal("Rex", dog.Name);
    }

    [Fact]
    public void CSharpUnionConverter_OtherObjectCase()
    {
        CSharpPet value = new CSharpCat("Felix");

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<CSharpPet>(content);

        Assert.Equal("""
            $type: CSharpCat
            Value:
              Name: Felix
            """, content, ignoreLineEndingDifferences: true);
        var cat = Assert.IsType<CSharpCat>(result.Value);
        Assert.Equal("Felix", cat.Name);
    }

    [Fact]
    public void CSharpUnionConverter_FullNameDiscriminator()
    {
        var content = $"""
            $type: {typeof(CSharpDog).FullName}
            Value:
              Name: Rex
            """;

        var result = YamlishSerializer.Deserialize<CSharpPet>(content);

        var dog = Assert.IsType<CSharpDog>(result.Value);
        Assert.Equal("Rex", dog.Name);
    }

    [Fact]
    public void CSharpUnionConverter_ScalarCase()
    {
        CSharpScalarUnion value = 42;

        var content = YamlishSerializer.Serialize(value);
        var result = YamlishSerializer.Deserialize<CSharpScalarUnion>(content);

        Assert.Equal("""
            $type: Int32
            Value: 42
            """, content, ignoreLineEndingDifferences: true);
        Assert.Equal(42, Assert.IsType<int>(result.Value));
    }

    [Fact]
    public void CSharpUnionConverter_UnknownDiscriminator_Throws()
    {
        var exception = Assert.Throws<FormatException>(() => YamlishSerializer.Deserialize<CSharpPet>("""
            $type: Unknown
            Value:
              Name: Rex
            """));

        Assert.Contains("Unknown", exception.Message, StringComparison.Ordinal);
    }
#endif

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

#if NET11_0_OR_GREATER
    private sealed record CSharpCat(string Name);

    private sealed record CSharpDog(string Name);

    private union CSharpPet(CSharpCat, CSharpDog);

    private union CSharpScalarUnion(int, string);
#endif
}
