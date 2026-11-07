---
title: JSON, XML and HTML values
description: Indent and sort the JSON, XML, HTML and URL-encoded form values embedded in the serialized object.
---

JSON, XML or HTML embedded in a value is hard to read and compare on one line. Formatters rewrite it: they apply to
`JsonNode`, `JsonDocument` and `JsonElement` values, to `XmlNode` and `XElement` values, and to the content of HTTP
messages, according to its media type.

## JSON and XML

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_JsonAndXml;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions()
    .AddJsonFormatter(new JsonFormatterOptions { OrderProperties = true })
    .AddXmlFormatter(new XmlFormatterOptions { OrderAttributes = true });

var value = new
{
    Json = JsonNode.Parse("""{"name":"Ada","id":42}"""),
    Xml = XElement.Parse("""<user name="Ada" id="42"><role>admin</role></user>"""),
};

Assert.Equal("""
    Json:
      {
        "id": 42,
        "name": "Ada"
      }
    Xml:
      <user id="42" name="Ada">
        <role>admin</role>
      </user>
    """, HumanReadableSerializer.Serialize(value, options));
```
<!-- endref -->

| Method | Media types | Options |
| --- | --- | --- |
| `AddJsonFormatter(JsonFormatterOptions?)` | `application/json`, `text/json`, `*+json` | [WriteIndented](cref:Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatterOptions.WriteIndented) (default `true`), [OrderProperties](cref:Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatterOptions.OrderProperties) (default `false`), [FormatAsStandardObject](cref:Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatterOptions.FormatAsStandardObject) (default `false`) |
| `AddXmlFormatter(XmlFormatterOptions?)` | `application/xml`, `text/xml`, `*+xml` | [WriteIndented](cref:Meziantou.Framework.HumanReadable.ValueFormatters.XmlFormatterOptions.WriteIndented) (default `true`), [OrderAttributes](cref:Meziantou.Framework.HumanReadable.ValueFormatters.XmlFormatterOptions.OrderAttributes) (default `false`) |
| `AddUrlEncodedFormFormatter(UrlEncodedFormFormatterOptions?)` | `application/x-www-form-urlencoded` | [OrderProperties](cref:Meziantou.Framework.HumanReadable.ValueFormatters.UrlEncodedFormFormatterOptions.OrderProperties), [UnescapeValues](cref:Meziantou.Framework.HumanReadable.ValueFormatters.UrlEncodedFormFormatterOptions.UnescapeValues) and [PrettyFormat](cref:Meziantou.Framework.HumanReadable.ValueFormatters.UrlEncodedFormFormatterOptions.PrettyFormat) (default `true`) |

Without a formatter, the value is written as it is. A value that cannot be parsed, such as invalid JSON, is written as it
is too.

[FormatAsStandardObject](cref:Meziantou.Framework.HumanReadable.ValueFormatters.JsonFormatterOptions.FormatAsStandardObject) writes the JSON as the serializer writes objects, without braces and quotes:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Tests/HumanReadableSerializerOptionsTests.cs;region=HumanReadableSerializer_JsonAsObject;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new HumanReadableSerializerOptions()
    .AddJsonFormatter(new JsonFormatterOptions { FormatAsStandardObject = true });

var value = new { Json = JsonNode.Parse("""{"name":"Ada","roles":["admin","dev"]}""") };

Assert.Equal("""
    Json:
      name: Ada
      roles:
        - admin
        - dev
    """, HumanReadableSerializer.Serialize(value, options));
```
<!-- endref -->

## HTML

The `Meziantou.Framework.HumanReadableSerializer.Html` package adds a formatter for `text/html` values:

<InstallNuGetPackage Id="Meziantou.Framework.HumanReadableSerializer.Html" />

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Html.Tests/HttpHtmlSerializerTests.cs;region=HtmlFormatter_Normalize;format=md-fenced-code-block;dedent=true -->
```csharp
using var content = new StringContent(
    """<p id='greeting' class=intro nonce=r4nd0m>Hello<br/>world &amp; all</p>""",
    encoding: null,
    "text/html");

var options = new HumanReadableSerializerOptions()
    .AddHtmlFormatter(new HtmlFormatterOptions
    {
        OrderAttributes = true,
        RedactContentSecurityPolicyNonce = true,
    });

Assert.Equal("""
    Headers:
      Content-Type: text/html; charset=utf-8
    Value: <p class="intro" id="greeting" nonce="[redacted]">Hello<br>world &amp; all</p>
    """, HumanReadableSerializer.Serialize(content, options));
```
<!-- endref -->

The value is parsed by the HTML parser of `Meziantou.Framework.WebPlatform`, which follows the HTML specification, and
serialized back, so the output is normalized: attribute values are quoted, character references are decoded and
escaped again, and `<br/>` becomes `<br>`. Two HTML values that differ only by these details give the same text.

| Option | Description |
| --- | --- |
| [OrderAttributes](cref:Meziantou.Framework.HumanReadable.ValueFormatters.HtmlFormatterOptions.OrderAttributes) | Sorts the attributes of each element by namespace and name |
| [AttributeQuote](cref:Meziantou.Framework.HumanReadable.ValueFormatters.HtmlFormatterOptions.AttributeQuote) | [HtmlAttributeQuote.DoubleQuote](cref:Meziantou.Framework.HumanReadable.ValueFormatters.HtmlAttributeQuote.DoubleQuote) (the default) or [HtmlAttributeQuote.SimpleQuote](cref:Meziantou.Framework.HumanReadable.ValueFormatters.HtmlAttributeQuote.SimpleQuote) |
| [RedactContentSecurityPolicyNonce](cref:Meziantou.Framework.HumanReadable.ValueFormatters.HtmlFormatterOptions.RedactContentSecurityPolicyNonce) | Replaces the value of the `nonce` attributes with `[redacted]`, as a nonce changes on every response |

A value that contains a doctype or an `html`, `head` or `body` tag is parsed as a document, and keeps these elements:

<!-- ref:../../tests/Meziantou.Framework.HumanReadableSerializer.Html.Tests/HttpHtmlSerializerTests.cs;region=HtmlFormatter_SimpleQuote;format=md-fenced-code-block;dedent=true -->
```csharp
using var content = new StringContent("""<!DOCTYPE html><html lang=en><body><a href="/docs">Docs</a></body></html>""", encoding: null, "text/html");

var options = new HumanReadableSerializerOptions()
    .AddHtmlFormatter(new HtmlFormatterOptions { AttributeQuote = HtmlAttributeQuote.SimpleQuote });

// A document keeps its doctype, html, head and body elements
Assert.Equal("""
    Headers:
      Content-Type: text/html; charset=utf-8
    Value: <!DOCTYPE html><html lang='en'><head></head><body><a href='/docs'>Docs</a></body></html>
    """, HumanReadableSerializer.Serialize(content, options));
```
<!-- endref -->

Any other value is parsed as the content of a `body` element, and written without the elements the parser adds around
it. A value that cannot be formatted is written as it is.

## Other media types

`AddFormatter(mediaType, formatter)` registers a [ValueFormatter](cref:Meziantou.Framework.HumanReadable.ValueFormatters.ValueFormatter) for any media type. A formatter overrides
`Format(writer, value, options)` and writes the value with the [HumanReadableTextWriter](cref:Meziantou.Framework.HumanReadable.HumanReadableTextWriter). The [ValueFormatter](cref:Meziantou.Framework.HumanReadable.ValueFormatters.ValueFormatter)
constants name the media types the serializer recognizes: [JsonMediaTypeName](cref:Meziantou.Framework.HumanReadable.ValueFormatters.ValueFormatter.JsonMediaTypeName), [XmlMediaTypeName](cref:Meziantou.Framework.HumanReadable.ValueFormatters.ValueFormatter.XmlMediaTypeName),
[HtmlMediaTypeName](cref:Meziantou.Framework.HumanReadable.ValueFormatters.ValueFormatter.HtmlMediaTypeName), [WwwFormUrlEncodedMediaTypeName](cref:Meziantou.Framework.HumanReadable.ValueFormatters.ValueFormatter.WwwFormUrlEncodedMediaTypeName), [CssMediaTypeName](cref:Meziantou.Framework.HumanReadable.ValueFormatters.ValueFormatter.CssMediaTypeName) and [JavascriptMediaTypeName](cref:Meziantou.Framework.HumanReadable.ValueFormatters.ValueFormatter.JavascriptMediaTypeName).
