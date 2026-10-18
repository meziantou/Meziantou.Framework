using System.Xml;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Xml;

public sealed class XmlParserTests
{
    private static string Serialize(Node node) => new XmlSerializer().SerializeToString(node);

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

    [Fact]
    public void EntityExpansion_IsLimited()
    {
        var xml = "<!DOCTYPE lolz [<!ENTITY lol 'lollollollollollollollollollol'>" +
            string.Concat(Enumerable.Range(1, 8).Select(i => $"<!ENTITY lol{i} '{string.Concat(Enumerable.Repeat(i == 1 ? "&lol;" : $"&lol{i - 1};", 10))}'>")) +
            "]><lolz>&lol8;</lolz>";
        Assert.Throws<XmlException>(() => XmlDocumentParser.ParseDocument(xml));
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
}
