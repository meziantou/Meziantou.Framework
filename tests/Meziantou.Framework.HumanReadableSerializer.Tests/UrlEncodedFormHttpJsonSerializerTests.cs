using System.Text;
using Meziantou.Framework.HumanReadable.ValueFormatters;
using Xunit;

namespace Meziantou.Framework.HumanReadable.Tests;
public sealed class UrlEncodedFormHttpJsonSerializerTests : SerializerTestsBase
{
    private static readonly HumanReadableSerializerOptions PrettyFormatOptions = new HumanReadableSerializerOptions()
        .AddUrlEncodedFormFormatter(new UrlEncodedFormFormatterOptions
        {
            PrettyFormat = true,
        });

    [Fact]
    public void PrettyFormat_InvalidData()
    {
        using var httpContent = new ByteArrayContent(Encoding.UTF8.GetBytes("a=b&c=d&sd"))
        {
            Headers =
            {
                ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded"),
            },
        };

        AssertSerialization(httpContent, PrettyFormatOptions, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value: a=b&c=d&sd
            """);
    }

    [Fact]
    public void PrettyFormat()
    {
        using var httpContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("a", "b"),
            new KeyValuePair<string, string>("c", "d"),
        });

        AssertSerialization(httpContent, PrettyFormatOptions, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value:
              a: b
              c: d
            """);
    }

    [Fact]
    public void PrettyFormat_DecodeValues()
    {
        using var httpContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("a\"", "b\""),
            new KeyValuePair<string, string>("c&", "d fs"),
        });

        AssertSerialization(httpContent, PrettyFormatOptions, """
            Headers:
              Content-Type: application/x-www-form-urlencoded
            Value:
              a": b"
              c&: d fs
            """);
    }
}
