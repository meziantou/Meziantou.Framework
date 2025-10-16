using Meziantou.Framework.HumanReadable.ValueFormatters;

namespace Meziantou.Framework.HumanReadable.Tests;

public sealed class HttpXmlSerializerTests : SerializerTestsBase
{
    private static readonly HumanReadableSerializerOptions IndentedAndOrderedOptions = new HumanReadableSerializerOptions()
        .AddXmlFormatter(new XmlFormatterOptions
        {
            WriteIndented = true,
            OrderAttributes = true,
        });

    private static readonly HumanReadableSerializerOptions IndentedAndNonOrderedOptions = new HumanReadableSerializerOptions()
        .AddXmlFormatter(new XmlFormatterOptions
        {
            WriteIndented = true,
            OrderAttributes = false,
        });

    private static readonly HumanReadableSerializerOptions NonIndentedAndOrderedOptions = new HumanReadableSerializerOptions()
        .AddXmlFormatter(new XmlFormatterOptions
        {
            WriteIndented = false,
            OrderAttributes = true,
        });

    [Fact]
    public void Indented_Ordered_FormatXml_InvalidXml()
    {
        using var httpContent = new StringContent("""dummy""", encoding: null, "application/xml");

        AssertSerialization(httpContent, IndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/xml; charset=utf-8
            Value: dummy
            """);
    }

    [Fact]
    public void Indented_Ordered_FormatXml()
    {
        using var httpContent = new StringContent("""<root value="42" foo="bar"><subitem>Sample</subitem></root>""", encoding: null, "application/xml");

        AssertSerialization(httpContent, IndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/xml; charset=utf-8
            Value:
              <root foo="bar" value="42">
                <subitem>Sample</subitem>
              </root>
            """);
    }

    [Fact]
    public void NonIndented_Ordered_FormatXml()
    {
        using var httpContent = new StringContent("""<root value="42" foo="bar"><subitem>Sample</subitem></root>""", encoding: null, "application/xml");

        AssertSerialization(httpContent, NonIndentedAndOrderedOptions, """
            Headers:
              Content-Type: application/xml; charset=utf-8
            Value: <root foo="bar" value="42"><subitem>Sample</subitem></root>
            """);
    }

    [Fact]
    public void Indented_NonOrdered_FormatXml()
    {
        using var httpContent = new StringContent("""<root value="42" foo="bar"><subitem>Sample</subitem></root>""", encoding: null, "application/xml");

        AssertSerialization(httpContent, IndentedAndNonOrderedOptions, """
            Headers:
              Content-Type: application/xml; charset=utf-8
            Value:
              <root value="42" foo="bar">
                <subitem>Sample</subitem>
              </root>
            """);
    }
}
