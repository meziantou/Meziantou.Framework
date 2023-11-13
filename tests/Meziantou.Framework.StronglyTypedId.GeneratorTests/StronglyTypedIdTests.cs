using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using Newtonsoft.Json;
using Xunit;

namespace Meziantou.Framework.StronglyTypedId.GeneratorTests;

public sealed partial class StronglyTypedIdTests
{
    public static TheoryData<Type, string, object> GetData()
    {
        var now = DateTime.UtcNow;
        now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc); // MongoDB serializer truncates milliseconds. 

        return new TheoryData<Type, string, object>
        {
            { typeof(IdBigInteger), "FromBigInteger", BigInteger.Parse("123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890", CultureInfo.InvariantCulture) },
            { typeof(IdBoolean), "FromBoolean", true },
            { typeof(IdByte), "FromByte", (byte)42 },
            { typeof(IdDateTime), "FromDateTime", now },
            { typeof(IdDateTimeOffset), "FromDateTimeOffset", new DateTimeOffset(now, TimeSpan.Zero) },
            { typeof(IdDecimal), "FromDecimal", 42m },
            { typeof(IdDouble), "FromDouble", 42d },
            { typeof(IdGuid), "FromGuid", Guid.NewGuid() },
            { typeof(IdHalf), "FromHalf", Half.MaxValue },
            { typeof(IdInt16), "FromInt16", (short)42 },
            { typeof(IdInt32), "FromInt32", 42 },
            { typeof(IdInt64), "FromInt64", 42L },
#if NET7_0_OR_GREATER
            { typeof(IdInt128), "FromInt128", Int128.MaxValue },
#endif
            { typeof(IdSByte), "FromSByte", (sbyte)42 },
            { typeof(IdSingle), "FromSingle", 42f },
            { typeof(IdString), "FromString", "test" },
            { typeof(IdUInt16), "FromUInt16", (ushort) 42 },
            { typeof(IdUInt32), "FromUInt32", (uint) 42 },
            { typeof(IdUInt64), "FromUInt64", (ulong) 42 },
#if NET7_0_OR_GREATER
            { typeof(IdUInt128), "FromUInt128", UInt128.MaxValue },
#endif

            { typeof(IdClassBoolean), "FromBoolean", true },
            { typeof(IdClassByte), "FromByte", (byte)42 },
            { typeof(IdClassDateTime), "FromDateTime", now },
            { typeof(IdClassDateTimeOffset), "FromDateTimeOffset", new DateTimeOffset(now, TimeSpan.Zero) },
            { typeof(IdClassDecimal), "FromDecimal", 42m },
            { typeof(IdClassDouble), "FromDouble", 42d },
            { typeof(IdClassGuid), "FromGuid", Guid.NewGuid() },
            { typeof(IdClassInt16), "FromInt16", (short)42 },
            { typeof(IdClassInt32), "FromInt32", 42 },
            { typeof(IdClassInt64), "FromInt64", 42L },
            { typeof(IdClassSByte), "FromSByte", (sbyte)42 },
            { typeof(IdClassSingle), "FromSingle", 42f },
            { typeof(IdClassString), "FromString", "test" },
            { typeof(IdClassUInt16), "FromUInt16", (ushort) 42 },
            { typeof(IdClassUInt32), "FromUInt32", (uint) 42 },
            { typeof(IdClassUInt64), "FromUInt64", (ulong) 42 },

            { typeof(IdRecordInt32), "FromInt32", 42 },
            { typeof(IdRecordStructInt32), "FromInt32", 42 },

            { typeof(BsonObjectId), "FromObjectId", ObjectId.GenerateNewId() },
        };
    }

    [Theory]
    [MemberData(nameof(GetData))]
    public void ValidateType(Type type, string fromMethodName, object value)
    {
        var from = (MethodInfo)type.GetMember(fromMethodName).Single();
        var instance = from.Invoke(null, new object[] { value });

        // System.Text.Json
        {
            var json = System.Text.Json.JsonSerializer.Serialize(instance);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize(json, type);
            var deserialized2 = System.Text.Json.JsonSerializer.Deserialize(@"{ ""a"": {}, ""b"": false, ""Value"": " + json + " }", type);

            deserialized.Should().Be(instance);
            deserialized2.Should().Be(instance);
        }

        // Newtonsoft.Json
        {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(instance);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject(json, type);
            var deserialized2 = Newtonsoft.Json.JsonConvert.DeserializeObject(@"{ ""a"": {}, ""b"": false, ""Value"": " + json + " }", type);

            deserialized.Should().Be(instance);
            deserialized2.Should().Be(instance);
        }

        // TypeConverter ToString - FromString
        {
            var converter = TypeDescriptor.GetConverter(type);
            converter.CanConvertTo(typeof(string)).Should().BeTrue();
            var str = converter.ConvertTo(instance, typeof(string))!;

            converter.CanConvertFrom(typeof(string)).Should().BeTrue();
            converter.ConvertFrom(str).Should().Be(instance);
        }

        // BsonConverter
        {
            var json = BsonExtensionMethods.ToJson(instance, type);
            var deserialized = BsonSerializer.Deserialize(json, type);

            deserialized.Should().Be(instance);
        }

        var defaultValue = value.GetType() == typeof(string) ? null : Activator.CreateInstance(value.GetType());
        var defaultInstance = from.Invoke(null, new object?[] { defaultValue });
        defaultInstance.Should().NotBe(instance);
    }

    [Fact]
    public void TestNullableClass()
    {
        IdClassInt32 value = IdClassInt32.FromInt32(42);
        (value == null).Should().BeFalse();
    }

    [Fact]
    public void DisableSomeGenerator()
    {
        typeof(IdRecordInt32WithoutSystemTextJson).GetCustomAttribute<System.Text.Json.Serialization.JsonConverterAttribute>().Should().BeNull();
    }

    [Fact]
    public void CodeGeneratedAttribute()
    {
        typeof(IdInt32WithCodeGeneratedAttribute).GetMethod("FromInt32")!.GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>().Should().NotBeNull();
        typeof(IdInt32WithoutCodeGeneratedAttribute).GetMethod("FromInt32")!.GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>().Should().BeNull();
    }

#if NET7_0_OR_GREATER
    [Fact]
    public void IParsable_Int32()
    {
        Parse<IdInt32>("test");
        static void Parse<T>(string _) where T : IParsable<IdInt32>
        { }
    }

    [Fact]
    public void ISpanParsable_Int32()
    {
        Parse<IdInt32>("test");
        static void Parse<T>(string _) where T : ISpanParsable<IdInt32>
        { }
    }
#endif

    [Fact]
    public void String_Parse()
    {
        IdString.Parse("test").Value.Should().Be("test");
    }

    [Fact]
    public void String_Parse_ReadOnlySpan()
    {
        IdString.Parse("test".AsSpan()).Value.Should().Be("test");
    }

    [Fact]
    public void String_TryParse_ReadOnlySpan()
    {
        IdString.TryParse("test".AsSpan(), out var value).Should().BeTrue();
        value.Value.Should().Be("test");
    }

    [Fact]
    public void String_TryParse_Null()
    {
        IdString.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void String_Parse_Null()
    {
        var action = () => IdString.Parse(null!);
        action.Should().Throw<FormatException>();
    }

    [Fact]
    public void MongoDB_NullableStringId_ParseNull()
    {
        var value = BsonClone<IdString?>(null);
        value.Should().BeNull();
    }

    [Fact]
    public void SystemTextJson_NullableStringId_ParseNull()
    {
        var value = System.Text.Json.JsonSerializer.Deserialize<IdString?>("null");
        value.Should().BeNull();
    }

    [Fact]
    public void NewtonsoftJson_NullableStringId_ParseNull()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdString?>("null");
        value.Should().BeNull();
    }

    [Fact]
    public void NewtonsoftJson_Int32_ParseNull()
    {
        var action = () => Newtonsoft.Json.JsonConvert.DeserializeObject<IdInt32>("null");
        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void NewtonsoftJson_IdRecordStructInt32_ParseNull()
    {
        var action = () => Newtonsoft.Json.JsonConvert.DeserializeObject<IdRecordStructInt32>("null");
        action.Should().Throw<JsonException>();
    }

    [Fact]
    public void NewtonsoftJson_NullableInt32_ParseNull()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdInt32?>("null");
        value.Should().BeNull();
    }

    [Fact]
    public void NewtonsoftJson_NullableInt32_ParseValue()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdInt32?>("42");
        value!.Value.Value.Should().Be(42);
    }

#if NET7_0_OR_GREATER
    [Fact]
    public void NewtonsoftJson_Int128_ParseStringValue()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdInt128?>("\"170141183460469231731687303715884105727\"");
        value!.Value.Value.Should().Be(Int128.Parse("170141183460469231731687303715884105727", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NewtonsoftJson_Int128_ParseLargeIntValue()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdInt128?>("170141183460469231731687303715884105727");
        value!.Value.Value.Should().Be(Int128.Parse("170141183460469231731687303715884105727", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NewtonsoftJson_Int128_ParseIntValue()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdInt128?>("42");
        value!.Value.Value.Should().Be(Int128.Parse("42", CultureInfo.InvariantCulture));
    }
    [Fact]
    public void NewtonsoftJson_UInt128_ParseStringValue()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdUInt128?>("\"340282366920938463463374607431768211455\"");
        value!.Value.Value.Should().Be(UInt128.Parse("340282366920938463463374607431768211455", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NewtonsoftJson_UInt128_ParseLargeIntValue()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdUInt128?>("340282366920938463463374607431768211455");
        value!.Value.Value.Should().Be(UInt128.Parse("340282366920938463463374607431768211455", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void NewtonsoftJson_UInt128_ParseIntValue()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdUInt128?>("42");
        value!.Value.Value.Should().Be(UInt128.Parse("42", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SystemTextJson_Int128_ParseStringValue()
    {
        var value = System.Text.Json.JsonSerializer.Deserialize<IdInt128?>("\"170141183460469231731687303715884105727\"");
        value!.Value.Value.Should().Be(Int128.Parse("170141183460469231731687303715884105727", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SystemTextJson_Int128_ParseLargeIntValue()
    {
        var value = System.Text.Json.JsonSerializer.Deserialize<IdInt128?>("170141183460469231731687303715884105727");
        value!.Value.Value.Should().Be(Int128.Parse("170141183460469231731687303715884105727", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SystemTextJson_Int128_ParseIntValue()
    {
        var value = System.Text.Json.JsonSerializer.Deserialize<IdInt128?>("42");
        value!.Value.Value.Should().Be(Int128.Parse("42", CultureInfo.InvariantCulture));
    }
    [Fact]
    public void SystemTextJson_UInt128_ParseStringValue()
    {
        var value = System.Text.Json.JsonSerializer.Deserialize<IdUInt128?>("\"340282366920938463463374607431768211455\"");
        value!.Value.Value.Should().Be(UInt128.Parse("340282366920938463463374607431768211455", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SystemTextJson_UInt128_ParseLargeIntValue()
    {
        var value = System.Text.Json.JsonSerializer.Deserialize<IdUInt128?>("340282366920938463463374607431768211455");
        value!.Value.Value.Should().Be(UInt128.Parse("340282366920938463463374607431768211455", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SystemTextJson_UInt128_ParseIntValue()
    {
        var value = System.Text.Json.JsonSerializer.Deserialize<IdUInt128?>("42");
        value!.Value.Value.Should().Be(UInt128.Parse("42", CultureInfo.InvariantCulture));
    }
#endif

    [Fact]
    public void NewtonsoftJson_IdClassString_ParseNull()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdClassString>("null");
        value.Should().BeNull();
    }

    [Fact]
    public void NewtonsoftJson_IdClassString_ParseString()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdClassString>("\"test\"");
        value.Should().Be(IdClassString.FromString("test"));
    }

    [Fact]
    public void IdClassString_ToString_Null()
    {
        var value = IdClassString.FromString(null!);
        value.ToString().Should().Be("IdClassString { Value = <null> }");
    }

    [Fact]
    public void IdClassString_ToString_NonNull()
    {
        var value = IdClassString.FromString("test");
        value.ToString().Should().Be("IdClassString { Value = test }");
    }

    [Fact]
    public void IdInt32_ToString_NonNull()
    {
        var value = IdInt32.FromInt32(-1);
        value.ToString().Should().Be("IdInt32 { Value = -1 }");
    }

    [Fact]
    public void ImplementStronglyTypedIdInterface()
    {
        var value = IdInt32.FromInt32(1);

        // The following should compile
        Assert.IsAssignableFrom<IStronglyTypedId>(value);
        Assert.IsAssignableFrom<IStronglyTypedId<int>>(value);
    }

    [Fact]
    public void IdInt32Comparable_ImplementsMembers()
    {
        var value = IdInt32Comparable.FromInt32(1);

        // The following should compile
        Assert.IsAssignableFrom<IComparable>(value);
        Assert.IsAssignableFrom<IComparable<IdInt32Comparable>>(value);
    }

    [Fact]
    public void IdInt32ComparableOfT_ImplementsMembers()
    {
        var value1 = IdInt32ComparableOfT.FromInt32(1);
        var value2 = IdInt32ComparableOfT.FromInt32(2);

        // The following should compile
        Assert.IsAssignableFrom<IComparable>(value1);
        Assert.IsAssignableFrom<IComparable<IdInt32ComparableOfT>>(value1);
        Assert.True(value1 < value2);
        Assert.True(value1 <= value2);
        Assert.False(value1 > value2);
        Assert.False(value1 >= value2);
    }

    [Fact]
    public void Bson_Guid_Class_Null()
    {
        IdClassGuid instance = null;
        var clone = BsonClone(instance);
        Assert.Null(clone);
    }
    
    [Fact]
    public void Bson_Guid_Class_Empty()
    {
        var instance = IdClassGuid.FromGuid(Guid.Empty);
        var clone = BsonClone(instance);
        Assert.Equal(Guid.Empty, clone.Value);
    }

    [return: NotNullIfNotNull(nameof(value))]
    private static T? BsonClone<T>(T value)
    {
        using var stream = new MemoryStream();
        using (var writer = new BsonBinaryWriter(stream))
        {
            BsonSerializer.Serialize(writer, new Wrapper<T> { Value = value });
        }

        return BsonSerializer.Deserialize<Wrapper<T>>(stream.ToArray()).Value;
    }

    // BsonSerializer cannot write non-dictionary-like object, such as collection, at root level. The dummy object bypass this limitation.
    private sealed class Wrapper<T>
    {
        public T? Value { get; set; }
    }

#nullable enable
    [StronglyTypedId(typeof(bool))]
    private partial struct IdBoolean { }

    [StronglyTypedId(typeof(byte))]
    private partial struct IdByte { }

    [StronglyTypedId(typeof(DateTime))]
    private partial struct IdDateTime { }

    [StronglyTypedId(typeof(DateTimeOffset))]
    private partial struct IdDateTimeOffset { }

    [StronglyTypedId(typeof(decimal))]
    private partial struct IdDecimal { }

    [StronglyTypedId(typeof(double))]
    private partial struct IdDouble { }

    [StronglyTypedId(typeof(Guid))]
    private partial struct IdGuid { }

    [StronglyTypedId(typeof(short))]
    private partial struct IdInt16 { }

    [StronglyTypedId(typeof(int))]
    private partial struct IdInt32 { }

    [StronglyTypedId(typeof(long))]
    private partial struct IdInt64 { }

#if NET7_0_OR_GREATER
    [StronglyTypedId(typeof(Int128))]
    private partial struct IdInt128 { }
#endif

    [StronglyTypedId(typeof(BigInteger))]
    private partial struct IdBigInteger { }

    [StronglyTypedId(typeof(Half))]
    private partial struct IdHalf { }

    [StronglyTypedId(typeof(sbyte))]
    private partial struct IdSByte { }

    [StronglyTypedId(typeof(float))]
    private partial struct IdSingle { }

    [StronglyTypedId(typeof(string))]
    private partial struct IdString { }

    [StronglyTypedId(typeof(ushort))]
    private partial struct IdUInt16 { }

    [StronglyTypedId(typeof(uint))]
    private partial struct IdUInt32 { }

    [StronglyTypedId(typeof(ulong))]
    private partial struct IdUInt64 { }

#if NET7_0_OR_GREATER
    [StronglyTypedId(typeof(UInt128))]
    private partial struct IdUInt128 { }
#endif

    [StronglyTypedId(typeof(bool))]
    private sealed partial class IdClassBoolean { }

    [StronglyTypedId(typeof(byte))]
    private sealed partial class IdClassByte { }

    [StronglyTypedId(typeof(DateTime))]
    private sealed partial class IdClassDateTime { }

    [StronglyTypedId(typeof(DateTimeOffset))]
    private sealed partial class IdClassDateTimeOffset { }

    [StronglyTypedId(typeof(decimal))]
    private sealed partial class IdClassDecimal { }

    [StronglyTypedId(typeof(double))]
    private sealed partial class IdClassDouble { }

    [StronglyTypedId(typeof(Guid))]
    private sealed partial class IdClassGuid { }

    [StronglyTypedId(typeof(short))]
    private sealed partial class IdClassInt16 { }

    [StronglyTypedId(typeof(int))]
    private sealed partial class IdClassInt32 { }

    [StronglyTypedId(typeof(long))]
    private sealed partial class IdClassInt64 { }

    [StronglyTypedId(typeof(sbyte))]
    private sealed partial class IdClassSByte { }

    [StronglyTypedId(typeof(float))]
    private sealed partial class IdClassSingle { }

    [StronglyTypedId(typeof(string))]
    private sealed partial class IdClassString { }

    [StronglyTypedId(typeof(ushort))]
    private sealed partial class IdClassUInt16 { }

    [StronglyTypedId(typeof(uint))]
    private sealed partial class IdClassUInt32 { }

    [StronglyTypedId(typeof(ulong))]
    private sealed partial class IdClassUInt64 { }

    [StronglyTypedId(typeof(int))]
    private partial struct IdCtorDefined
    {
        public IdCtorDefined(int value)
        {
            _value = value;
        }
    }

    [StronglyTypedId(typeof(int))]
    private partial struct IdToStringDefined
    {
        public override readonly string ToString() => "";
    }

    [StronglyTypedId(typeof(int))]
    private sealed partial record IdRecordInt32
    {
    }

    [StronglyTypedId(typeof(int))]
    private partial record struct IdRecordStructInt32
    {
    }

    [StronglyTypedId(typeof(int), generateSystemTextJsonConverter: false)]
    private sealed partial record IdRecordInt32WithoutSystemTextJson
    {
    }

    [StronglyTypedId(typeof(int), addCodeGeneratedAttribute: true)]
    private sealed partial record IdInt32WithCodeGeneratedAttribute
    {
    }

    [StronglyTypedId(typeof(int), addCodeGeneratedAttribute: false)]
    private sealed partial record IdInt32WithoutCodeGeneratedAttribute
    {
    }

    [StronglyTypedId(typeof(int))]
    private sealed partial class IdInt32Comparable : IComparable
    {
    }

    [StronglyTypedId(typeof(int))]
    private sealed partial class IdInt32ComparableOfT : IComparable<IdInt32ComparableOfT>
    {
    }

    [StronglyTypedId(typeof(int))]
    private partial class IdInt32Base
    {
    }
    
    [StronglyTypedId(typeof(MongoDB.Bson.ObjectId))]
    private sealed partial class BsonObjectId
    {
    }

    private sealed partial class IdInt32Derived : IdInt32Base
    {
        public IdInt32Derived() : base(0)
        {
        }
    }

    private sealed partial class SampleContainer
    {
        [StronglyTypedId(typeof(int))]
        private readonly partial struct IdInt32Contained
        {
        }
    }
}
