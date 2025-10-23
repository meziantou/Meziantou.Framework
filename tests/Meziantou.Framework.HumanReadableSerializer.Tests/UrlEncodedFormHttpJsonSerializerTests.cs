using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable.Tests;
public sealed class UrlEncodedFormHttpJsonSerializerTests : SerializerTestsBase
{
    private static void AssertUrlSerialization(string url, UrlEncodedFormFormatterOptions options, string expected)
    {
        using var httpContent = new ByteArrayContent(Encoding.UTF8.GetBytes(url))
        {
            Headers =
            {
                ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded"),
            },
        };

        AssertSerialization(httpContent, new HumanReadableSerializerOptions().AddUrlEncodedFormFormatter(options), type: null, expected);
    }

    [Fact]
    public void PrettyFormat_OrderProperties_UnescapeValues1()
    {
        var options = new UrlEncodedFormFormatterOptions();
        AssertUrlSerialization("a=b&c=d&ab", options, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value:
              a: b
              ab:
              c: d
            """);
    }

    [Fact]
    public void PrettyFormat_OrderProperties_UnescapeValues2()
    {
        var options = new UrlEncodedFormFormatterOptions();
        AssertUrlSerialization("a=b&c=d", options, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value:
              a: b
              c: d
            """);
    }

    [Fact]
    public void PrettyFormat_OrderProperties_UnescapeValues3()
    {
        var options = new UrlEncodedFormFormatterOptions();
        AssertUrlSerialization("a%22=b%22&c%26=d+fs", options, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value:
              a": b"
              c&: d fs
            """);
    }

    [Fact]
    public void PrettyFormat_OrderProperties()
    {
        var options = new UrlEncodedFormFormatterOptions { UnescapeValues = false };
        AssertUrlSerialization("a%22=b%22&c%26=d+fs", options, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value:
              a%22: b%22
              c%26: d+fs
            """);
    }

    [Fact]
    public void PrettyFormat()
    {
        var options = new UrlEncodedFormFormatterOptions { UnescapeValues = false, OrderProperties = false };
        AssertUrlSerialization("z=b&a=d", options, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value:
              z: b
              a: d
            """);
    }

    [Fact]
    public void OrderProperties()
    {
        var options = new UrlEncodedFormFormatterOptions { UnescapeValues = false, OrderProperties = true, PrettyFormat = false };
        AssertUrlSerialization("z=b&a=d", options, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value: a=d&z=b
            """);
    }
}
