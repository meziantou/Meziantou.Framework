#pragma warning disable CA1720
#pragma warning disable CA1814
#pragma warning disable CA1822
#pragma warning disable CA3075
#pragma warning disable MA0009
#pragma warning disable MA0110
#pragma warning disable SYSLIB1045
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Xml;
using Meziantou.Framework.HumanReadableSerializer.FSharp.Tests;
using NodaTime;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.HumanReadable.Tests;
public sealed partial class SerializerTests : SerializerTestsBase
{
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
    public void SerializeObject() => AssertSerialization(new object(), "{}");

    [Fact]
    public void SerializeObject_Null() => AssertSerialization(null, "<null>");

    [Fact]
    public void SerializeObject_Null_Nested() => AssertSerialization(new { Obj = (object)null }, "Obj: <null>");

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
    public void IEnumerableKeyValuePairStringString_Dictionary()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["A"] = "10",
                ["B"] = "20",
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
    public void AsyncEnumerable_YieldReturnInt32()
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

        static async IAsyncEnumerable<int> TypedYield()
        {
            await Task.Delay(1);
            yield return 1;
            yield return 2;
            yield return 3;
        }
    }

    [Fact]
    public void AsyncEnumerable_YieldReturnInt32_NoAsync()
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

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        static async IAsyncEnumerable<int> TypedYield()
        {
            yield return 1;
            yield return 2;
            yield return 3;
        }
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
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
    public void EnumerableNonGeneric_YieldReturn()
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
    public void ArrayList()
    {
        AssertSerialization(new Validation
        {
            Subject = new ArrayList() { 1, 2, 3 },
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void ListDictionary()
    {
        AssertSerialization(new Validation
        {
            Subject = new System.Collections.Specialized.ListDictionary() { ["a"] = 1, [2] = 3 },
            Expected = """
                - Key: a
                  Value: 1
                - Key: 2
                  Value: 3
                """,
        });
    }

    [Fact]
    public void ImmutableDictionaryStringInt32()
    {
        AssertSerialization(new Validation
        {
            Subject = ImmutableDictionary.Create<string, int>(new CustomStringComparer()).Add("a", 1).Add("b", 2),
            Expected = """
                a: 1
                b: 2
                """,
        });
    }

    [Fact]
    public void ImmutableArrayInt32()
    {
        AssertSerialization(new Validation
        {
            Subject = ImmutableArray.Create<int>().Add(1).Add(2),
            Expected = """
                - 1
                - 2
                """,
        });
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
    public void ConcurrentBag()
    {
        AssertSerialization(new Validation
        {
            Subject = new ConcurrentBag<int> { 1, 2, 3 },
            Expected = """
                - 3
                - 2
                - 1
                """,
        });
    }

    [Fact]
    public void ConcurrentDictionaryStringInt32()
    {
        AssertSerialization(new Validation
        {
            Subject = new ConcurrentDictionary<string, int>(new CustomStringComparer())
            {
                ["a"] = 1,
                ["b"] = 2,
            },
            Expected = """
                b: 2
                a: 1
                """,
        });
    }

    [Fact]
    public void HashSet_int()
    {
        AssertSerialization(new Validation
        {
            Subject = new HashSet<int> { 1 },
            Expected = """
                - 1
                """,
        });
    }

    [Fact]
    public void ConcurrentQueue_int()
    {
        var queue = new ConcurrentQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        AssertSerialization(new Validation
        {
            Subject = queue,
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void Queue_int()
    {
        var queue = new Queue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);
        AssertSerialization(new Validation
        {
            Subject = queue,
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void ConcurrentStack_int()
    {
        var queue = new ConcurrentStack<int>();
        queue.Push(1);
        queue.Push(2);
        queue.Push(3);
        AssertSerialization(new Validation
        {
            Subject = queue,
            Expected = """
                - 3
                - 2
                - 1
                """,
        });
    }

    [Fact]
    public void Stack_int()
    {
        var queue = new Stack<int>();
        queue.Push(1);
        queue.Push(2);
        queue.Push(3);
        AssertSerialization(new Validation
        {
            Subject = queue,
            Expected = """
                - 3
                - 2
                - 1
                """,
        });
    }

    [Fact]
    public void FSharpOptionNone() => AssertSerialization(Factory.create_option_none<int>(), "<null>");

    [Fact]
    public void FSharpOptionSome() => AssertSerialization(Factory.create_option_some, "1");

    [Fact]
    public void FSharpValueOptionNone() => AssertSerialization(Factory.create_valueoption_none<int>(), "<null>");

    [Fact]
    public void FSharpValueOptionSome() => AssertSerialization(Factory.create_valueoption_some, "1");

    [Fact]
    public void FSharpArray()
    {
        var collection = Factory.create_array;
        AssertSerialization(new Validation
        {
            Subject = collection,
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void FSharpList()
    {
        var collection = Factory.create_list;
        AssertSerialization(new Validation
        {
            Subject = collection,
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void FSharpSeq()
    {
        var collection = Factory.create_seq;
        AssertSerialization(new Validation
        {
            Subject = collection,
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void FSharpMap()
    {
        var collection = Factory.create_map;
        AssertSerialization(new Validation
        {
            Subject = collection,
            Expected = """
                - Key: 1
                  Value: a
                - Key: 2
                  Value: b
                """,
        });
    }

    [Fact]
    public void FSharpMap_String()
    {
        var collection = Factory.create_map_string;
        AssertSerialization(new Validation
        {
            Subject = collection,
            Expected = """
                a: 1
                b: 2
                """,
        });
    }

    [Fact]
    public void FSharpSet()
    {
        var collection = Factory.create_set;
        AssertSerialization(new Validation
        {
            Subject = collection,
            Expected = """
                - 1
                - 2
                - 3
                """,
        });
    }

    [Fact]
    public void FSharpTuple()
    {
        var tuple = Factory.create_tuple;
        AssertSerialization(new Validation
        {
            Subject = tuple,
            Expected = """
                Item1: 1
                Item2: 2
                Item3: 3
                """,
        });
    }

    [Fact]
    public void Tuple()
    {
        var tuple = System.Tuple.Create(1, 2, 3);
        AssertSerialization(new Validation
        {
            Subject = tuple,
            Expected = """
                Item1: 1
                Item2: 2
                Item3: 3
                """,
        });
    }

    [Fact]
    public void ValueTuple()
    {
        var tuple = (1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12);
        AssertSerialization(new Validation
        {
            Subject = tuple,
            Expected = """
                Item1: 1
                Item2: 2
                Item3: 3
                Item4: 4
                Item5: 5
                Item6: 6
                Item7: 7
                Item8: 8
                Item9: 9
                Item10: 10
                Item11: 11
                Item12: 12
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
    public void String_MultiLine_InArray() => AssertSerialization(new string[] { "line1\nline2\n", "line3" }, """"
        - line1
          line2

        - line3
        """");

    [Fact]
    public void ByteArray() => AssertSerialization(new byte[] { 1, 2, 3 }, "AQID");

    [Fact]
    public void MemoryByte() => AssertSerialization(new byte[] { 1, 2, 3 }.AsMemory(), "AQID");

    [Fact]
    public void ReadOnlyMemoryByte() => AssertSerialization((ReadOnlyMemory<byte>)new byte[] { 1, 2, 3 }.AsMemory(), "AQID");

    [Fact]
    public void MemoryInt() => AssertSerialization(new int[] { 1, 2, 3 }.AsMemory(), """
        - 1
        - 2
        - 3
        """);

    [Fact]
    public void ReadOnlyMemoryInt() => AssertSerialization((ReadOnlyMemory<int>)new int[] { 1, 2, 3 }.AsMemory(), """
        - 1
        - 2
        - 3
        """);

    [Fact]
    public void MemoryChar() => AssertSerialization(new char[] { 't', 'e', 's', 't' }.AsMemory(), "test");

    [Fact]
    public void ReadOnlyMemoryChar() => AssertSerialization("test".AsMemory(), "test");

    [Fact]
    public void StringWriter()
    {
        using var value = new StringWriter();
        value.Write("test");
        AssertSerialization(value, "test");
    }

    [Fact]
    public void Type() => AssertSerialization(typeof(SerializerTests), "Meziantou.Framework.HumanReadable.Tests.SerializerTests, Meziantou.Framework.HumanReadableSerializer.Tests, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");

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
    public void JsonArray()
    {
        var node = System.Text.Json.JsonSerializer.SerializeToNode(new[] { 1, 2, 3 });
        AssertSerialization(node, """
            [
              1,
              2,
              3
            ]
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
    public void StringBuilder()
    {
        AssertSerialization(new Validation
        {
            Subject = new StringBuilder().Append("test"),
            Expected = """
            test
            """,
        });
    }

    [Fact]
    public void HttpMethod_Post()
    {
        AssertSerialization(HttpMethod.Post, "POST");
    }

    [Fact]
    public void MediaTypeHeaderValue() => AssertSerialization(new MediaTypeHeaderValue("application/json"), "application/json");

    [Fact]
    public void MediaTypeHeaderValue_WithParameters() => AssertSerialization(new MediaTypeHeaderValue("application/json") { Parameters = { new NameValueHeaderValue("foo", "bar") } }, "application/json; foo=bar");

    [Fact]
    public void MediaTypeHeaderValue_WithCharSet() => AssertSerialization(new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" }, "application/json; charset=utf-8");

    [Fact]
    public void HttpRequestHeaders()
    {
        using var message = new HttpRequestMessage();
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        message.Headers.ExpectContinue = true;

        AssertSerialization(message.Headers, """
            Accept:
              - application/json
              - text/plain
            Expect: 100-continue
            """);
    }

    [Fact]
    public void HttpRequestHeaders_Empty()
    {
        using var message = new HttpRequestMessage();

        AssertSerialization(message.Headers, """
            {}
            """);
    }

    [Fact]
    public void HttpResponseHeaders()
    {
        using var message = new HttpResponseMessage();
        message.Headers.ETag = new EntityTagHeaderValue("\"dummy\"");
        message.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        message.Headers.AcceptRanges.Add("1-2");
        message.Headers.AcceptRanges.Add("3-4");

        AssertSerialization(message.Headers, """
            ETag: "dummy"
            Retry-After: 1
            Accept-Ranges:
              - 1-2
              - 3-4
            """);
    }

    [Fact]
    public void HttpResponseHeaders_Sorted()
    {
        using var message = new HttpResponseMessage();
        message.Headers.ETag = new EntityTagHeaderValue("\"dummy\"");
        message.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        message.Headers.AcceptRanges.Add("1-2");
        message.Headers.AcceptRanges.Add("3-4");

        AssertSerialization(message.Headers, new HumanReadableSerializerOptions { PropertyOrder = StringComparer.Ordinal }, """
            Accept-Ranges:
              - 1-2
              - 3-4
            ETag: "dummy"
            Retry-After: 1
            """);
    }

    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
    public void HttpContent_MultiPartContent()
    {
        using var message = new MultipartContent(subtype: "mixed", boundary: "23f7d466-b54f-4db9-9a4b-8d26ea978125")
        {
            new StringContent("a"),
            new StringContent("b"),
        };
        AssertSerialization(message, """
            Headers:
              Content-Type: multipart/mixed; boundary="23f7d466-b54f-4db9-9a4b-8d26ea978125"
            Value:
              - Headers:
                  Content-Type: text/plain; charset=utf-8
                Value: a
              - Headers:
                  Content-Type: text/plain; charset=utf-8
                Value: b
            """);
    }

    [Fact]
    public void HttpContent_StringContent()
    {
        using var message = new StringContent("dummy");
        AssertSerialization(message, """
            Headers:
              Content-Type: text/plain; charset=utf-8
            Value: dummy
            """);
    }

    [Fact]
    public void HttpContent_JsonContent()
    {
        using var message = JsonContent.Create(new { A = 10 });
        AssertSerialization(message, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value: {"a":10}
            """);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/html")]
    [InlineData("application/javascript")]
    [InlineData("dummy/custom+xml")]
    public void HttpContent_TextFromContentType(string contentType)
    {
        using var message = new ByteArrayContent(Encoding.UTF8.GetBytes("test"))
        {
            Headers =
            {
                ContentType = new MediaTypeHeaderValue(contentType),
            },
        };

        AssertSerialization(message, $$"""
            Headers:
              Content-Type: {{contentType}}
            Value: test
            """);
    }

    [Fact]
    public void HttpContent_ByteArrayContent()
    {
        using var message = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        AssertSerialization(message, """
            AQIDBAUGBwgJ
            """);
    }

    [Fact]
    public void HttpContent_ByteArrayContent_WithHeaders()
    {
        using var message = new ByteArrayContent(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })
        {
            Headers =
            {
                Expires = new DateTimeOffset(2023, 2, 3,4,5,6,7, TimeSpan.Zero),
            },
        };
        AssertSerialization(message, """
            Headers:
              Expires: Fri, 03 Feb 2023 04:05:06 GMT
            Value: AQIDBAUGBwgJ
            """);
    }

    [Fact]
    public void NodaTime_Instant()
    {
        AssertSerialization(NodaTime.Instant.FromDateTimeUtc(new DateTime(2023, 1, 2, 3, 4, 5, DateTimeKind.Utc)), """
            2023-01-02T03:04:05Z
            """);
    }

    [Fact]
    public void NodaTime_LocalDateTime()
    {
        AssertSerialization(new LocalDateTime(2012, 3, 27, 0, 45, 00), """
            2012-03-27T00:45:00
            """);
    }

    [Fact]
    public void NodaTime_Duration()
    {
        AssertSerialization(NodaTime.Duration.FromMinutes(3), """
            0:00:03:00
            """);
    }

    [Fact]
    public void NodaTime_DateTimeZone()
    {
        var london = DateTimeZoneProviders.Tzdb["Europe/London"];

        AssertSerialization(london, """
            Id: Europe/London
            MinOffset: -00:01:15
            MaxOffset: +02
            """);
    }

    [Fact]
    public void NodaTime_ZonedDateTime()
    {
        var instant = NodaTime.Instant.FromDateTimeUtc(new DateTime(2023, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var value = instant.InUtc();

        AssertSerialization(value, """
            2023-01-02T03:04:05 UTC (+00)
            """);
    }

    [Fact]
    public void DnsEndPoint()
    {
        var value = new DnsEndPoint("example.com", 80, System.Net.Sockets.AddressFamily.InterNetwork);

        AssertSerialization(value, """
            Host: example.com
            AddressFamily: InterNetwork
            Port: 80
            """);
    }

    [Fact]
    public void DnsEndPoint2()
    {
        var value = new DnsEndPoint("example.com", 80, System.Net.Sockets.AddressFamily.InterNetworkV6);

        AssertSerialization(value, """
            Host: example.com
            AddressFamily: InterNetworkV6
            Port: 80
            """);
    }

    [Fact]
    public void IPEndPoint()
    {
        var value = new IPEndPoint(System.Net.IPAddress.Parse("1.2.3.4"), 80);

        AssertSerialization(value, """
            AddressFamily: InterNetwork
            Address: 1.2.3.4
            Port: 80
            """);
    }

#if NET6_0_OR_GREATER
    [Fact]
    public void UnixDomainSocketEndPoint()
    {
        var value = new System.Net.Sockets.UnixDomainSocketEndPoint("/var/run/dummy.sock");

        AssertSerialization(value, """
            /var/run/dummy.sock
            """);
    }
#endif

    [Fact]
    public void IPAddress()
    {
        var value = System.Net.IPAddress.Parse("1.2.3.4");

        AssertSerialization(value, """
            1.2.3.4
            """);
    }

#if NET6_0_OR_GREATER
    [Fact]
    public void HttpRequestMessage()
    {
        using var obj = new HttpRequestMessage()
        {
            RequestUri = new Uri("/sample", UriKind.Relative),
            Method = HttpMethod.Post,
            Headers =
            {
                Accept = { new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/plain") },
            },
            Version = new Version("1.1"),
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent("dummy", Encoding.UTF8, "text/plain"),
        };

        AssertSerialization(new Validation
        {
            Subject = obj,
            Expected = """
                Version: 1.1
                VersionPolicy: RequestVersionExact
                Content:
                  Headers:
                    Content-Type: text/plain; charset=utf-8
                  Value: dummy
                Method: POST
                RequestUri: /sample
                Headers:
                  Accept: text/plain
                Options: {}
                """,
        });
    }
#endif

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
    public void String_Multiline_InObject_NoStartingWhitespace()
    {
        AssertSerialization(new Validation
        {
            Subject = new
            {
                A = "abc\n\ndef",
            },
            Expected = "A:\n  abc\n\n  def",
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

    [Fact]
    public void IgnoreNullValues_Never()
    {
        AssertSerialization(new Validation
        {
            Subject = new { Dummy = "", Object = (object)null, NullableInt32 = (int?)null, DefaultStruct = 0 },
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.Never },
            Expected = """
                Dummy:
                Object: <null>
                NullableInt32: <null>
                DefaultStruct: 0
                """,
        });
    }

    [Fact]
    public void IgnoreNullValues_WhenNull()
    {
        AssertSerialization(new Validation
        {
            Subject = new { Dummy = "", Object = (object)null, NullableInt32 = (int?)null, DefaultStruct = 0 },
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull },
            Expected = """
                Dummy:
                DefaultStruct: 0
                """,
        });
    }

    [Fact]
    public void IgnoreNullValues_WhenDefault()
    {
        AssertSerialization(new Validation
        {
            Subject = new { Dummy = "", Object = (object)null, NullableInt32 = (int?)null, DefaultStruct = 0 },
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault },
            Expected = """
                Dummy:
                """,
        });
    }

    [Fact]
    public void IgnoreNullValues_PropertyAttribute()
    {
        AssertSerialization(new Validation
        {
            Subject = new IgnoreConditionSetOnProp(),
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.Never },
            Expected = """
                PropInt32_Null: 0
                PropObject2: <null>
                """,
        });
    }

    [Fact]
    public void AddTypeAttribute()
    {
        var instance = new
        {
            A = 0,
            B = 1,
            C = 2,
        };
        var options = new HumanReadableSerializerOptions();
        options.AddAttribute(instance.GetType(), nameof(instance.B), new HumanReadableIgnoreAttribute());

        AssertSerialization(new Validation
        {
            Subject = instance,
            Options = options,
            Expected = """
                A: 0
                C: 2
                """,
        });
    }

    [Fact]
    public void ObjectWithFields_IncludeFields_False()
    {
        AssertSerialization(new Validation
        {
            Subject = new ObjectWithFields { PropInt32 = 1, FieldInt32 = 2, FieldString = "a" },
            Options = new HumanReadableSerializerOptions { IncludeFields = false },
            Expected = """
                _privateFieldIncluded: <null>
                PropInt32: 1
                """,
        });
    }

    [Fact]
    public void ObjectWithFields_IncludeFields_True()
    {
        AssertSerialization(new Validation
        {
            Subject = new ObjectWithFields { PropInt32 = 1, FieldInt32 = 2, FieldString = "a" },
            Options = new HumanReadableSerializerOptions { IncludeFields = true },
            Expected = """
                _privateFieldIncluded: <null>
                FieldString: a
                FieldInt32: 2
                PropInt32: 1
                """,
        });
    }

    [Fact]
    public void OrderAttribute()
    {
        AssertSerialization(new Validation
        {
            Subject = new OrderedMember(),
            Expected = """
                Prop4: 4
                Field1: 1
                Prop2: 2
                Prop3: 3
                """,
        });
    }

    [Fact]
    public void ObjectHierarchy()
    {
        AssertSerialization(new Validation
        {
            Subject = new Child(),
            Expected = """
                PropRoot: 2
                PropChild: 3
                """,
        });
    }

    [Fact]
    public void StructWithDefaultConstructor_New_IgnoreDefault()
    {
        AssertSerialization(new Validation
        {
            Subject = new StructWithDefaultConstructor(),
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault },
            Expected = """
                Value: 1
                """,
        });
    }

    [Fact]
    public void StructWithDefaultConstructor_Default_IgnoreDefault()
    {
        AssertSerialization(new Validation
        {
            Subject = default(StructWithDefaultConstructor),
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault },
            Expected = """
                {}
                """,
        });
    }

    [Fact]
    public void ClassWithCustomConverterUsingAttribute()
    {
        AssertSerialization(new Validation
        {
            Subject = new ClassWithCustomConverter(),
            Expected = """
                dummy
                """,
        });
    }

    [Fact]
    public void IgnoreDefaultValueWithCustomDefaultValue()
    {
        var obj = new { A = 1, B = 2 };

        var options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault };
        options.AddAttribute(obj.GetType(), "A", new HumanReadableDefaultValueAttribute(1));

        AssertSerialization(new Validation
        {
            Subject = obj,
            Options = options,
            Expected = """
                B: 2
                """,
        });
    }

    [Fact]
    public void WhenWritingEmptyCollection_EmptyEnumerable()
    {
        var obj = new { A = Enumerable.Empty<int>(), B = 2 };

        AssertSerialization(new Validation
        {
            Subject = obj,
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingEmptyCollection },
            Expected = """
                B: 2
                """,
        });
    }

    [Fact]
    public void WhenWritingEmptyCollection_Null()
    {
        var obj = new { A = (string[])null, B = 2 };

        AssertSerialization(new Validation
        {
            Subject = obj,
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingEmptyCollection },
            Expected = """
                A: <null>
                B: 2
                """,
        });
    }

    [Fact]
    public void WhenWritingEmptyCollection_NonEmpty()
    {
        var obj = new { A = new string[] { "a", "b" }, B = 2 };

        AssertSerialization(new Validation
        {
            Subject = obj,
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingEmptyCollection },
            Expected = """
                A:
                  - a
                  - b
                B: 2
                """,
        });
    }

    [Fact]
    public void WhenWritingDefaultOrEmptyCollection_EmptyEnumerable()
    {
        var obj = new { A = Enumerable.Empty<int>(), B = 2 };

        AssertSerialization(new Validation
        {
            Subject = obj,
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection },
            Expected = """
                B: 2
                """,
        });
    }

    [Fact]
    public void WhenWritingDefaultOrEmptyCollection_Null()
    {
        var obj = new { A = (string[])null, B = 2 };

        AssertSerialization(new Validation
        {
            Subject = obj,
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection },
            Expected = """
                B: 2
                """,
        });
    }

    [Fact]
    public void WhenWritingDefaultOrEmptyCollection_NonEmpty()
    {
        var obj = new { A = new string[] { "a", "b" }, B = 2 };

        AssertSerialization(new Validation
        {
            Subject = obj,
            Options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefaultOrEmptyCollection },
            Expected = """
                A:
                  - a
                  - b
                B: 2
                """,
        });
    }

    [Fact]
    public void ConditionalPropertyAttribute()
    {
        var obj = new { A = new string[] { "a", "b" }, B = 2 };
        var options = new HumanReadableSerializerOptions();
        options.AddPropertyAttribute(prop => prop.Name == "B", new HumanReadableIgnoreAttribute());

        AssertSerialization(new Validation
        {
            Subject = obj,
            Options = options,
            Expected = """
                A:
                  - a
                  - b
                """,
        });
    }

    [Fact]
    public void IgnoreException()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMembersThatThrow<NotSupportedException>();

        AssertSerialization(new Validation
        {
            Subject = new PropThrowAnException(),
            Options = options,
            Expected = """
                B: 1
                """,
        });
    }

    [Fact]
    public void IgnoreException_ThrowException()
    {
        Assert.Throws<NotSupportedException>(() => AssertSerialization(new Validation
        {
            Subject = new PropThrowAnException(),
            Expected = """
                B: 1
                """,
        }));
    }

    [Fact]
    public void IgnoreException_ThrowExceptionUnexpectedException()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMembersThatThrow<NotImplementedException>();

        Assert.Throws<NotSupportedException>(() => AssertSerialization(new Validation
        {
            Subject = new PropThrowAnException(),
            Options = options,
            Expected = """
                B: 1
                """,
        }));
    }

    private sealed class PropThrowAnException
    {
        public int A => throw new NotSupportedException();
        public int B => 1;
    }

    private readonly struct StructWithDefaultConstructor
    {
        public int Value { get; }

        public StructWithDefaultConstructor()
        {
            Value = 1;
        }
    }

    private sealed class ObjectWithFields
    {
        private string _privateField;

        [HumanReadableInclude]
        private string _privateFieldIncluded;
        public string FieldString;
        public int FieldInt32;

        public int PropInt32 { get; set; }

        // Use the fields
        public void Dummy()
        {
            _privateField = "";
            _privateFieldIncluded = "";
            Console.Write(_privateFieldIncluded + _privateField);
        }
    }

    private sealed class IgnoreConditionSetOnProp
    {
        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.WhenWritingDefault)]
        public int PropInt32 { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.WhenWritingNull)]
        public int PropInt32_Null { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.WhenWritingNull)]
        public object PropObject { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.Never)]
        public object PropObject2 { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.WhenWritingDefault)]
        public object PropObject3 { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.Always)]
        public object PropObject4 { get; set; }
    }

    private sealed class OrderedMember
    {
        [HumanReadablePropertyOrder(2)]
        [HumanReadableInclude]
        public int Field1 = 1;

        [HumanReadablePropertyOrder(3)]
        public int Prop2 { get; set; } = 2;

        [HumanReadablePropertyOrder(4)]
        public int Prop3 { get; set; } = 3;

        [HumanReadablePropertyOrder(1)]
        public int Prop4 { get; set; } = 4;
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

    private sealed class CustomStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string x, string y) => x == y;
        public int GetHashCode([DisallowNull] string obj) => 0;
    }

    [HumanReadableConverter(typeof(ClassWithCustomConverterConverter))]
    private sealed record ClassWithCustomConverter();

    private sealed class ClassWithCustomConverterConverter : HumanReadableConverter<ClassWithCustomConverter>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, ClassWithCustomConverter value, HumanReadableSerializerOptions options)
        {
            writer.WriteValue("dummy");
        }
    }

    private class Root
    {
        public int PropRoot { get; set; } = 1;
    }

    private sealed class Child : Root
    {
        public new int PropRoot { get; set; } = 2;
        public int PropChild { get; set; } = 3;
    }
}