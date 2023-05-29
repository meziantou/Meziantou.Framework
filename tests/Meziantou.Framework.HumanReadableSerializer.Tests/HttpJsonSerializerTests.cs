using Meziantou.Framework.HumanReadable.Converters;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Xunit;

namespace Meziantou.Framework.HumanReadable.Tests;
public sealed class HttpJsonSerializerTests : SerializerTestsBase
{
    private static readonly HumanReadableSerializerOptions IndentedAndOrderedOptions = new HumanReadableSerializerOptions()
        .AddJsonFormatter(new JsonFormatterOptions
        {
            WriteIndented = true,
            OrderProperties = true,
        });

    private static readonly HumanReadableSerializerOptions NonIndentedAndOrderedOptions = new HumanReadableSerializerOptions()
        .AddJsonFormatter(new JsonFormatterOptions
        {
            WriteIndented = false,
            OrderProperties = true,
        });

    private static readonly HumanReadableSerializerOptions IndentedAndNotOrderedOptions = new HumanReadableSerializerOptions()
        .AddJsonFormatter(new JsonFormatterOptions
        {
            WriteIndented = true,
            OrderProperties = false,
        });

    [Fact]
    public void Indented_Ordered_InvalidJson()
    {
        using var httpContent = new StringContent("dummy", encoding: null, "application/json");
        AssertSerialization(httpContent, IndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value: dummy
            """);
    }

    [Fact]
    public void Indented_Ordered_NullValue()
    {
        using var httpContent = new StringContent("null", encoding: null, "application/json");
        AssertSerialization(httpContent, IndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value: null
            """);
    }

    [Fact]
    public void Indented_Ordered_NumberValue()
    {
        using var httpContent = new StringContent("42", encoding: null, "application/json");
        AssertSerialization(httpContent, IndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value: 42
            """);
    }

    [Fact]
    public void Indented_Ordered_SimpleObject()
    {
        using var httpContent = new StringContent("""{"foo":"bar","answer":42}""", encoding: null, "application/json");
        AssertSerialization(httpContent, IndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value:
              {
                "answer": 42,
                "foo": "bar"
              }
            """);
    }

    [Fact]
    public void Indented_Ordered_SimpleArray()
    {
        using var httpContent = new StringContent("""[3,2,1]""", encoding: null, "application/json");
        AssertSerialization(httpContent, IndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value:
              [
                3,
                2,
                1
              ]
            """);
    }

    [Fact]
    public void Indented_Ordered_NestedObjectsAndArray()
    {
        using var httpContent = new StringContent("""{"foo":"bar","answers":[ {"z": 1, "a": 2, "c":3 } ]}""", encoding: null, "application/json");
        AssertSerialization(httpContent, IndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value:
              {
                "answers": [
                  {
                    "a": 2,
                    "c": 3,
                    "z": 1
                  }
                ],
                "foo": "bar"
              }
            """);
    }

    [Fact]
    public void Indented_NonOrdered_NestedObjectsAndArray()
    {
        using var httpContent = new StringContent("""{"foo":"bar","answers":[ {"z": 1, "a": 2, "c":3 } ]}""", encoding: null, "application/json");
        AssertSerialization(httpContent, IndentedAndNotOrderedOptions, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value:
              {
                "foo": "bar",
                "answers": [
                  {
                    "z": 1,
                    "a": 2,
                    "c": 3
                  }
                ]
              }
            """);
    }

    [Fact]
    public void NonIndented_Ordered_NestedObjectsAndArray()
    {
        using var httpContent = new StringContent("""{"foo":"bar","answers":[ {"z": 1, "a": 2, "c":3 } ]}""", encoding: null, "application/json");
        AssertSerialization(httpContent, NonIndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value: {"answers":[{"a":2,"c":3,"z":1}],"foo":"bar"}
            """);
    }
}
