using System.ComponentModel;
using System.Reflection;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
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
            { typeof(IdBoolean), "FromBoolean", true },
            { typeof(IdByte), "FromByte", (byte)42 },
            { typeof(IdDateTime), "FromDateTime", now },
            { typeof(IdDateTimeOffset), "FromDateTimeOffset", new DateTimeOffset(now, TimeSpan.Zero) },
            { typeof(IdDecimal), "FromDecimal", 42m },
            { typeof(IdDouble), "FromDouble", 42d },
            { typeof(IdGuid), "FromGuid", Guid.NewGuid() },
            { typeof(IdInt16), "FromInt16", (short)42 },
            { typeof(IdInt32), "FromInt32", 42 },
            { typeof(IdInt64), "FromInt64", 42L },
            { typeof(IdSByte), "FromSByte", (sbyte)42 },
            { typeof(IdSingle), "FromSingle", 42f },
            { typeof(IdString), "FromString", "test" },
            { typeof(IdUInt16), "FromUInt16", (ushort) 42 },
            { typeof(IdUInt32), "FromUInt32", (uint) 42 },
            { typeof(IdUInt64), "FromUInt64", (ulong) 42 },

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
            var str = converter.ConvertTo(instance, typeof(string));

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
        var defaultInstance = from.Invoke(null, new object[] { defaultValue });
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
        typeof(IdInt32WithCodeGeneratedAttribute).GetMethod("FromInt32").GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>().Should().NotBeNull();
        typeof(IdInt32WithoutCodeGeneratedAttribute).GetMethod("FromInt32").GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>().Should().BeNull();
    }

#if NET7_0_OR_GREATER
    [Fact]
    public void IParsable_Int32()
    {
        Parse<IdInt32>("test");
        void Parse<T>(string _) where T : IParsable<IdInt32> { }
    }

    [Fact]
    public void ISpanParsable_Int32()
    {
        Parse<IdInt32>("test");
        void Parse<T>(string _) where T : ISpanParsable<IdInt32> { }
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
        var action = () => IdString.Parse(null);
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
    public void NewtonsoftJson_NullableInt32_ParseNull()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdInt32?>("null");
        value.Should().BeNull();
    }

    [Fact]
    public void NewtonsoftJson_NullableInt32_ParseValue()
    {
        var value = Newtonsoft.Json.JsonConvert.DeserializeObject<IdInt32?>("42");
        value.Value.Value.Should().Be(42);
    }

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

 
    

    private static T BsonClone<T>(T value)
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
        public T Value { get; set; }
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
    private partial class IdInt32Base
    {
    }

    private sealed partial class IdInt32Derived : IdInt32Base
    {
        public IdInt32Derived() : base(0)
        {
        }
    }
}
