#pragma warning disable CA1720
#pragma warning disable CA1814
#pragma warning disable CA3075
#pragma warning disable MA0009
#pragma warning disable MA0110
#pragma warning disable SYSLIB1045
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.Numerics;
using System.Xml;
using Meziantou.Framework.HumanReadableSerializer.FSharp.Tests;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.HumanReadable.Tests;

public sealed partial class SerializerTests
{
    private sealed record Validation
    {
        public object Subject { get; init; }
        public string Expected { get; init; }
        public HumanReadableSerializerOptions Options { get; init; }
    }

    private static void AssertSerialization(object obj, string expected)
    {
        AssertSerialization(obj, options: null, expected);
    }

    private static void AssertSerialization(object obj, HumanReadableSerializerOptions options, string expected)
    {
        var text = HumanReadableSerializer.Serialize(obj, options);
        Assert.Equal(expected, text, ignoreLineEndingDifferences: true);
    }

    private static void AssertSerialization(Validation validation)
    {
        AssertSerialization(validation.Subject, validation.Options, validation.Expected);
    }

    [Fact]
    public void FSharp_DiscriminatedUnion_Rectangle()
    {
        AssertSerialization(Shape.NewRectangle(1, 2), """
            Tag: Rectangle
            width: 1
            length: 2
            """);
    }
    
    [Fact]
    public void FSharp_DiscriminatedUnion_Circle()
    {
        AssertSerialization(Shape.NewCircle(1), """
            Tag: Circle
            radius: 1
            """);
    }
    
    [Fact]
    public void CultureInfo_Invariant()
        => AssertSerialization(CultureInfo.InvariantCulture, "Invariant Language (Invariant Country)");

    [RunIfFact(globalizationMode: FactInvariantGlobalizationMode.Disabled)]
    public void CultureInfo_EnUs()
        => AssertSerialization(CultureInfo.GetCultureInfo("en-US"), "en-US");

    [Fact]
    public void SerializeNullableOfInt32_Null()
        => AssertSerialization(new { Int32 = (int?)null }, "Int32: <null>");

    [Fact]
    public void SerializeNullableOfInt32_NotNull()
        => AssertSerialization(new { Int32 = (int?)1 }, "Int32: 1");

    [Fact]
    public void SerializeArray_Empty()
        => AssertSerialization(Array.Empty<string>(), "[]");

    [Fact]
    public void IEnumerableKeyValuePairStringObject_Empty()
        => AssertSerialization(Array.Empty<KeyValuePair<string, object>>(), "{}");

    [Fact]
    public void IEnumerableKeyValuePairStringObject_Array()
    {
        AssertSerialization(new Validation
        {
            Subject = new KeyValuePair<string, object>[]
            {
                new KeyValuePair<string, object>("A", 10),
                new KeyValuePair<string, object>("B", 20),
            },
            Expected = """
                A: 10
                B: 20
                """,
        });
    }

    [Fact]
    public void IEnumerableKeyValuePairStringObject_Dictionary()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["A"] = 10,
                ["B"] = 20,
            },
            Expected = """
                A: 10
                B: 20
                """,
        });
    }


    [Fact]
    public void IEnumerableKeyValuePairObjectObject_EmptyArray()
    {
        AssertSerialization(new Validation
        {
            Subject = Array.Empty<KeyValuePair<object, object>>(),
            Expected = "[]",
        });
    }

    [Fact]
    public void IEnumerableKeyValuePairObjectObject_Array()
    {
        AssertSerialization(new Validation
        {
            Subject = new KeyValuePair<object, object>[]
            {
                new KeyValuePair<object, object>("A", 10),
                new KeyValuePair<object, object>("B", 20),
            },
            Expected = """
                - Key: A
                  Value: 10
                - Key: B
                  Value: 20
                """,
        });
    }

    [Fact]
    public void IEnumerableKeyValuePairObjectObject_Dictionary()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<object, object>
            {
                ["A"] = 10,
                ["B"] = 20,
            },
            Expected = """
                - Key: A
                  Value: 10
                - Key: B
                  Value: 20
                """,
        });
    }

    [Fact]
    public void DictionaryInt32String()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<int, string>
            {
                [1] = "10",
                [2] = "20",
            },
            Expected = """
                - Key: 1
                  Value: 10
                - Key: 2
                  Value: 20
                """,
        });
    }

    [Fact]
    public void ArrayInt32()
    {
        AssertSerialization(new Validation
        {
            Subject = new[] { 1, 2, 3 },
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void ArrayInt32Array()
    {
        AssertSerialization(new Validation
        {
            Subject = new int[][] { new[] { 1, 2, 3 }, new[] { 4, 5, 6 } },
            Expected = """
                - - 1
                  - 2
                  - 3
                - - 4
                  - 5
                  - 6
                """,
        });
    }

    [Fact]
    public void ArrayObject()
    {
        AssertSerialization(new Validation
        {
            Subject = new object[] { 1, 2, 3 },
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void MultiDimensionalArrayInt32()
    {
        var data = new int[1, 2, 3];
        data[0, 0, 0] = 1;
        data[0, 0, 1] = 2;
        data[0, 0, 2] = 3;
        data[0, 1, 0] = 4;
        data[0, 1, 1] = 5;
        data[0, 1, 2] = 6;

        AssertSerialization(new Validation
        {
            Subject = data,
            Expected = """
                - [0, 0, 0]: 1
                - [0, 0, 1]: 2
                - [0, 0, 2]: 3
                - [0, 1, 0]: 4
                - [0, 1, 1]: 5
                - [0, 1, 2]: 6
                """,
        });
    }

    [Fact]
    public void ListInt32()
    {
        AssertSerialization(new Validation
        {
            Subject = new List<int> { 1, 2, 3 },
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void Enumerable_YieldReturnInt32()
    {
        AssertSerialization(new Validation
        {
            Subject = TypedYield(),
            Expected = """
                - 1
                - 2
                - 3
                """,
        });

        static IEnumerable<int> TypedYield()
        {
            yield return 1;
            yield return 2;
            yield return 3;
        }
    }

    [Fact]
    public void EnumerableRange()
    {
        AssertSerialization(new Validation
        {
            Subject = Enumerable.Range(1, 3),
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void Enumerable_YieldReturn()
    {
        AssertSerialization(new Validation
        {
            Subject = TypedYield(),
            Expected = """
                - 1
                - 2
                - 3
                """,
        });

        static IEnumerable TypedYield()
        {
            yield return 1;
            yield return 2;
            yield return 3;
        }

    }

    [Fact]
    public void ListObject()
    {
        AssertSerialization(new Validation
        {
            Subject = new List<object> { 1, 2, 3 },
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void UseTypeConverter()
    {
        AssertSerialization(new Validation
        {
            Subject = new CustomTypeConverter(),
            Expected = "converter",
        });
    }

    [Fact]
    public void UseIConvertible()
    {
        AssertSerialization(new Validation
        {
            Subject = new CustomConvertible(),
            Expected = "convertible",
        });
    }

    [Fact]
    public void SerializeObjectGraph()
    {
        AssertSerialization(new Validation
        {
            Subject = new
            {
                A = 1,
                B = 2,
                C = new
                {
                    D = 3,
                    E = 4,
                },
                F = new object(),
                G = new { },
                H = 7,
            },
            Expected = """
            A: 1
            B: 2
            C:
              D: 3
              E: 4
            F: {}
            G: {}
            H: 7
            """,
        });
    }

    [Fact]
    public void Boolean_True() => AssertSerialization(true, "true");

    [Fact]
    public void Boolean_False() => AssertSerialization(false, "false");

    [Fact]
    public void Byte() => AssertSerialization((byte)1, "1");

    [Fact]
    public void Sbyte() => AssertSerialization((sbyte)-1, "-1");

    [Fact]
    public void Short() => AssertSerialization((short)-1, "-1");

    [Fact]
    public void UShort() => AssertSerialization((ushort)1, "1");


    [Fact]
    public void Int32() => AssertSerialization(-1, "-1");

    [Fact]
    public void UInt32() => AssertSerialization(1u, "1");

    [Fact]
    public void Int64() => AssertSerialization(-1L, "-1");

    [Fact]
    public void UInt64() => AssertSerialization(1uL, "1");

    [Fact]
    public void IntPtr() => AssertSerialization((IntPtr)(-1), "-1");

    [Fact]
    public void UIntPtr() => AssertSerialization((UIntPtr)1, "1");

#if NET7_0_OR_GREATER
    [Fact]
    public void Int128() => AssertSerialization(new Int128(123, 456), "2268949521066274849224");
#endif

#if NET7_0_OR_GREATER
    [Fact]
    public void UInt128() => AssertSerialization(new UInt128(123, 456), "2268949521066274849224");
#endif

    [Fact]
    public void BigInteger() => AssertSerialization(new BigInteger(12), "12");

    [Fact]
    public void Complex() => AssertSerialization(new Complex(12, 34), "<12; 34>");

#if NET5_0_OR_GREATER
    [Fact]
    public void Half() => AssertSerialization((Half)0.5, "0.5");
#endif

    [Fact]
    public void Single() => AssertSerialization(-5.30f, "-5.3");

    [Fact]
    public void Double() => AssertSerialization(-5.30d, "-5.3");

    [Fact]
    public void Decimal() => AssertSerialization(-5.30m, "-5.30");

    [Fact]
    public void Char() => AssertSerialization('c', "c");

    [Fact]
    public void String() => AssertSerialization("str", "str");

    [Fact]
    public void String_MultiLine() => AssertSerialization("line1\nline2", "line1\nline2");

    [Fact]
    public void String_MultiLine_Indented() => AssertSerialization(new { A = "line1\nline2" }, """"
        A:
          line1
          line2
        """");

    [Fact]
    public void String_MultiLine_InArray() => AssertSerialization(new string[] { "line1\nline2", "line3" }, """"
        - line1
          line2
        - line3
        """");

    [Fact]
    public void ByteArray() => AssertSerialization(new byte[] { 1, 2, 3 }, "AQID");

    [Fact]
    public void DateTime_Utc() => AssertSerialization(new DateTime(2123, 4, 5, 6, 7, 8, DateTimeKind.Utc), "2123-04-05T06:07:08Z");

    [Fact]
    public void DateTime_Local()
    {
        var currentUtcOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow);
        AssertSerialization(new DateTime(2123, 4, 5, 6, 7, 8, DateTimeKind.Local), "2123-04-05T06:07:08" + (currentUtcOffset < TimeSpan.Zero ? "-" : "+") + currentUtcOffset.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DateTime_Unspecified() => AssertSerialization(new DateTime(2123, 4, 5, 6, 7, 8, DateTimeKind.Unspecified), "2123-04-05T06:07:08");

    [Fact]
    public void DateTimeOffset_Zero() => AssertSerialization(new DateTimeOffset(2123, 4, 5, 6, 7, 8, TimeSpan.Zero), "2123-04-05T06:07:08+00:00");

    [Fact]
    public void DateTimeOffset_PositiveOffset() => AssertSerialization(new DateTimeOffset(2123, 4, 5, 6, 7, 8, TimeSpan.FromHours(5)), "2123-04-05T06:07:08+05:00");

    [Fact]
    public void DateTimeOffset_NegativeOffset() => AssertSerialization(new DateTimeOffset(2123, 4, 5, 6, 7, 8, TimeSpan.FromHours(-5)), "2123-04-05T06:07:08-05:00");

    [Fact]
    public void Timespan_HoursMinutesSeconds() => AssertSerialization(new TimeSpan(1, 2, 3), "01:02:03");

    [Fact]
    public void Timespan_ZeroDay_HoursMinutesSeconds() => AssertSerialization(new TimeSpan(0, 1, 2, 3), "01:02:03");

    [Fact]
    public void Timespan_DaysHoursMinutesSeconds() => AssertSerialization(new TimeSpan(1, 2, 3, 4), "1.02:03:04");

    [Fact]
    public void Timespan_DaysHoursMinutesSecondsMilliseconds() => AssertSerialization(new TimeSpan(1, 2, 3, 4, 5), "1.02:03:04.0050000");

#if NET7_0_OR_GREATER
    [Fact]
    public void Timespan_DaysHoursMinutesSecondsMillisecondsMicroseconds() => AssertSerialization(new TimeSpan(1, 2, 3, 4, 5, 6), "1.02:03:04.0050060");
#endif

#if NET6_0_OR_GREATER
    [Fact]
    public void DateOnly() => AssertSerialization(new DateOnly(2123, 4, 5), "2123-04-05");
#endif

    [Fact]
    public void Guid() => AssertSerialization(System.Guid.Empty, "00000000-0000-0000-0000-000000000000");

    [Fact]
    public void Uri_Relative() => AssertSerialization(new Uri("abc", UriKind.RelativeOrAbsolute), "abc");

    [Fact]
    public void Uri_StartsWithSlash() => AssertSerialization(new Uri("/abc", UriKind.RelativeOrAbsolute), "/abc");

    [Fact]
    public void Uri_Absolute() => AssertSerialization(new Uri("http://example.com/abc?test=a#anchor"), "http://example.com/abc?test=a#anchor");

    [Fact]
    public void Version_TwoComponents() => AssertSerialization(new Version(1, 2), "1.2");

    [Fact]
    public void Version_ThreeComponents() => AssertSerialization(new Version(1, 2, 3), "1.2.3");

    [Fact]
    public void Version_FourComponents() => AssertSerialization(new Version(1, 2, 3, 4), "1.2.3.4");

    [Fact]
    public void Enum_Defined() => AssertSerialization(DayOfWeek.Monday, "Monday");

    [Fact]
    public void Enum_Undefined() => AssertSerialization((DayOfWeek)17, "17");

    [Fact]
    public void EnumFlags_Defined() => AssertSerialization(CommandBehavior.SingleResult | CommandBehavior.KeyInfo, "SingleResult, KeyInfo");

    [Fact]
    public void EnumFlags_Undefined() => AssertSerialization(CommandBehavior.SingleResult | (CommandBehavior)580, "581");

    [Fact]
    public void DBNull() => AssertSerialization(System.DBNull.Value, "<null>");

    [Fact]
    public void XmlDocument()
    {
        var document = new XmlDocument();
        document.LoadXml("<element />");
        AssertSerialization(document, "<element />");
    }

    [Fact]
    public void XmlElement()
    {
        var document = new XmlDocument();
        document.LoadXml("<element />");
        AssertSerialization(document.DocumentElement, "<element />");
    }

    [Fact]
    public void XmlAttribute()
    {
        var document = new XmlDocument();
        var attribute = document.CreateAttribute("test");
        attribute.Value = "value";
        AssertSerialization(attribute, "test=\"value\"");
    }

    [Fact]
    public void XDocument()
    {
        var document = System.Xml.Linq.XDocument.Parse("<root />");
        AssertSerialization(document, "<root />");
    }

#if NETCOREAPP3_0_OR_GREATER
    [Fact]
    public void JsonNode()
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(new { Root = 1 });
        AssertSerialization(node, """
            {
              "Root": 1
            }
            """);
    }

    [Fact]
    public void JsonElement()
    {
        var node = System.Text.Json.JsonSerializer.SerializeToElement(new { Root = 1 });
        AssertSerialization(node, """
            {
              "Root": 1
            }
            """);
    }

    [Fact]
    public void JsonDocument()
    {
        var node = System.Text.Json.JsonSerializer.SerializeToDocument(new { Root = 1 });
        AssertSerialization(node, """
            {
              "Root": 1
            }
            """);
    }
#endif

    [Fact]
    public void ExpandoObject()
    {
        dynamic obj = new ExpandoObject();
        obj.Prop1 = 1;
        obj.Prop2 = 2;

        AssertSerialization(new Validation
        {
            Subject = obj,
            Expected = """
            Prop1: 1
            Prop2: 2
            """,
        });
    }

    [Fact]
    public void Regex_NoTimeout()
    {
        AssertSerialization(new Validation
        {
            Subject = new System.Text.RegularExpressions.Regex("test", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
            Expected = """
            Pattern: test
            Options: IgnoreCase
            """,
        });
    }

    [Fact]
    public void Regex_Timeout()
    {
        AssertSerialization(new Validation
        {
            Subject = new System.Text.RegularExpressions.Regex("test", System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)),
            Expected = """
            Pattern: test
            Options: IgnoreCase
            MatchTimeout: 00:00:02
            """,
        });
    }

    [Fact]
    public void NullPropertyFollowedByNonNullProperty()
    {
        AssertSerialization(new Validation
        {
            Subject = new { Null = (int?)null, Str = "test" },
            Expected = """
                Null: <null>
                Str: test
                """,
        });
    }

    [Fact]
    public void String_InvisibleChar()
    {
        AssertSerialization(new Validation
        {
            Subject = "a b\tc\r\nd\ne\0",
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = """
            a␠b␉c␍␊
            d␊
            e␀
            """,
        });
    }

    [Fact]
    public void InfiniteLoop()
    {
        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(new Recursive()));
    }

    [Fact]
    public void Attributes()
    {
        AssertSerialization(new Validation
        {
            Subject = new ClassWithAttributes() { Prop1 = 1, Prop2 = 2, Prop3 = "test" },
            Expected = """
            Prop 2 Display: 2
            Prop3: Custom
            """,
        });
    }

    [Fact]
    public void Attributes_InvalidConverter_NotCompatible()
    {
        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(new InvalidConverters_NotCompatible()));
    }

    [Fact]
    public void Attributes_InvalidConverter_NotAConverter()
    {
        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(new InvalidConverters_NotAConverter()));
    }

    [Fact]
    public void OptionsAreReadOnlyAfterFirstUse()
    {
        var options = new HumanReadableSerializerOptions();
        AssertSerialization("", options, "");
        Assert.Throws<InvalidOperationException>(() => options.Converters.Add(new DummyConverter()));
    }

    [TypeConverter(typeof(CustomTypeConverterImpl))]
    private sealed class CustomTypeConverter
    {
        private sealed class CustomTypeConverterImpl : TypeConverter
        {
            public override bool CanConvertTo(ITypeDescriptorContext context, [NotNullWhen(true)] Type destinationType) => destinationType == typeof(string);

            public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType) => "converter";
        }
    }

    private sealed class CustomConvertible : IConvertible
    {
        public TypeCode GetTypeCode() => throw new NotSupportedException();
        public bool ToBoolean(IFormatProvider provider) => throw new NotSupportedException();
        public byte ToByte(IFormatProvider provider) => throw new NotSupportedException();
        public char ToChar(IFormatProvider provider) => throw new NotSupportedException();
        public DateTime ToDateTime(IFormatProvider provider) => throw new NotSupportedException();
        public decimal ToDecimal(IFormatProvider provider) => throw new NotSupportedException();
        public double ToDouble(IFormatProvider provider) => throw new NotSupportedException();
        public short ToInt16(IFormatProvider provider) => throw new NotSupportedException();
        public int ToInt32(IFormatProvider provider) => throw new NotSupportedException();
        public long ToInt64(IFormatProvider provider) => throw new NotSupportedException();
        public sbyte ToSByte(IFormatProvider provider) => throw new NotSupportedException();
        public float ToSingle(IFormatProvider provider) => throw new NotSupportedException();
        public string ToString(IFormatProvider provider) => "convertible";
        public object ToType(Type conversionType, IFormatProvider provider) => throw new NotSupportedException();
        public ushort ToUInt16(IFormatProvider provider) => throw new NotSupportedException();
        public uint ToUInt32(IFormatProvider provider) => throw new NotSupportedException();
        public ulong ToUInt64(IFormatProvider provider) => throw new NotSupportedException();
    }

    private sealed class Recursive
    {
        public Recursive Prop => this;
    }

    private sealed class ClassWithAttributes
    {
        [HumanReadableIgnore]
        public int Prop1 { get; set; }

        [HumanReadablePropertyName("Prop 2 Display")]
        public int Prop2 { get; set; }

        [HumanReadableConverter(typeof(CustomStringConverter))]
        public string Prop3 { get; set; }

        private sealed class CustomStringConverter : HumanReadableConverter<string>
        {
            protected override void WriteValue(HumanReadableTextWriter writer, string value, HumanReadableSerializerOptions options)
            {
                writer.WriteValue("Custom");
            }
        }
    }

    private sealed class InvalidConverters_NotCompatible
    {
        [HumanReadableConverter(typeof(CustomStringConverter))]
        public int Prop1 { get; set; }

        private sealed class CustomStringConverter : HumanReadableConverter<string>
        {
            protected override void WriteValue(HumanReadableTextWriter writer, string value, HumanReadableSerializerOptions options)
            {
                writer.WriteValue("Custom");
            }
        }
    }

    private sealed class InvalidConverters_NotAConverter
    {
        [HumanReadableConverter(typeof(DuplicateNameException))]
        public int Prop1 { get; set; }
    }

    private sealed class DummyConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => throw new NotSupportedException();
        public override void WriteValue(HumanReadableTextWriter writer, object value, HumanReadableSerializerOptions options) => throw new NotSupportedException();
    }
}