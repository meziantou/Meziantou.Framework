using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable.Tests;

public sealed class HttpHtmlSerializerTests
{
    private static void AssertSerialization(object value, HtmlFormatterOptions formatterOptions, string expected)
    {
        var options = new HumanReadableSerializerOptions()
               .AddHtmlFormatter(formatterOptions);

        var text = HumanReadableSerializer.Serialize(value, options);
        Assert.Equal(expected, text, ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ReadmeExample_Usage()
    {
        using var httpResponseMessage = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""<p z='1' a=2 nonce=abc>test</p>""", encoding: null, "text/html"),
        };

        #region HtmlFormatter_Usage
        var options = new HumanReadableSerializerOptions()
            .AddHtmlFormatter(new HtmlFormatterOptions
            {
                OrderAttributes = true,
                RedactContentSecurityPolicyNonce = true,
            });

        var text = HumanReadableSerializer.Serialize(httpResponseMessage, options);
        #endregion

        Assert.Contains("""Value: <p a="2" nonce="[redacted]" z="1">test</p>""", text);
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
            Value: <p a="2" d="3" z="1">test</p>
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
    public void NormalizeSimpleQuote_EscapesSimpleQuote()
    {
        using var httpContent = new StringContent("""<p title="it's &amp; <b>">test</p>""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { AttributeQuote = HtmlAttributeQuote.SimpleQuote }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <p title='it&#39;s &amp; &lt;b&gt;'>test</p>
            """);
    }

    [Fact]
    public void Redact_Nonce()
    {
        using var httpContent = new StringContent("""<p z='"' nonce="value" a=2 d="3">test</p>""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { RedactContentSecurityPolicyNonce = true }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <p z="&quot;" nonce="[redacted]" a="2" d="3">test</p>
            """);
    }

    [Fact]
    public void Document()
    {
        using var httpContent = new StringContent("""<!DOCTYPE html><html lang=en><head><title>Test</title></head><body><p z=1 a=2>line<br/>break</p></body></html>""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { OrderAttributes = true }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <!DOCTYPE html><html lang="en"><head><title>Test</title></head><body><p a="2" z="1">line<br>break</p></body></html>
            """);
    }

    [Fact]
    public void Template()
    {
        using var httpContent = new StringContent("""<template><script nonce="value" z=1 a=2></script></template>""", encoding: null, "text/html");

        AssertSerialization(httpContent, new HtmlFormatterOptions { OrderAttributes = true, RedactContentSecurityPolicyNonce = true }, """
            Headers:
              Content-Type: text/html; charset=utf-8
            Value: <template><script a="2" nonce="[redacted]" z="1"></script></template>
            """);
    }
}
