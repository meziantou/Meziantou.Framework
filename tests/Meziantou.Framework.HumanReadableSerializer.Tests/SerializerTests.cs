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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.Dynamic;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Numerics;
using System.Reflection;
using System.Xml;
using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadableSerializer.FSharp.Tests;
using Meziantou.Xunit;

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
    public void FSharp_DiscriminatedUnion_ObjectField_UsesRuntimeType()
    {
        AssertSerialization(Boxed.NewBoxed(new Version(1, 2)), """
            Tag: Boxed
            value: 1.2
            """);
    }

#if NET11_0_OR_GREATER
    [Fact]
    public void CSharp_Union()
    {
        CSharpPet value = new CSharpDog("Rex");

        AssertSerialization(value, "Name: Rex");
    }

    [Fact]
    public void CSharp_Union_Cat()
    {
        CSharpPet value = new CSharpCat("Misty");

        AssertSerialization(value, "Name: Misty");
    }

    [Fact]
    public void CSharp_Union_Default()
    {
        AssertSerialization(default(CSharpPet), "<null>");
    }
#endif

    [Fact]
    public void CultureInfo_Invariant()
        => AssertSerialization(CultureInfo.InvariantCulture, "Invariant Language (Invariant Country)");

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
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
    public void SerializeObject_Null_Nested() => AssertSerialization(new { Obj = (object?)null }, "Obj: <null>");

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
                new("A", 10),
                new("B", 20),
            },
            Expected = """
                A: 10
                B: 20
                """,
        });
    }

    [Fact]
    public void IEnumerableKeyValuePairStringObject_Array_Order()
    {
        AssertSerialization(new Validation
        {
            Subject = new KeyValuePair<string, object>[]
            {
                new("B", 20),
                new("A", 10),
            },
            Options = new HumanReadableSerializerOptions { DictionaryKeyOrder = StringComparer.Ordinal },
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
            Options = new HumanReadableSerializerOptions { DictionaryKeyOrder = StringComparer.Ordinal },
            Expected = """
                A: 10
                B: 20
                """,
        });
    }

    [Fact]
    public void IEnumerableKeyValuePairStringObject_Dictionary_Order()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["B"] = 20,
                ["A"] = 10,
            },
            Options = new HumanReadableSerializerOptions { DictionaryKeyOrder = StringComparer.Ordinal },
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
                new("A", 10),
                new("B", 20),
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
            Subject = new int[][] { [1, 2, 3], [4, 5, 6] },
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
    public void MultiDimensionalArrayInt32_NonZeroLowerBounds()
    {
        var data = Array.CreateInstance(typeof(int), lengths: [2, 2], lowerBounds: [1, -1]);
        data.SetValue(1, 1, -1);
        data.SetValue(2, 1, 0);
        data.SetValue(3, 2, -1);
        data.SetValue(4, 2, 0);

        AssertSerialization(data, """
            - [1, -1]: 1
            - [1, 0]: 2
            - [2, -1]: 3
            - [2, 0]: 4
            """);
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
            await Task.Delay(1, XunitCancellationToken);
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

        static async IAsyncEnumerable<int> TypedYield()
        {
            await Task.Yield();

            yield return 1;
            yield return 2;
            yield return 3;
        }
    }

    [Fact]
    public void AsyncEnumerable_KeyValuePairStringInt32()
    {
        AssertSerialization(new Validation
        {
            Subject = TypedYield(),
            Expected = """
                A: 1
                C: 3
                B: 2
                """,
        });

        static async IAsyncEnumerable<KeyValuePair<string, int>> TypedYield()
        {
            await Task.Yield();
            yield return new KeyValuePair<string, int>("A", 1);
            yield return new KeyValuePair<string, int>("C", 3);
            yield return new KeyValuePair<string, int>("B", 2);
        }
    }

    [Fact]
    public void AsyncEnumerable_KeyValuePairStringInt32_Order()
    {
        AssertSerialization(new Validation
        {
            Subject = TypedYield(),
            Options = new HumanReadableSerializerOptions { DictionaryKeyOrder = StringComparer.Ordinal },
            Expected = """
                A: 1
                B: 2
                C: 3
                """,
        });

        static async IAsyncEnumerable<KeyValuePair<string, int>> TypedYield()
        {
            await Task.Yield();
            yield return new KeyValuePair<string, int>("A", 1);
            yield return new KeyValuePair<string, int>("C", 3);
            yield return new KeyValuePair<string, int>("B", 2);
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
#pragma warning disable RS0030 // Do not use banned APIs
            Subject = new ArrayList() { 1, 2, 3 },
#pragma warning restore RS0030 // Do not use banned APIs
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
            Subject = new ListDictionary() { ["a"] = 1, [2] = 3 },
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
            Subject = ImmutableArray.Create(1, 2),
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
    public void FSharpOptionSome()
    {
        var value = Factory.create_option_some;
        Assert.NotNull(value);
        AssertSerialization(value, "1");
    }

    [Fact]
    public void FSharpValueOptionNone() => AssertSerialization(Factory.create_valueoption_none<int>(), "<null>");

    [Fact]
    public void FSharpValueOptionSome() => AssertSerialization(Factory.create_valueoption_some, "1");

    [Fact]
    public void FSharpValueOptionNone_ReferenceType() => AssertSerialization(Factory.create_valueoption_none<string>(), "<null>");

    [Fact]
    public void FSharpValueOptionSome_ReferenceType() => AssertSerialization(Microsoft.FSharp.Core.FSharpValueOption<string>.NewValueSome("test"), "test");

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

    [Fact]
    public void Int128() => AssertSerialization(new Int128(123, 456), "2268949521066274849224");

    [Fact]
    public void UInt128() => AssertSerialization(new UInt128(123, 456), "2268949521066274849224");

    [Fact]
    public void BigInteger() => AssertSerialization(new BigInteger(12), "12");

    [Fact]
    public void Complex() => AssertSerialization(new Complex(12, 34), "<12; 34>");

    [Fact]
    public void Half() => AssertSerialization((Half)0.5, "0.5");

#if NET11_0_OR_GREATER
    [Fact]
    public void BFloat16() => AssertSerialization((BFloat16)0.5, "0.5");

    [Fact]
    public void BFloat16_NaN() => AssertSerialization(System.Numerics.BFloat16.NaN, "NaN");

    [Fact]
    public void BFloat16_PositiveInfinity() => AssertSerialization(System.Numerics.BFloat16.PositiveInfinity, "Infinity");

    [Fact]
    public void BFloat16_NegativeInfinity() => AssertSerialization(System.Numerics.BFloat16.NegativeInfinity, "-Infinity");

    [Fact]
    public void Decimal32() => AssertSerialization(System.Numerics.Decimal32.Parse("-5.30", CultureInfo.InvariantCulture), "-5.30");

    [Fact]
    public void Decimal32_NaN() => AssertSerialization(System.Numerics.Decimal32.NaN, "NaN");

    [Fact]
    public void Decimal64() => AssertSerialization(System.Numerics.Decimal64.Parse("123.456", CultureInfo.InvariantCulture), "123.456");

    [Fact]
    public void Decimal128() => AssertSerialization(System.Numerics.Decimal128.Pi, "3.141592653589793238462643383279503");

    [Fact]
    public void Decimal128_NegativeInfinity() => AssertSerialization(System.Numerics.Decimal128.NegativeInfinity, "-Infinity");
#endif

    [Fact]
    public void Single() => AssertSerialization(-5.30f, "-5.3");

    [Fact]
    public void Single_NaN() => AssertSerialization(float.NaN, "NaN");

    [Fact]
    public void Single_PositiveInfinity() => AssertSerialization(float.PositiveInfinity, "Infinity");

    [Fact]
    public void Single_NegativeInfinity() => AssertSerialization(float.NegativeInfinity, "-Infinity");

    [Fact]
    public void Double() => AssertSerialization(-5.30d, "-5.3");

    [Fact]
    public void Double_NaN() => AssertSerialization(double.NaN, "NaN");

    [Fact]
    public void Double_PositiveInfinity() => AssertSerialization(double.PositiveInfinity, "Infinity");

    [Fact]
    public void Double_NegativeInfinity() => AssertSerialization(double.NegativeInfinity, "-Infinity");

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
    public void ReadOnlyMemoryObject_UsesRuntimeType() => AssertSerialization((ReadOnlyMemory<object>)new object[] { new Version(1, 2), new List<int> { 1 } }.AsMemory(), """
        - 1.2
        - - 1
        """);

    [Fact]
    public void StringWriter()
    {
        using var value = new StringWriter();
        value.Write("test");
        AssertSerialization(value, "test");
    }

    [Fact]
    public void Type() => AssertSerialization(typeof(SerializerTests), "Meziantou.Framework.HumanReadable.Tests.SerializerTests, Meziantou.Framework.HumanReadableSerializer.Tests");

    [Fact]
    public void Type_Covariant_Contravariant() => AssertSerialization(typeof(ICovariantContravariantInterface<,>), "Meziantou.Framework.HumanReadable.Tests.SerializerTests+ICovariantContravariantInterface<in T1, out T2>, Meziantou.Framework.HumanReadableSerializer.Tests");

    [Fact]
    public void Type_List_OpenGeneric() => AssertSerialization(typeof(List<>), "System.Collections.Generic.List<T>, System.Private.CoreLib");

    [Fact]
    public void Type_List_Int32() => AssertSerialization(typeof(List<int>), "System.Collections.Generic.List<System.Int32>, System.Private.CoreLib");

    [Fact]
    public void Type_Array_GenericElementType() => AssertSerialization(typeof(List<int>[,]), "System.Collections.Generic.List<System.Int32>[,], System.Private.CoreLib");

    [Fact]
    public void Type_Covariant_WithConstraint() => AssertSerialization(typeof(ICovariantWithConstraintInterface<>), "Meziantou.Framework.HumanReadable.Tests.SerializerTests+ICovariantWithConstraintInterface<out T>, Meziantou.Framework.HumanReadableSerializer.Tests");

    [Fact]
    public void MethodInfo_ByRefParameters() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.ByRefParameters))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.ByRefParameters(ref System.Int32 a,out System.Collections.Generic.List<System.Int32>[] b,ref dynamic c)");

    [Fact]
    public void Type_AnonymType() => AssertSerialization(new { }.GetType(), "<>f__AnonymousType4, Meziantou.Framework.HumanReadableSerializer.Tests");

    [Fact]
    public void MethodInfo() => AssertSerialization(typeof(object).GetMethod("ToString")!, "System.Object.ToString()");

    [Fact]
    public void MethodInfo_dynamic_Parameter() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.Dynamic))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.Dynamic(dynamic value)");

    [Fact]
    public void MethodInfo_dynamic_ValueTuple_Parameter() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.ValueTupleDynamic))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.ValueTupleDynamic((dynamic, System.Int32) value)");

    [Fact]
    public void MethodInfo_dynamic_Nested_ValueTuple_Parameter() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.ValueTupleNestedDynamic))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.ValueTupleNestedDynamic((dynamic A, (System.Int32 B, dynamic C) D) value)");

    [Fact]
    public void MethodInfo_ValueTuple_Parameter() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.NotNamed))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.NotNamed((System.Int32, System.String) a)");

    [Fact]
    public void MethodInfo_ValueTuple_Named_Parameter() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.Named))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.Named((System.Int32 A, System.String B) a)");

    [Fact]
    public void MethodInfo_ValueTuple_Named_ReturnType() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.NamedResult))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.NamedResult()");

    [Fact]
    public void PropertyInfo_ValueTuple_Named_ReturnType() => AssertSerialization(typeof(Methods).GetProperty(nameof(Methods.NamedProperty))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.NamedProperty");

    [Fact]
    public void MethodInfo_WithParameters() => AssertSerialization(typeof(Guid).GetMethod("Parse", [typeof(string)]), "static System.Guid.Parse(System.String input)");

    [Fact]
    public void MethodInfo_Generic() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.GenericMethod))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.GenericMethod<TEnum>(System.String value)");

    [Fact]
    public void MethodInfo_Generic_Constructed() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.GenericMethod))!.MakeGenericMethod(typeof(DayOfWeek)), "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.GenericMethod<System.DayOfWeek>(System.String value)");

    [Fact]
    public void FieldInfo() => AssertSerialization(typeof(Guid).GetField("_a", BindingFlags.NonPublic | BindingFlags.Instance)!, "System.Guid._a");

    [Fact]
    public void FieldInfo_OpenGenericType() => AssertSerialization(typeof(Nullable<>).GetField("hasValue", BindingFlags.NonPublic | BindingFlags.Instance)!, "System.Nullable<T>.hasValue");

    [Fact]
    public void FieldInfo_GenericType() => AssertSerialization(typeof(int?).GetField("hasValue", BindingFlags.NonPublic | BindingFlags.Instance)!, "System.Nullable<System.Int32>.hasValue");

    [Fact]
    public void PropertyInfo() => AssertSerialization(typeof(string).GetProperty("Length")!, "System.String.Length");

    [Fact]
    public void PropertyInfo_Indexer() => AssertSerialization(typeof(string).GetProperty("Chars")!, "System.String.Chars[System.Int32 index]");

    [Fact]
    public void ConstructorInfo() => AssertSerialization(typeof(object).GetConstructor([])!, "new System.Object()");

    [Fact]
    public void ConstructorInfo_Static() => AssertSerialization(typeof(ClassWithStaticCtor).GetConstructors(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)[0], "static Meziantou.Framework.HumanReadable.Tests.SerializerTests+ClassWithStaticCtor()");

    [Fact]
    public void ParameterInfo() => AssertSerialization(typeof(Guid).GetMethod("Parse", [typeof(string)])!.GetParameters()[0], "System.String input");

    [Fact]
    public void DateTime_Utc() => AssertSerialization(new DateTime(2123, 4, 5, 6, 7, 8, DateTimeKind.Utc), "2123-04-05T06:07:08Z");

    [Fact]
    public void DateTime_Local()
    {
        var dateTime = new DateTime(2123, 4, 5, 6, 7, 8, DateTimeKind.Local);
        var utcOffset = TimeZoneInfo.Local.GetUtcOffset(dateTime);
        AssertSerialization(dateTime, "2123-04-05T06:07:08" + (utcOffset < TimeSpan.Zero ? "-" : "+") + utcOffset.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
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
    public void DateTime_Milliseconds() => AssertSerialization(new DateTime(2123, 4, 5, 6, 7, 8, 9, DateTimeKind.Utc), "2123-04-05T06:07:08.0090000Z");

    [Fact]
    public void DateTime_SubMillisecondTicks() => AssertSerialization(new DateTime(2123, 4, 5, 6, 7, 8, DateTimeKind.Utc).AddTicks(5000), "2123-04-05T06:07:08.0005000Z");

    [Fact]
    public void DateTime_SingleTick() => AssertSerialization(new DateTime(2123, 4, 5, 6, 7, 8, DateTimeKind.Utc).AddTicks(1), "2123-04-05T06:07:08.0000001Z");

    [Fact]
    public void DateTimeOffset_SubMillisecondTicks() => AssertSerialization(new DateTimeOffset(2123, 4, 5, 6, 7, 8, TimeSpan.Zero).AddTicks(5000), "2123-04-05T06:07:08.0005000+00:00");

    [Fact]
    public void DateTime_ValuesDifferingBySubMillisecondTicksAreNotSerializedIdentically()
    {
        var value = new DateTime(2123, 4, 5, 6, 7, 8, DateTimeKind.Utc);

        Assert.NotEqual(HumanReadableSerializer.Serialize(value), HumanReadableSerializer.Serialize(value.AddTicks(1)));
    }

    [Fact]
    public void Timespan_HoursMinutesSeconds() => AssertSerialization(new TimeSpan(1, 2, 3), "01:02:03");

    [Fact]
    public void Timespan_ZeroDay_HoursMinutesSeconds() => AssertSerialization(new TimeSpan(0, 1, 2, 3), "01:02:03");

    [Fact]
    public void Timespan_DaysHoursMinutesSeconds() => AssertSerialization(new TimeSpan(1, 2, 3, 4), "1.02:03:04");

    [Fact]
    public void Timespan_DaysHoursMinutesSecondsMilliseconds() => AssertSerialization(new TimeSpan(1, 2, 3, 4, 5), "1.02:03:04.0050000");

    [Fact]
    public void Timespan_DaysHoursMinutesSecondsMillisecondsMicroseconds() => AssertSerialization(new TimeSpan(1, 2, 3, 4, 5, 6), "1.02:03:04.0050060");

    [Fact]
    public void DateOnly() => AssertSerialization(new DateOnly(2123, 4, 5), "2123-04-05");

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
              Content-Type: multipart/mixed
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
        using var message = new ByteArrayContent([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        AssertSerialization(message, """
            AQIDBAUGBwgJ
            """);
    }

    [Fact]
    public void HttpContent_ByteArrayContent_WithHeaders()
    {
        using var message = new ByteArrayContent([1, 2, 3, 4, 5, 6, 7, 8, 9])
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
    public void StringDictionary()
    {
        var value = new StringDictionary()
        {
            ["key1"] = "value1",
            ["key2"] = "value2",
        };

        AssertSerialization(new Validation
        {
            Subject = value,
            Options = new HumanReadableSerializerOptions { DictionaryKeyOrder = StringComparer.Ordinal },
            Expected = """
                key1: value1
                key2: value2
                """,
        });
    }

    [Fact]
    public void NameValueCollection()
    {
        var value = new NameValueCollection()
        {
            ["key1"] = "value1",
            ["key2"] = "value2",
        };

        AssertSerialization(value, """
            key1: value1
            key2: value2
            """);
    }

    [Fact]
    public void NameValueCollection_DictionaryKeyOrder()
    {
        var value = new NameValueCollection
        {
            { "key2", "value2" },
            { null, "value0" },
            { "key1", "value1a" },
            { "key1", "value1b" },
        };

        AssertSerialization(value, new HumanReadableSerializerOptions { DictionaryKeyOrder = StringComparer.Ordinal }, """
            : value0
            key1:
              - value1a
              - value1b
            key2: value2
            """);
    }

    [Fact]
    public void BitVector32()
    {
        var value = new BitVector32();
        var section = System.Collections.Specialized.BitVector32.CreateSection(3);
        value[section] = 2;

        AssertSerialization(value, """
            00000000000000000000000000000010
            """);
    }

    [Fact]
    public void BitArray()
    {
        var value = new BitArray(length: 32);
        value[1] = true;

        AssertSerialization(value, """
            01000000000000000000000000000000
            """);
    }

    [Fact]
    public void Expression_NewObject()
    {
        var value = Expression.New(typeof(object));

        AssertSerialization(value, """
            new Object()
            """);
    }

    [Fact]
    public void Expression_Lambda()
    {
        var param = Expression.Parameter(typeof(string), "x");
        var body = Expression.Constant(true);
        var value = Expression.Lambda(body, param);

        AssertSerialization(value, """
            x => True
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

    [Fact]
    public void UnixDomainSocketEndPoint()
    {
        var value = new System.Net.Sockets.UnixDomainSocketEndPoint("/var/run/dummy.sock");

        AssertSerialization(value, """
            /var/run/dummy.sock
            """);
    }

    [Fact]
    public void IPAddress()
    {
        var value = System.Net.IPAddress.Parse("1.2.3.4");

        AssertSerialization(value, """
            1.2.3.4
            """);
    }

    [Fact]
    public void IPNetwork()
    {
        AssertSerialization(new Validation
        {
            Subject = System.Net.IPNetwork.Parse("192.168.1.0/24"),
            Expected = "192.168.1.0/24",
        });
    }

    [Fact]
    public void HttpRequestMessage()
    {
        using var obj = new HttpRequestMessage()
        {
            RequestUri = new Uri("/sample", UriKind.Relative),
            Method = HttpMethod.Post,
            Headers =
            {
                Accept = { new MediaTypeWithQualityHeaderValue("text/plain") },
            },
            Version = new Version("1.1"),
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent("dummy", Encoding.UTF8, "text/plain"),
        };

        AssertSerialization(new Validation
        {
            Subject = obj,
#if NET11_0_OR_GREATER
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
                ConnectionId: <null>
                """,
#else
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
#endif
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
            a b␉c␍␊
            d␊
            e␀
            """,
        });
    }

    [Fact]
    public void String_InvisibleChar_InObject_IndentsEveryLine()
    {
        var options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true };
        var text = HumanReadableSerializer.Serialize(new { Multiline = "line 1\r\nline\t2", Empty = "a\r\n\rb\n" }, options);

        // Line endings are normalized to options.NewLine: the control pictures already record the original ones
        var expected = string.Join(options.NewLine, "Multiline:", "  line 1␍␊", "  line␉2", "Empty:", "  a␍␊", "  ␍", "  b␊", "");
        Assert.Equal(expected, text);
    }

    [Fact]
    public void PropertyName_InvisibleChar_InObject_IndentsEveryLine()
    {
        AssertSerialization(new Validation
        {
            Subject = new { A = new Dictionary<string, int> { ["a\nb"] = 1, [""] = 2 } },
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = """
            A:
              a␊
              b: 1
              : 2
            """,
        });
    }

    [Fact]
    public void PropertyName_InvisibleChar()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<string, int> { ["a\nb"] = 1, ["z"] = 2 },
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = """
            a␊
            b: 1
            z: 2
            """,
        });
    }

    [Fact]
    public void PropertyName_InvisibleChar_Disabled()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<string, int> { ["a\nb"] = 1, ["z"] = 2 },
            Expected = """
            a
            b: 1
            z: 2
            """,
        });
    }

    [Fact]
    public void String_InvisibleChar_Nested()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<string, string> { ["A"] = "a b\nc", ["B"] = "d" },
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = """
            A:
              a b␊
              c
            B: d
            """,
        });
    }

    [Fact]
    public void String_InvisibleChar_SingleLine()
    {
        AssertSerialization(new Validation
        {
            Subject = "a\tb\0c d\u007F",
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = "a␉b␀c d␡",
        });
    }

    [Fact]
    public void String_InvisibleChar_LeadingAndTrailingWhitespace()
    {
        AssertSerialization(new Validation
        {
            Subject = "  a  b  \n c \t\n   ",
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = """
            ␠␠a  b␠␠␊
            ␠c␠␉␊
            ␠␠␠
            """,
        });
    }

    [Fact]
    public void String_InvisibleChar_OtherSpaces()
    {
        AssertSerialization(new Validation
        {
            Subject = "a\u00A0b\u2009c\u3000d\u200Be\u2060f\uFEFFg\u1680h",
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = "a<U+00A0>b<U+2009>c<U+3000>d<U+200B>e<U+2060>f<U+FEFF>g<U+1680>h",
        });
    }

    [Fact]
    public void String_InvisibleChar_OtherSpaces_AtTheEdges()
    {
        AssertSerialization(new Validation
        {
            Subject = "\u00A0 a \u00A0",
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = "<U+00A0>␠a␠<U+00A0>",
        });
    }

    [Theory]
    [InlineData("\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67", "\uD83D\uDC68\u200D\uD83D\uDC69\u200D\uD83D\uDC67")] // Family
    [InlineData("\u2764\uFE0F\u200D\uD83D\uDD25", "\u2764\uFE0F\u200D\uD83D\uDD25")] // Heart on fire (variation selector)
    [InlineData("\uD83E\uDDD1\uD83C\uDFFD\u200D\uD83D\uDCBB", "\uD83E\uDDD1\uD83C\uDFFD\u200D\uD83D\uDCBB")] // Technologist (skin tone)
    [InlineData("\uD83D\uDC68\u200D\uD83D\uDC69 a\u200Db", "\uD83D\uDC68\u200D\uD83D\uDC69 a<U+200D>b")]
    [InlineData("a\u200Db", "a<U+200D>b")]
    [InlineData("\uD83D\uDC68\u200Db", "\uD83D\uDC68<U+200D>b")]
    [InlineData("a\u200D\uD83D\uDC69", "a<U+200D>\uD83D\uDC69")]
    [InlineData("\uD83D\uDC68\u200D", "\uD83D\uDC68<U+200D>")]
    [InlineData("\u200D\uD83D\uDC69", "<U+200D>\uD83D\uDC69")]
    [InlineData("\u0915\u094D\u200D\u0937", "\u0915\u094D<U+200D>\u0937")] // Devanagari conjunct
    [InlineData("a\u200Cb", "a<U+200C>b")]
    [InlineData("\uD83D\uDC68\u200C\uD83D\uDC69", "\uD83D\uDC68<U+200C>\uD83D\uDC69")]
    public void String_InvisibleChar_ZeroWidthJoiner_OnlyKeptInEmojiSequences(string value, string expected)
    {
        AssertSerialization(value, new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true }, expected);
    }

    [Fact]
    public void String_InvisibleChar_UnicodeLineSeparators()
    {
        AssertSerialization(new Validation
        {
            Subject = "a\u2028b\u0085c",
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = """
            a<U+2028>
            b<U+0085>
            c
            """,
        });
    }

    [Fact]
    public void PropertyName_InvisibleChar_SingleLine()
    {
        AssertSerialization(new Validation
        {
            Subject = new Dictionary<string, string> { ["a b "] = " c d" },
            Options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true },
            Expected = "a b␠: ␠c d",
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
    public void InfiniteLoop_SelfReferencingCollection()
    {
        var value = new List<object>();
        value.Add(value);

        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(value));
    }

    [Fact]
    public void InfiniteLoop_CollectionsAlternatingWithObjects()
    {
        var value = new Recursive2();
        value.Items.Add(value);

        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(value));
    }

    [Fact]
    public void NestedCollectionsWithinMaxDepthAreSerialized()
    {
        object value = 1;
        for (var i = 0; i < 8; i++)
        {
            value = new List<object> { value };
        }

        AssertSerialization(new Validation
        {
            Subject = value,
            Options = new HumanReadableSerializerOptions { MaxDepth = 8 },
            Expected = """
                - - - - - - - - 1
                """,
        });
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
    public void MaxDepthViolationDoesNotConsumeDepthBudget()
    {
        var options = new HumanReadableSerializerOptions { MaxDepth = 1 };
        var writer = new HumanReadableTextWriter(options);

        writer.StartObject();
        writer.WritePropertyName("a");
        Assert.Throws<HumanReadableSerializerException>(writer.StartObject);
        writer.WriteValue("1");
        writer.EndObject();

        // The failed StartObject must not have left the depth inflated, so an unrelated
        // object written afterwards is still within the budget.
        writer.StartObject();
        writer.WritePropertyName("b");
        writer.WriteValue("2");
        writer.EndObject();

        Assert.Equal("""
            a: 1
              b: 2
            """, writer.ToString(), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void IgnoreNullValues_Never()
    {
        AssertSerialization(new Validation
        {
            Subject = new { Dummy = "", Object = (object?)null, NullableInt32 = (int?)null, DefaultStruct = 0 },
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
            Subject = new { Dummy = "", Object = (object?)null, NullableInt32 = (int?)null, DefaultStruct = 0 },
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
            Subject = new { Dummy = "", Object = (object?)null, NullableInt32 = (int?)null, DefaultStruct = 0 },
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
        var obj = new { A = (string[]?)null, B = 2 };

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
        var obj = new { A = (string[]?)null, B = 2 };

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

    [Fact]
    public void IgnoreException_MultipleExceptionTypes()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMembersThatThrow<NotSupportedException>();
        options.IgnoreMembersThatThrow<NotImplementedException>();

        AssertSerialization(new PropThrowAnException(), options, "B: 1");
    }

    [Fact]
    public void IgnoreException_KeepsDefaultIgnoreCondition()
    {
        var options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingNull };
        options.IgnoreMembersThatThrow<NotSupportedException>();

        AssertSerialization(new PropThrowAnExceptionAndNull(), options, "B: 1");
    }

    [Fact]
    public void Fields_DefaultValueAttribute()
    {
        var options = new HumanReadableSerializerOptions { IncludeFields = true, DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault };

        AssertSerialization(new FieldsWithAttributes(), options, "Other: 2");
    }

    [Fact]
    public void Fields_Obsolete()
    {
        AssertSerialization(new FieldsWithAttributes(), new HumanReadableSerializerOptions { IncludeFields = true }, """
            WithDefaultValue: 1
            Other: 2
            """);
    }

    [Fact]
    public void Fields_Obsolete_IncludeObsoleteMembers()
    {
        AssertSerialization(new FieldsWithAttributes(), new HumanReadableSerializerOptions { IncludeFields = true, IncludeObsoleteMembers = true }, """
            WithDefaultValue: 1
            ObsoleteField: 3
            Other: 2
            """);
    }

    [Fact]
    public void Inheritance_NewMemberWithDifferentType()
    {
        AssertSerialization(new ChildWithNewMemberType(), """
            PropRoot: child
            PropChild: 3
            """);
    }

    [Fact]
    public void Inheritance_IgnoredNewMemberHidesBaseMember()
    {
        AssertSerialization(new ChildWithIgnoredNewMember(), "PropChild: 3");
    }

    [Fact]
    public void Inheritance_IncludedPrivateMemberOfBaseType()
    {
        AssertSerialization(new ChildOfRootWithPrivateMember(), """
            PropChild: 3
            PrivateRoot: 1
            """);
    }

    [Fact]
    public void Serialize_NullAsValueType_Throws()
    {
        Assert.Throws<ArgumentException>(() => HumanReadableSerializer.Serialize(value: null, typeof(int)));
    }

    [Fact]
    public void Serialize_NullAsNullableValueType()
    {
        AssertSerialization(obj: null, options: null, typeof(int?), "<null>");
    }

    [Fact]
    public void IgnoreMember_Expression_SingleMember()
    {
        var options = new HumanReadableSerializerOptions();
        options.PropertyOrder = StringComparer.Ordinal;
        options.IgnoreMember<Exception>(exception => exception.TargetSite!);

        AssertSerialization(new Validation
        {
            Subject = new Exception("test"),
            Options = options,
            Expected = """
                Data: []
                HResult: -2146233088
                HelpLink: <null>
                InnerException: <null>
                Message: test
                Source: <null>
                StackTrace: <null>
                """,
        });
    }

    [Fact]
    public void IgnoreMember_Expression_SingleMember_Nested()
    {
        var options = new HumanReadableSerializerOptions();
        options.PropertyOrder = StringComparer.Ordinal;
        options.IgnoreMember<Exception>(exception => exception.TargetSite!);
        options.IgnoreMember<Exception>(exception => exception.InnerException!.Message); // Detect Exception.Message

        AssertSerialization(new Validation
        {
            Subject = new Exception("test"),
            Options = options,
            Expected = """
                Data: []
                HResult: -2146233088
                HelpLink: <null>
                InnerException: <null>
                Source: <null>
                StackTrace: <null>
                """,
        });
    }

    [Fact]
    public void IgnoreMember_Expression_MultipleMember()
    {
        var options = new HumanReadableSerializerOptions();
        options.PropertyOrder = StringComparer.Ordinal;
        options.IgnoreMember<Exception>(exception => new { exception.TargetSite, exception.Source, exception.StackTrace });

        AssertSerialization(new Validation
        {
            Subject = new Exception("test"),
            Options = options,
            Expected = """
                Data: []
                HResult: -2146233088
                HelpLink: <null>
                InnerException: <null>
                Message: test
                """,
        });
    }

    [Fact]
    public void IgnoreMember_Expression_InheritedProperty_TargetDerivedType()
    {
        var options = new HumanReadableSerializerOptions();
        options.PropertyOrder = StringComparer.Ordinal;
        options.IgnoreMember<PersonWithBase>(x => x.FirstName);

        AssertSerialization(new Validation
        {
            Subject = new PersonWithBase("John", "Doe"),
            Options = options,
            Expected = """
                LastName: Doe
                """,
        });
    }

    [Fact]
    public void IgnoreMember_Expression_InheritedProperty_TargetBaseType()
    {
        var options = new HumanReadableSerializerOptions();
        options.PropertyOrder = StringComparer.Ordinal;
        options.IgnoreMember<BasePersonWithFirstName>(x => x.FirstName);

        AssertSerialization(new Validation
        {
            Subject = new PersonWithBase("John", "Doe"),
            Options = options,
            Expected = """
                LastName: Doe
                """,
        });
    }

    [Fact]
    public void IgnoreMember()
    {
        var options = new HumanReadableSerializerOptions();
        options.PropertyOrder = StringComparer.Ordinal;
        options.IgnoreMember<Exception>("Source", "HResult", "TargetSite", "Data");

        AssertSerialization(new Validation
        {
            Subject = new Exception("test"),
            Options = options,
            Expected = """
                HelpLink: <null>
                InnerException: <null>
                Message: test
                StackTrace: <null>
                """,
        });
    }

    [Fact]
    public void IgnoreMemberWithType()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMembersWithType<string>();

        AssertSerialization(new Validation
        {
            Subject = new { Prop1 = "A", Prop2 = 1 },
            Options = options,
            Expected = """
                Prop2: 1
                """,
        });
    }

    [Fact]
    public void AddPropertyInfoAttribute()
    {
        var options = new HumanReadableSerializerOptions();
        options.PropertyOrder = StringComparer.Ordinal;
        options.AddAttribute<Exception>(e => e.Source!, new HumanReadableIgnoreAttribute());
        options.AddAttribute<Exception>(e => new { e.HResult, e.TargetSite, e.Data }, new HumanReadableIgnoreAttribute());

        AssertSerialization(new Validation
        {
            Subject = new Exception("test"),
            Options = options,
            Expected = """
                HelpLink: <null>
                InnerException: <null>
                Message: test
                StackTrace: <null>
                """,
        });
    }

    [Fact]
    public void FuncConverter()
    {
        var options = new HumanReadableSerializerOptions();
        options.Converters.Add<double>(value => value.ToString("G17", CultureInfo.InvariantCulture));
        AssertSerialization(-5.30d, options, "-5.2999999999999998");
    }

    private sealed class PropThrowAnException
    {
        public int A => throw new NotSupportedException();
        public int B => 1;
    }

    private sealed class PropThrowAnExceptionAndNull
    {
        public int A => throw new NotSupportedException();
        public int B => 1;
        public string? C => null;
    }

    private sealed class FieldsWithAttributes
    {
        [HumanReadableDefaultValue(1)]
        public int WithDefaultValue = 1;

        [Obsolete("For tests")]
        public int ObsoleteField = 3;

        public int Other = 2;
    }

    private abstract class BasePersonWithFirstName(string firstName)
    {
        public string FirstName => firstName;
    }

    private sealed class PersonWithBase(string firstName, string lastName) : BasePersonWithFirstName(firstName)
    {
        public string LastName => lastName;
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
        private string? _privateField;

        [HumanReadableInclude]
        private string? _privateFieldIncluded;
        public string? FieldString;
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
        public object? PropObject { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.Never)]
        public object? PropObject2 { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.WhenWritingDefault)]
        public object? PropObject3 { get; set; }

        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.Always)]
        public object? PropObject4 { get; set; }
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
            public override bool CanConvertTo(ITypeDescriptorContext? context, [NotNullWhen(true)] Type? destinationType) => destinationType == typeof(string);

            public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType) => "converter";
        }
    }

    private sealed class CustomConvertible : IConvertible
    {
        public TypeCode GetTypeCode() => throw new NotSupportedException();
        public bool ToBoolean(IFormatProvider? provider) => throw new NotSupportedException();
        public byte ToByte(IFormatProvider? provider) => throw new NotSupportedException();
        public char ToChar(IFormatProvider? provider) => throw new NotSupportedException();
        public DateTime ToDateTime(IFormatProvider? provider) => throw new NotSupportedException();
        public decimal ToDecimal(IFormatProvider? provider) => throw new NotSupportedException();
        public double ToDouble(IFormatProvider? provider) => throw new NotSupportedException();
        public short ToInt16(IFormatProvider? provider) => throw new NotSupportedException();
        public int ToInt32(IFormatProvider? provider) => throw new NotSupportedException();
        public long ToInt64(IFormatProvider? provider) => throw new NotSupportedException();
        public sbyte ToSByte(IFormatProvider? provider) => throw new NotSupportedException();
        public float ToSingle(IFormatProvider? provider) => throw new NotSupportedException();
        public string ToString(IFormatProvider? provider) => "convertible";
        public object ToType(Type conversionType, IFormatProvider? provider) => throw new NotSupportedException();
        public ushort ToUInt16(IFormatProvider? provider) => throw new NotSupportedException();
        public uint ToUInt32(IFormatProvider? provider) => throw new NotSupportedException();
        public ulong ToUInt64(IFormatProvider? provider) => throw new NotSupportedException();
    }

    private sealed class Recursive
    {
        public Recursive Prop => this;
    }

    private sealed class Recursive2
    {
        public List<Recursive2> Items { get; } = [];
    }

    private sealed class ClassWithAttributes
    {
        [HumanReadableIgnore]
        public int Prop1 { get; set; }

        [HumanReadablePropertyName("Prop 2 Display")]
        public int Prop2 { get; set; }

        [HumanReadableConverter(typeof(CustomStringConverter))]
        public string? Prop3 { get; set; }

        private sealed class CustomStringConverter : HumanReadableConverter<string>
        {
            protected override void WriteValue(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
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
            protected override void WriteValue(HumanReadableTextWriter writer, string? value, HumanReadableSerializerOptions options)
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
        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) => throw new NotSupportedException();
    }

    private sealed class CustomStringComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => x == y;
        public int GetHashCode([DisallowNull] string obj) => 0;
    }

    [HumanReadableConverter(typeof(ClassWithCustomConverterConverter))]
    private sealed record ClassWithCustomConverter();

    private sealed class ClassWithCustomConverterConverter : HumanReadableConverter<ClassWithCustomConverter>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, ClassWithCustomConverter? value, HumanReadableSerializerOptions options)
        {
            writer.WriteValue("dummy");
        }
    }

    private sealed class ClassWithStaticCtor
    {
        static ClassWithStaticCtor() { Console.WriteLine(); }
    }

#if NET11_0_OR_GREATER
    private sealed record CSharpCat(string Name);

    private sealed record CSharpDog(string Name);

    private union CSharpPet(CSharpCat, CSharpDog);
#endif

    private interface ICovariantContravariantInterface<in T1, out T2> { }

    private interface ICovariantWithConstraintInterface<out T> where T : class { }

    private class Root
    {
        public int PropRoot { get; set; } = 1;
    }

    private sealed class Child : Root
    {
        public new int PropRoot { get; set; } = 2;
        public int PropChild { get; set; } = 3;
    }

    private sealed class ChildWithNewMemberType : Root
    {
        public new string PropRoot { get; set; } = "child";
        public int PropChild { get; set; } = 3;
    }

    private sealed class ChildWithIgnoredNewMember : Root
    {
        [HumanReadableIgnore]
        public new string PropRoot { get; set; } = "child";
        public int PropChild { get; set; } = 3;
    }

    private class RootWithPrivateMember
    {
        [HumanReadableInclude]
        [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Read by the serializer")]
        private int PrivateRoot { get; } = 1;
    }

    private sealed class ChildOfRootWithPrivateMember : RootWithPrivateMember
    {
        public int PropChild { get; set; } = 3;
    }

    private sealed class Methods
    {
        [SuppressMessage("Style", "IDE0060:Remove unused parameter")]
        public void NotNamed((int, string) a) { }

        [SuppressMessage("Style", "IDE0060:Remove unused parameter")]
        public void Named((int A, string B) a) { }

        public (int A, int B) NamedResult() => default;

        public (int A, int B) NamedProperty => default;

        public void GenericMethod<TEnum>(string value) => throw new NotSupportedException();

        public void Dynamic(dynamic value) => throw new NotSupportedException();

        public void ValueTupleDynamic((dynamic, int) value) => throw new NotSupportedException();

        public void ValueTupleNestedDynamic((dynamic A, (int B, dynamic C) D) value) => throw new NotSupportedException();

        public void ByRefParameters(ref int a, out List<int>[] b, ref dynamic c) => throw new NotSupportedException();

        public void LongValueTuple((dynamic a, int b, int c, int d, int e, int f, int g, dynamic h, (int x, dynamic y) i) value) => throw new NotSupportedException();

        public void NestedValueTupleFirst(((int x, int y) p, int q) value) => throw new NotSupportedException();

        public void InParameters(in int a, ref readonly int b, out int c) => throw new NotSupportedException();
    }

    [Fact]
    public void Component_IsSerializedAsObject()
    {
        using var component = new SampleComponent();

        AssertSerialization(component, new HumanReadableSerializerOptions { PropertyOrder = StringComparer.Ordinal }, """
            Container: <null>
            Site: <null>
            Value: 1
            """);
    }

    [Fact]
    public void DataTable_IsSerializedAsObject()
    {
        using var table = new DataTable("Sample");

        var text = HumanReadableSerializer.Serialize(new ValueHolder { Value = table });

        Assert.Contains("TableName: Sample", text);
    }

    [Fact]
    public void Serialize_AsInterfaceType()
    {
        AssertSerialization(new ImplementsDerivedInterface(), options: null, typeof(IDerivedInterface), """
            B: 2
            A: 1
            """);
    }

    [Fact]
    public void IgnoreMember_Expression_OverriddenProperty()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMember<BaseWithVirtualProperty>(x => x.Name);

        AssertSerialization(new DerivedWithOverriddenProperty(), options, "Id: 1");
    }

    [Fact]
    public void IgnoreMember_Expression_OverriddenPropertyOfBaseClassLibraryType()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMember<ObjectDisposedException>(e => e.Message);

        // ObjectDisposedException overrides Exception.Message, and derives from InvalidOperationException
        Assert.DoesNotContain("Message:", HumanReadableSerializer.Serialize(new ObjectDisposedException("Sample"), options));
        Assert.Contains("Message: msg", HumanReadableSerializer.Serialize(new InvalidOperationException("msg"), options));
    }

    [Fact]
    public void IgnoreMember_Expression_OnlyAppliesToTheConfiguredGenericInstantiation()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMember<GenericBox<int>>(x => x.Value);

        AssertSerialization(new GenericBox<int> { Value = 2, Id = 1 }, options, "Id: 1");
        AssertSerialization(new GenericBox<string> { Value = "a", Id = 1 }, options, """
            Value: a
            Id: 1
            """);
    }

    [Fact]
    public void IgnoreMember_Expression_DerivedType_DoesNotApplyToBaseType()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMember<DerivedWithOverriddenProperty>(x => x.Id);

        AssertSerialization(new DerivedWithOverriddenProperty(), options, "Name: derived");
        AssertSerialization(new BaseWithVirtualProperty(), options, """
            Name: base
            Id: 1
            """);
    }

    [Fact]
    public void IgnoreMember_Expression_InterfaceProperty()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMember<IBaseInterface>(x => x.A);

        AssertSerialization(new ImplementsDerivedInterface(), options, "B: 2");
    }

    [Fact]
    public void IgnoreMember_Name_PrivateMemberOfBaseType()
    {
        var options = new HumanReadableSerializerOptions();
        options.IgnoreMember<ChildOfRootWithPrivateMember>("PrivateRoot");

        AssertSerialization(new ChildOfRootWithPrivateMember(), options, "PropChild: 3");
    }

    [Fact]
    public void AddAttribute_Field()
    {
        var options = new HumanReadableSerializerOptions { IncludeFields = true };
        options.AddAttribute(typeof(ClassWithFields).GetField(nameof(ClassWithFields.A))!, new HumanReadablePropertyNameAttribute("Renamed"));
        options.AddFieldAttribute(field => field.Name == nameof(ClassWithFields.B), new HumanReadableIgnoreAttribute());

        AssertSerialization(new ClassWithFields(), options, "Renamed: 1");
    }

    [Fact]
    public void IgnoreMembersWithType_Field()
    {
        var options = new HumanReadableSerializerOptions { IncludeFields = true };
        options.IgnoreMembersWithType<string>();

        AssertSerialization(new ClassWithFields(), options, "A: 1");
    }

    [Fact]
    public void AddAttribute_Type_AppliesToDerivedTypes()
    {
        var options = new HumanReadableSerializerOptions();
        options.AddAttribute(typeof(BaseWithVirtualProperty), new HumanReadableConverterAttribute(new ConstantConverter()));

        AssertSerialization(new DerivedWithOverriddenProperty(), options, "custom");
    }

    [Fact]
    public void ConverterAttribute_Factory_Type()
    {
        AssertSerialization(new TypeWithConverterFactory(), "custom");
    }

    [Fact]
    public void ConverterAttribute_Factory_Member()
    {
        AssertSerialization(new MemberWithConverterFactory(), "Value: custom");
    }

    [Fact]
    public void ConverterAttribute_Factory_ReturnsNull()
    {
        var options = new HumanReadableSerializerOptions();
        options.AddAttribute(typeof(ClassWithFields), new HumanReadableConverterAttribute(new NullConverterFactory()));

        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(new ClassWithFields(), options));
    }

    [Fact]
    public void ConverterAttribute_Interface()
    {
        AssertSerialization(new ImplementsInterfaceWithConverter(), "custom");
        AssertSerialization(new ValueHolder<IInterfaceWithConverter> { Value = new ImplementsInterfaceWithConverter() }, "Value: custom");
    }

    [Fact]
    public void ConverterAttribute_Interface_MostSpecificInterfaceWins()
    {
        AssertSerialization(new ImplementsDerivedInterfaceWithConverter(), "derived");
    }

    [Fact]
    public void ConverterAttribute_Interface_Ambiguous()
    {
        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(new ImplementsTwoInterfacesWithConverter()));
    }

    [Fact]
    public void ConverterAttribute_Struct()
    {
        AssertSerialization(new StructWithConverter(), "custom");
    }

    [Fact]
    public void DefaultIgnoreCondition_Always_MemberCanOptIn()
    {
        var options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.Always };

        AssertSerialization(new OptInMembers(), options, "A: 1");
    }

    [Fact]
    public void WhenWritingDefault_SpanProperty()
    {
        var options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault };
        options.IgnoreMembersThatThrow();

        AssertSerialization(new WithSpanProperty(), options, "Value: 1");
    }

    [Fact]
    public void RefReturningProperty()
    {
        AssertSerialization(new WithRefProperties(), """
            Reference: <null>
            Value: 0
            """);
    }

    [Fact]
    public void RefReturningProperty_WhenWritingDefault()
    {
        var options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault };

        AssertSerialization(new WithRefProperties(), options, "{}");
    }

    [Fact]
    public void DefaultValueAttribute_IsConvertedToTheMemberType()
    {
        var options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault };

        AssertSerialization(new WithConvertibleDefaultValues(), options, "Other: 1");
    }

    [Fact]
    public void DefaultValueAttribute_InvalidValue()
    {
        var options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault };

        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(new WithInvalidDefaultValue(), options));
    }

    [Fact]
    public void String_InvisibleChar_FormatAndC1ControlCharacters()
    {
        var options = new HumanReadableSerializerOptions { ShowInvisibleCharactersInValues = true };

        AssertSerialization("a­b‎c‮de⁦f", options, "a<U+00AD>b<U+200E>c<U+202E>d<U+0081>e<U+2066>f");
    }

    [Fact]
    public void Array_EmptyStringItem_NoTrailingWhitespace()
    {
        AssertSerialization(new[] { "", "a" }, "-\n- a");
    }

    [Fact]
    public void Array_ItemStartingWithNewLine_NoTrailingWhitespace()
    {
        AssertSerialization(new[] { "\na" }, "-\n  a");
    }

    [Fact]
    public void MultiDimensionalArray_EmptyStringItem_NoTrailingWhitespace()
    {
        AssertSerialization(new string[,] { { "", "a" } }, "- [0, 0]:\n- [0, 1]: a");
    }

    [Fact]
    public void ConverterRecursion_RespectsMaxDepth()
    {
        var box = new SelfReferencingBox();
        box.Inner = box;
        var options = new HumanReadableSerializerOptions();
        options.Converters.Add(new SelfReferencingBoxConverter());

        Assert.Throws<HumanReadableSerializerException>(() => HumanReadableSerializer.Serialize(box, options));
    }

    [Fact]
    public void StringDictionary_DefaultOrder()
    {
        var value = new StringDictionary();
        for (var i = 19; i >= 0; i--)
        {
            value["key" + i.ToString("D2", CultureInfo.InvariantCulture)] = "v" + i.ToString(CultureInfo.InvariantCulture);
        }

        value["null"] = null;

        var expected = string.Join('\n', Enumerable.Range(0, 20).Select(i => FormattableString.Invariant($"key{i:D2}: v{i}"))) + "\nnull: <null>";
        AssertSerialization(value, expected);
    }

    [Fact]
    public void Hashtable_DefaultOrder()
    {
        var value = new Hashtable();
        for (var i = 19; i >= 0; i--)
        {
            value["key" + i.ToString("D2", CultureInfo.InvariantCulture)] = i;
        }

        var expected = string.Join('\n', Enumerable.Range(0, 20).Select(i => FormattableString.Invariant($"- Key: key{i:D2}\n  Value: {i}")));
        AssertSerialization(value, expected);
    }

    [Fact]
    public void Hashtable_Empty()
    {
        AssertSerialization(new Hashtable(), "[]");
    }

    [Fact]
    public void ListDictionary_KeepsInsertionOrder()
    {
        var value = new ListDictionary { ["b"] = 1, ["a"] = 2 };

        AssertSerialization(value, """
            - Key: b
              Value: 1
            - Key: a
              Value: 2
            """);
    }

    [Fact]
    public void ListDictionary_DictionaryKeyOrder()
    {
        var value = new ListDictionary { ["b"] = 1, ["a"] = 2 };

        AssertSerialization(value, new HumanReadableSerializerOptions { DictionaryKeyOrder = StringComparer.Ordinal }, """
            - Key: a
              Value: 2
            - Key: b
              Value: 1
            """);
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Expression_ConstantsUseInvariantCulture()
    {
        Expression<Func<double, bool>> expression = x => x > 1.5;

        var text = UseCulture("fr-FR", () => HumanReadableSerializer.Serialize(expression));

        Assert.Equal("x => (x > 1.5)", text);
    }

    [Fact]
    public void Enum_UndefinedNegativeValue()
    {
        AssertSerialization((DayOfWeek)(-1), "-1");
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void Enum_UndefinedNegativeValue_UsesInvariantCulture()
    {
        var text = UseCulture("sv-SE", () => HumanReadableSerializer.Serialize((DayOfWeek)(-1)));

        Assert.Equal("-1", text);
    }

    [Fact]
    public void JsonElement_Default()
    {
        AssertSerialization(new ValueHolder<System.Text.Json.JsonElement>(), "Value: <undefined>");
    }

    [Fact]
    public void JsonElement_DoesNotEscapeNonAsciiCharacters()
    {
        using var document = System.Text.Json.JsonDocument.Parse("""{"Name":"café <b>"}""");

        AssertSerialization(document.RootElement, """
            {
              "Name": "café <b>"
            }
            """);
    }

    [Fact]
    public void SByteArray_IsNotSerializedAsBase64()
    {
        AssertSerialization(new sbyte[] { -1, 2 }, "- -1\n- 2");
    }

    [Fact]
    public void ByteEnumArray_IsNotSerializedAsBase64()
    {
        AssertSerialization(new[] { ByteEnum.A }, "- A");
    }

    [Fact]
    public void Grouping()
    {
        AssertSerialization(new[] { 1, 2, 3 }.GroupBy(x => x % 2), """
            - Key: 1
              Values:
                - 1
                - 3
            - Key: 0
              Values:
                - 2
            """);
    }

    [Fact]
    public void Type_NestedTypeOfGenericType() => AssertSerialization(typeof(List<int>.Enumerator), "System.Collections.Generic.List<System.Int32>+Enumerator, System.Private.CoreLib");

    [Fact]
    public void Type_NestedTypeOfOpenGenericType() => AssertSerialization(typeof(Dictionary<,>.KeyCollection), "System.Collections.Generic.Dictionary<TKey, TValue>+KeyCollection, System.Private.CoreLib");

    [Fact]
    public void Type_GenericNestedTypeOfGenericType() => AssertSerialization(typeof(GenericBox<int>.Nested<string>), "Meziantou.Framework.HumanReadable.Tests.SerializerTests+GenericBox<System.Int32>+Nested<System.String>, Meziantou.Framework.HumanReadableSerializer.Tests");

    [Fact]
    public void MethodInfo_LongValueTuple_Parameter() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.LongValueTuple))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.LongValueTuple((dynamic a, System.Int32 b, System.Int32 c, System.Int32 d, System.Int32 e, System.Int32 f, System.Int32 g, dynamic h, (System.Int32 x, dynamic y) i) value)");

    [Fact]
    public void MethodInfo_NestedValueTuple_FirstElement_Parameter() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.NestedValueTupleFirst))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.NestedValueTupleFirst(((System.Int32 x, System.Int32 y) p, System.Int32 q) value)");

    [Fact]
    public void MethodInfo_InAndRefReadOnlyParameters() => AssertSerialization(typeof(Methods).GetMethod(nameof(Methods.InParameters))!, "Meziantou.Framework.HumanReadable.Tests.SerializerTests+Methods.InParameters(in System.Int32 a,ref readonly System.Int32 b,out System.Int32 c)");

    [Fact]
    public void SingleDimensionArray_NonZeroLowerBound()
    {
        var data = Array.CreateInstance(typeof(int), lengths: [3], lowerBounds: [5]);
        data.SetValue(1, 5);
        data.SetValue(2, 6);
        data.SetValue(3, 7);

        AssertSerialization(data, """
            - [5]: 1
            - [6]: 2
            - [7]: 3
            """);
    }

    [Fact]
    public void CultureInfo_InvariantInstance()
        => AssertSerialization((CultureInfo)CultureInfo.InvariantCulture.Clone(), "Invariant Language (Invariant Country)");

    [Fact]
    public void FSharp_DiscriminatedUnion_WhenWritingDefault()
    {
        var options = new HumanReadableSerializerOptions { DefaultIgnoreCondition = HumanReadableIgnoreCondition.WhenWritingDefault };

        AssertSerialization(Shape.NewRectangle(0, 2), options, """
            Tag: Rectangle
            length: 2
            """);
    }

    [Fact]
    public void HttpContent_MultiPartContent_RandomBoundary()
    {
        static string Serialize()
        {
            using var part = new StringContent("a");
            using var content = new MultipartFormDataContent { { part, "field" } };
            return HumanReadableSerializer.Serialize(content);
        }

        Assert.Equal(Serialize(), Serialize());
    }

    [Fact]
    public void HttpContent_UnsupportedCharset()
    {
        using var content = new ByteArrayContent([0x63, 0x61, 0x66, 0xE9]);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/plain; charset=x-unknown");

        AssertSerialization(content, """
            Headers:
              Content-Type: text/plain; charset=x-unknown
            Value: Y2Fm6Q==
            """);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public void HttpContent_CompressedContent(string encoding)
    {
        using var output = new MemoryStream();
        using (Stream compressionStream = encoding switch
        {
            "gzip" => new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true),
            "deflate" => new System.IO.Compression.ZLibStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true),
            _ => new System.IO.Compression.BrotliStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true),
        })
        {
            compressionStream.Write("{\"a\":1}"u8);
        }

        using var content = new ByteArrayContent(output.ToArray());
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
        content.Headers.ContentEncoding.Add(encoding);

        AssertSerialization(content, $$"""
            Headers:
              Content-Type: application/json; charset=utf-8
              Content-Encoding: {{encoding}}
            Value: {"a":1}
            """);
    }

    [Fact]
    public void HttpContent_UnknownContentEncoding()
    {
        using var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("text/plain");
        content.Headers.ContentEncoding.Add("zstd");

        AssertSerialization(content, """
            Headers:
              Content-Type: text/plain
              Content-Encoding: zstd
            Value: AQID
            """);
    }

    [Fact]
    public void HttpContent_StringContentWithContentEncodingHeader()
    {
        using var content = new StringContent("text");
        content.Headers.ContentEncoding.Add("gzip");

        AssertSerialization(content, """
            Headers:
              Content-Type: text/plain; charset=utf-8
              Content-Encoding: gzip
            Value: text
            """);
    }

    private static string UseCulture(string cultureName, Func<string> func)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            return func();
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private sealed class SampleComponent : Component
    {
        public int Value => 1;
    }

    private sealed class ValueHolder
    {
        public object? Value { get; set; }
    }

    private sealed class ValueHolder<T>
    {
        public T? Value { get; set; }
    }

    private interface IBaseInterface
    {
        int A { get; }
    }

    private interface IDerivedInterface : IBaseInterface
    {
        int B { get; }
    }

    private sealed class ImplementsDerivedInterface : IDerivedInterface
    {
        public int A => 1;
        public int B => 2;
    }

    private class BaseWithVirtualProperty
    {
        public virtual string Name => "base";
        public int Id => 1;
    }

    private sealed class DerivedWithOverriddenProperty : BaseWithVirtualProperty
    {
        public override string Name => "derived";
    }

    private sealed class GenericBox<T>
    {
        public T? Value { get; set; }
        public int Id { get; set; }

        [SuppressMessage("Performance", "CA1812", Justification = "Used through typeof")]
        public sealed class Nested<TNested>;
    }

    private sealed class ClassWithFields
    {
        public int A = 1;
        public string B = "b";
    }

    private sealed class ConstantConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => true;
        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) => writer.WriteValue("custom");
    }

    private sealed class ConstantConverterFactory : HumanReadableConverterFactory
    {
        public override bool CanConvert(Type type) => true;
        public override HumanReadableConverter CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options) => new ConstantConverter();
    }

    private sealed class NullConverterFactory : HumanReadableConverterFactory
    {
        public override bool CanConvert(Type type) => true;
        public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options) => null;
    }

    private sealed class DerivedConverter : HumanReadableConverter
    {
        public override bool CanConvert(Type type) => true;
        public override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) => writer.WriteValue("derived");
    }

    [HumanReadableConverter(typeof(ConstantConverterFactory))]
    private sealed class TypeWithConverterFactory
    {
        public int A => 1;
    }

    private sealed class MemberWithConverterFactory
    {
        [HumanReadableConverter(typeof(ConstantConverterFactory))]
        public int Value => 1;
    }

    [HumanReadableConverter(typeof(ConstantConverter))]
    private interface IInterfaceWithConverter;

    [HumanReadableConverter(typeof(DerivedConverter))]
    private interface IDerivedInterfaceWithConverter : IInterfaceWithConverter;

    [HumanReadableConverter(typeof(DerivedConverter))]
    private interface IOtherInterfaceWithConverter;

    private sealed class ImplementsInterfaceWithConverter : IInterfaceWithConverter
    {
        public int A => 1;
    }

    private sealed class ImplementsDerivedInterfaceWithConverter : IDerivedInterfaceWithConverter
    {
        public int A => 1;
    }

    private sealed class ImplementsTwoInterfacesWithConverter : IInterfaceWithConverter, IOtherInterfaceWithConverter
    {
        public int A => 1;
    }

    [HumanReadableConverter(typeof(ConstantConverter))]
    private readonly struct StructWithConverter
    {
        public int A => 1;
    }

    private sealed class OptInMembers
    {
        [HumanReadableIgnore(Condition = HumanReadableIgnoreCondition.Never)]
        public int A => 1;

        public int B => 2;
    }

    private sealed class WithSpanProperty
    {
        public Span<byte> Buffer => new byte[1];
        public int Value => 1;
    }

    private sealed class WithRefProperties
    {
        private string? _reference;
        private int _value;

        public ref string? Reference => ref _reference;
        public ref int Value => ref _value;
    }

    private sealed class WithConvertibleDefaultValues
    {
        [HumanReadableDefaultValue(10)]
        public long Timeout => 10;

        [HumanReadableDefaultValue(1)]
        public double? Ratio => 1d;

        [HumanReadableDefaultValue(1)]
        public ByteEnum Enum => ByteEnum.A;

        public int Other => 1;
    }

    private sealed class WithInvalidDefaultValue
    {
        [HumanReadableDefaultValue("abc")]
        public int Value => 1;
    }

    private enum ByteEnum : byte
    {
        A = 1,
    }

    private sealed class SelfReferencingBox
    {
        public SelfReferencingBox? Inner { get; set; }
    }

    private sealed class SelfReferencingBoxConverter : HumanReadableConverter<SelfReferencingBox>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, SelfReferencingBox? value, HumanReadableSerializerOptions options)
            => HumanReadableSerializer.Serialize(writer, value!.Inner, options);
    }
}

