using System.Text;
using System.Xml;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Xml;

public sealed class XmlParserTests
{
    private static string Serialize(Node node) => new XmlSerializer().SerializeToString(node);

    [Theory]
    [InlineData("utf-8", false)]
    [InlineData("utf-8", true)]
    [InlineData("utf-16", true)]
    [InlineData("utf-16BE", true)]
    [InlineData("utf-16BE", false)]
    [InlineData("utf-32", true)]
    [InlineData("iso-8859-1", false)]
    public async Task ByteAndStreamInputs_DetermineEncoding(string label, bool bom)
    {
        var encoding = Encoding.GetEncoding(label);
        var xml = $"<?xml version='1.0' encoding='{label}'?><root attr='caf\u00e9'>caf\u00e9</root>";
        var bytes = (bom ? encoding.GetPreamble() : []).Concat(encoding.GetBytes(xml)).ToArray();
        var expected = Serialize(XmlDocumentParser.ParseDocument(xml));
        Assert.Equal(expected, Serialize(XmlDocumentParser.ParseDocument(bytes)));
        using var stream = new MemoryStream(bytes);
        Assert.Equal(expected, Serialize(XmlDocumentParser.ParseDocument(stream)));
        Assert.True(stream.CanRead);
        stream.Position = 0;
        Assert.Equal(expected, Serialize(await XmlDocumentParser.ParseDocumentAsync(stream)));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public void ByteInputs_WithoutDeclaration(string label)
    {
        var encoding = Encoding.GetEncoding(label);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("<root>caf\u00e9</root>")).ToArray();
        Assert.Equal("caf\u00e9", XmlDocumentParser.ParseDocument(bytes).DocumentElement!.TextContent);
        Assert.Equal("caf\u00e9", XmlDocumentParser.ParseDocument("<root>caf\u00e9</root>"u8).DocumentElement!.TextContent);
    }

    [Theory]
    [InlineData("utf-8", "iso-8859-1")]
    [InlineData("utf-16", "utf-8")]
    [InlineData("utf-16BE", "utf-16LE")]
    public async Task ByteInputs_ConflictingDeclarationsThrow(string actual, string declared)
    {
        var encoding = Encoding.GetEncoding(actual);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes($"<?xml version='1.0' encoding='{declared}'?><root/>")).ToArray();
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument(bytes));
        using var stream = new MemoryStream(bytes);
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument(stream));
        stream.Position = 0;
        await Assert.ThrowsAsync<XmlException>(() => XmlDocumentParser.ParseDocumentAsync(stream));
        Assert.True(stream.CanRead);
    }

    [Theory]
    [InlineData("<?xml version='1.0' encoding='unknown-encoding'?><root/>")]
    [InlineData("<?xml version='1.0' encoding='utf-16'?><root/>")]
    [InlineData("<?xml version='1.0' encoding='utf-8' encoding='utf-8'?><root/>")]
    [InlineData("<root><unclosed></root>")]
    [InlineData("")]
    public void ByteInputs_InvalidXmlThrows(string xml)
    {
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument(Encoding.UTF8.GetBytes(xml)));
    }

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    public async Task ByteInputs_MalformedSequencesThrow(string label)
    {
        var encoding = Encoding.GetEncoding(label);
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("<root/>")).Append((byte)0xFF).ToArray();
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument(bytes));
        using var stream = new MemoryStream(bytes);
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument(stream));
        stream.Position = 0;
        await Assert.ThrowsAsync<XmlException>(() => XmlDocumentParser.ParseDocumentAsync(stream));
    }

    [Fact]
    public void ByteInputs_GenericUtf16PreservesBigEndian()
    {
        var encoding = Encoding.BigEndianUnicode;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("<?xml version='1.0' encoding='UTF-16'?><root>caf\u00e9</root>")).ToArray();
        Assert.Equal("caf\u00e9", XmlDocumentParser.ParseDocument(bytes).DocumentElement!.TextContent);
    }

    [Fact]
    public void StringInputs_DoNotDecodeDeclarations()
    {
        Assert.Equal("\u20ac", XmlDocumentParser.ParseDocument("<?xml version='1.0' encoding='iso-8859-1'?><root>\u20ac</root>").DocumentElement!.TextContent);
        Assert.Equal("\u20ac", XmlDocumentParser.ParseDocument("<?xml version='1.0' encoding='unknown-encoding'?><root>\u20ac</root>").DocumentElement!.TextContent);
    }

    [Theory]
    [InlineData("C0AF")]
    [InlineData("EDA080")]
    [InlineData("E282")]
    [InlineData("FF")]
    public void ByteInputs_InvalidUtf8IsNotReplaced(string hex)
    {
        var bytes = "<root>"u8.ToArray().Concat(Convert.FromHexString(hex)).Concat("</root>"u8.ToArray()).ToArray();
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument(bytes));
    }

    [Fact]
    public void ByteInputs_CustomElementsAreConstructed()
    {
        var registry = new CustomElementRegistry();
        HtmlElement? constructed = null;
        registry.Define("x-example", new CustomElementConstructor(element =>
        {
            constructed = element;
            return new TestCustomElement(element);
        }));
        var document = XmlDocumentParser.ParseDocument("<x-example xmlns='http://www.w3.org/1999/xhtml'/>"u8, new XmlParserOptions { CustomElementRegistry = registry });
        Assert.Same(document.DocumentElement, constructed);
        Assert.NotNull(((HtmlElement)document.DocumentElement!).CustomElement);
    }

    [Fact]
    public async Task StreamInputs_UseCurrentPositionAndPreserveOptions()
    {
        var registry = new CustomElementRegistry();
        var options = new XmlParserOptions { Url = "https://example.com/document.xml", ContentType = "application/xhtml+xml", CustomElementRegistry = registry };
        using var stream = new MemoryStream("skip<html xmlns='http://www.w3.org/1999/xhtml'><a href='next'>caf\u00e9</a><svg xmlns='http://www.w3.org/2000/svg'/></html>"u8.ToArray());
        stream.Position = 4;
        var document = await XmlDocumentParser.ParseDocumentAsync(stream, options);
        Assert.Equal(options.Url, document.Url);
        Assert.Equal(options.ContentType, document.ContentType);
        Assert.Same(registry, document.CustomElementRegistry);
        Assert.IsType<HtmlHtmlElement>(document.DocumentElement);
        var anchor = Assert.IsType<HtmlAnchorElement>(document.DocumentElement!.FirstElementChild);
        Assert.Equal("https://example.com/next", anchor.Href);
        Assert.Equal("caf\u00e9", anchor.TextContent);
        Assert.IsType<SvgSvgElement>(anchor.NextElementSibling);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task StreamInputs_Cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new CancellationStream(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => XmlDocumentParser.ParseDocumentAsync(stream, cancellationToken: cancellation.Token));
        Assert.True(stream.CanRead);
        using var memory = new MemoryStream("<root/>"u8.ToArray());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => XmlDocumentParser.ParseDocumentAsync(memory, cancellationToken: cancellation.Token));
        Assert.Equal(0, memory.Position);
    }

    [Fact]
    public void Namespaces()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns='urn:a' xmlns:b='urn:b'><b:child b:attr='1' attr='2'/><svg xmlns='http://www.w3.org/2000/svg'/></root>");
        var root = document.DocumentElement!;
        Assert.Equal("urn:a", root.NamespaceUri);
        var child = root.FirstElementChild!;
        Assert.Equal("urn:b", child.NamespaceUri);
        Assert.Equal("b", child.Prefix);
        Assert.Equal("1", child.GetAttributeNS("urn:b", "attr"));
        Assert.Equal("2", child.GetAttributeNS(null, "attr"));
        Assert.IsType<SvgSvgElement>(child.NextElementSibling);
        Assert.Equal("urn:b", root.GetAttributeNodeNS("http://www.w3.org/2000/xmlns/", "b")!.Value);
    }

    [Fact]
    public void NodesAndEntities()
    {
        var document = XmlDocumentParser.ParseDocument("<?xml version='1.0'?><!DOCTYPE root [<!ENTITY e 'expanded'>]><?pi data?><!--c--><root>&e;&amp;&#65;<![CDATA[<cdata>]]></root>");
        var doctype = document.Doctype!;
        Assert.Equal("root", doctype.Name);
        Assert.IsType<ProcessingInstruction>(doctype.NextSibling);
        Assert.IsType<Comment>(doctype.NextSibling!.NextSibling);
        var root = document.DocumentElement!;
        Assert.Equal("expanded&A", root.FirstChild!.TextContent);
        Assert.IsType<CDataSection>(root.LastChild);
        Assert.Equal("<cdata>", root.LastChild!.TextContent);
    }

    [Fact]
    public void XhtmlDoctype_DeclaresNamedCharacterReferences()
    {
        var document = XmlDocumentParser.ParseDocument("<!DOCTYPE html PUBLIC '-//W3C//DTD XHTML 1.0 Strict//EN' 'http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd'><html xmlns='http://www.w3.org/1999/xhtml'>&nbsp;&eacute;&AMP;&LT;&nvlt;</html>");
        Assert.Equal("" + (char)0xA0 + (char)0xE9 + "&<<" + (char)0x20D2, document.DocumentElement!.TextContent);
        Assert.Equal(Serialize(document), Serialize(XmlDocumentParser.ParseDocument(Encoding.UTF8.GetBytes(Serialize(document)))));
        var bytes = Encoding.Latin1.GetBytes("<?xml version='1.0' encoding='iso-8859-1'?><!DOCTYPE html PUBLIC '-//W3C//DTD XHTML 1.0 Strict//EN' 'unused'><html xmlns='http://www.w3.org/1999/xhtml'>&nbsp;&eacute;</html>");
        Assert.Equal("\u00a0\u00e9", XmlDocumentParser.ParseDocument(bytes).DocumentElement!.TextContent);
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument("<html>&nbsp;</html>"));
    }

    [Fact]
    public void ExternalEntities_AreNotLoaded()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "secret");
            var xml = $"<!DOCTYPE root [<!ENTITY ext SYSTEM '{new Uri(path).AbsoluteUri}'>]><root>&ext;</root>";
            var document = XmlDocumentParser.ParseDocument(xml);
            Assert.DoesNotContain("secret", document.DocumentElement!.TextContent, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EntityExpansion_IsLimited(bool bytes)
    {
        var xml = "<!DOCTYPE lolz [<!ENTITY lol 'lollollollollollollollollollol'>" +
            string.Concat(Enumerable.Range(1, 8).Select(i => $"<!ENTITY lol{i} '{string.Concat(Enumerable.Repeat(i == 1 ? "&lol;" : $"&lol{i - 1};", 10))}'>")) +
            "]><lolz>&lol8;</lolz>";
        Assert.Throws<XmlException>(() => bytes ? XmlDocumentParser.ParseDocument(Encoding.UTF8.GetBytes(xml)) : XmlDocumentParser.ParseDocument(xml));
    }

    [Fact]
    public void ByteInputs_ExternalEntitiesAreNotLoaded()
    {
        var document = XmlDocumentParser.ParseDocument("<!DOCTYPE root SYSTEM 'https://example.invalid/external.dtd' [<!ENTITY ext SYSTEM 'https://example.invalid/secret'>]><root>&ext;</root>"u8);
        Assert.Equal("", document.DocumentElement!.TextContent);
    }

    [Fact]
    public void Template_ContentsAreInTheTemplateContents()
    {
        var document = XmlDocumentParser.ParseDocument("<html xmlns='http://www.w3.org/1999/xhtml'><template><p>x</p></template></html>");
        var template = (HtmlTemplateElement)document.DocumentElement!.FirstChild!;
        Assert.Null(template.FirstChild);
        Assert.Equal("p", ((Element)template.Content.FirstChild!).LocalName);
        Assert.Equal("<html xmlns=\"http://www.w3.org/1999/xhtml\"><template><p>x</p></template></html>", Serialize(document));
    }

    [Fact]
    public void Template_InnerHtml_XmlDocument()
    {
        var document = XmlDocumentParser.ParseDocument("<template xmlns='http://www.w3.org/1999/xhtml'/>");
        var template = (HtmlTemplateElement)document.DocumentElement!;
        template.InnerHtml = "<div>hello</div>";
        Assert.Null(template.FirstChild);
        var div = Assert.IsType<HtmlDivElement>(Assert.Single(template.Content.ChildNodes));
        Assert.Equal("hello", div.TextContent);
        Assert.Same(template.Content.NodeDocument, div.NodeDocument);
        Assert.Equal("<div xmlns=\"http://www.w3.org/1999/xhtml\">hello</div>", template.InnerHtml);

        template.InnerHtml = "<p>a</p><template><b>b</b></template>";
        Assert.Equal(2u, template.Content.ChildNodes.Length);
        var nested = (HtmlTemplateElement)template.Content.LastChild!;
        Assert.Equal("b", nested.Content.TextContent);
        Assert.Equal("<p xmlns=\"http://www.w3.org/1999/xhtml\">a</p><template xmlns=\"http://www.w3.org/1999/xhtml\"><b>b</b></template>", template.InnerHtml);
    }

    [Fact]
    public void DomParser_ParseError()
    {
        var document = new DomParser().ParseFromString("<root><unclosed></root>", DomParserSupportedType.ApplicationXml);
        Assert.Equal("parsererror", document.DocumentElement!.LocalName);
        Assert.Equal("http://www.mozilla.org/newlayout/xml/parsererror.xml", document.DocumentElement.NamespaceUri);
        Assert.Single(document.ChildNodes);
    }

    [Fact]
    public void InnerHtml_XmlDocument()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns='urn:a' xmlns:p='urn:p'><child/></root>");
        var child = document.DocumentElement!.FirstElementChild!;
        child.InnerHtml = "<x/><p:y a='1'/>text";
        Assert.Equal("urn:a", child.FirstElementChild!.NamespaceUri);
        Assert.Equal("urn:p", child.FirstElementChild.NextElementSibling!.NamespaceUri);
        Assert.Equal("<x xmlns=\"urn:a\"/><p:y xmlns:p=\"urn:p\" a=\"1\"/>text", child.InnerHtml);
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => child.InnerHtml = "<unclosed>").Name);
    }

    [Theory]
    [InlineData("urn:x\U000000A0y")]
    [InlineData("urn:a&b<c>\"d'e")]
    [InlineData("urn:a\tb\nc\rd  e")]
    public void InnerHtml_XmlDocument_ContextNamespaceIsPreserved(string ns)
    {
        var document = new Document();
        var root = document.CreateElementNS(ns, "root");
        document.AppendChild(root);
        root.InnerHtml = "<child/>";
        Assert.Equal(ns, root.FirstElementChild!.NamespaceUri);
    }

    [Theory]
    [InlineData("<root/>", "<root/>")]
    [InlineData("<root xmlns='urn:a'><child/></root>", "<root xmlns=\"urn:a\"><child/></root>")]
    [InlineData("<a:root xmlns:a='urn:a'><a:child attr='&lt;&amp;&quot;&#9;'/></a:root>", "<a:root xmlns:a=\"urn:a\"><a:child attr=\"&lt;&amp;&quot;&#9;\"/></a:root>")]
    [InlineData("<root>a&lt;b&gt;c&amp;</root>", "<root>a&lt;b&gt;c&amp;</root>")]
    public void RoundTrip(string xml, string expected)
    {
        Assert.Equal(expected, Serialize(XmlDocumentParser.ParseDocument(xml)));
    }

    [Fact]
    public void Serialize_HtmlElements()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a<br>b</p>");
        Assert.Equal("<p xmlns=\"http://www.w3.org/1999/xhtml\">a<br />b</p>", Serialize(document.Body!.FirstChild!));
    }

    [Fact]
    public void Serialize_RequireWellFormed()
    {
        var document = XmlDocumentParser.ParseDocument("<root/>");
        document.DocumentElement!.AppendChild(document.CreateComment("a--b"));
        Assert.Equal("<root><!--a--b--></root>", Serialize(document.DocumentElement));
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => document.DocumentElement.InnerHtml).Name);
    }

    [Fact]
    public void Documentation_ParseXml()
    {
        #region WebPlatform_ParseXml
        var feed = XmlDocumentParser.ParseDocument("""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry><title>First post</title></entry>
              <entry><title>Second post</title></entry>
            </feed>
            """);

        var titles = feed.GetElementsByTagNameNS("http://www.w3.org/2005/Atom", "title").Select(title => title.TextContent);
        Assert.Equal(["First post", "Second post"], titles);

        // A document that is not well-formed throws
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument("<root><item></root>"));

        // DOMParser returns a parsererror document instead, as in a browser
        var invalid = new DomParser().ParseFromString("<root><item></root>", DomParserSupportedType.ApplicationXml);
        Assert.Equal("parsererror", invalid.DocumentElement!.LocalName);
        #endregion
    }

    [Fact]
    public async Task Documentation_ParseXmlBytes()
    {
        #region WebPlatform_ParseXmlBytes
        var bytes = Encoding.Latin1.GetBytes("<?xml version='1.0' encoding='iso-8859-1'?><root>caf\u00e9</root>");
        var document = XmlDocumentParser.ParseDocument(bytes);
        Assert.Equal("caf\u00e9", document.DocumentElement!.TextContent);

        using var stream = new MemoryStream(bytes);
        var streamed = await XmlDocumentParser.ParseDocumentAsync(stream, cancellationToken: CancellationToken.None);
        Assert.Equal("caf\u00e9", streamed.DocumentElement!.TextContent);
        Assert.True(stream.CanRead);
        #endregion
    }

    private sealed class TestCustomElement(HtmlElement element) : CustomElement(element)
    {
    }

    private sealed class CancellationStream(CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await cancellation.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
            return 0;
        }
    }
}
