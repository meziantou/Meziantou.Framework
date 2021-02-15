using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Meziantou.Framework.StronglyTypedId.GeneratorTests
{
    public sealed partial class StronglyTypedIdTests
    {
        public static TheoryData<Type, string, object> GetData()
        {
            return new TheoryData<Type, string, object>
            {
                { typeof(IdBoolean), "FromBoolean", true },
                { typeof(IdByte), "FromByte", (byte)42 },
                { typeof(IdDateTime), "FromDateTime", DateTime.UtcNow },
                { typeof(IdDateTimeOffset), "FromDateTimeOffset", DateTimeOffset.UtcNow },
                { typeof(IdDecimal), "FromDecimal", 42m },
                { typeof(IdDouble), "FromDouble", 42d },
                { typeof(IdGuid), "FromGuid", Guid.NewGuid() },
                { typeof(IdInt16), "FromInt16", (short)42 },
                { typeof(IdInt32), "FromInt32", 42 },
                { typeof(IdInt64), "FromInt64", 42L },
                { typeof(IdSByte), "FromSByte", (sbyte)42 },
                { typeof(IdSingle), "FromSingle", 42f },
                { typeof(IdString), "FromString", "test" },
                { typeof(IdUInt16), "FromUInt16", (ushort) 42},
                { typeof(IdUInt32), "FromUInt32", (uint) 42},
                { typeof(IdUInt64), "FromUInt64", (ulong) 42},
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

                Assert.Equal(instance, deserialized);
                Assert.Equal(instance, deserialized2);
            }

            // Newtonsoft.Json
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(instance);
                var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject(json, type);
                var deserialized2 = Newtonsoft.Json.JsonConvert.DeserializeObject(@"{ ""a"": {}, ""b"": false, ""Value"": " + json + " }", type);

                Assert.Equal(instance, deserialized);
                Assert.Equal(instance, deserialized2);
            }

            // TypeConverter ToString - FromString
            {
                var converter = TypeDescriptor.GetConverter(type);
                Assert.True(converter.CanConvertTo(typeof(string)));
                var str = converter.ConvertTo(instance, typeof(string));

                Assert.True(converter.CanConvertFrom(typeof(string)));
                var converted = converter.ConvertFrom(str);

                Assert.Equal(instance, converted);
            }

            var defaultValue = value.GetType() == typeof(string) ? null : Activator.CreateInstance(value.GetType());
            var defaultInstance = from.Invoke(null, new object[] { defaultValue });
            Assert.NotEqual(instance, defaultInstance);
        }

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
    }
}
