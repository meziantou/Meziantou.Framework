using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable.Tests;

public sealed class HttpHtmlSerializerTests : SerializerTestsBase
{
    private static void AssertSerialization(object value, HtmlFormatterOptions formatterOptions, string expected)
    {
        var options = new HumanReadableSerializerOptions()
               .AddHtmlFormatter(formatterOptions);

        AssertSerialization(value, options, expected);
    }

    [Fact]
    public void StringValue()
    {
        using var httpContent = new StringContent("""dummy""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { OrderAttributes = true }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: dummy
            """);
    }

    [Fact]
    public void Order_Attributes_Element()
    {
        using var httpContent = new StringContent("""<p z='1' a=2 d="3">test</p>""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { OrderAttributes = true }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <p a="2" d="3" z='1'>test</p>
            """);
    }

    [Fact]
    public void Order_NormalizeDoubleQuote_Attributes_Element()
    {
        using var httpContent = new StringContent("""<p z='"' a=2 d="3">test</p>""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { OrderAttributes = true, AttributeQuote = HtmlAttributeQuote.DoubleQuote }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <p a="2" d="3" z="&quot;">test</p>
            """);
    }

    [Fact]
    public void Order_NormalizeSimpleQuote_Attributes_Element()
    {
        using var httpContent = new StringContent("""<p nonce="value" z='"' a=2 d="3">test</p>""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { OrderAttributes = true, AttributeQuote = HtmlAttributeQuote.SimpleQuote }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <p a='2' d='3' nonce='value' z='"'>test</p>
            """);
    }

    [Fact]
    public void Redact_Nonce()
    {
        using var httpContent = new StringContent("""<p z='"' nonce="value" a=2 d="3">test</p>""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { RedactContentSecurityPolicyNonce = true }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <p z='"' nonce="[redacted]" a="2" d="3">test</p>
            """);
    }
}
