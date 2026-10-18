using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.XPath;

/// <summary>Tests of the XPath data model exposed by <see cref="Node.CreateNavigator"/>.</summary>
public sealed class XPathNavigatorTests
{
    private const string ReferenceXml =
        "<?xml-stylesheet href='a.css'?>" +
        "<!--before-->" +
        "<root xmlns='urn:default' xmlns:p='urn:p' id='r' xml:lang='en'>" +
        "<a n='1' p:x='px'>text1<![CDATA[cdata]]>text2</a>" +
        "<!--c1-->" +
        "<p:b n='2'><c n='3' xml:lang='fr-CA'>deep</c><c n='4'/><?pi data?></p:b>" +
        "<d xmlns='' n='5'><e/>tail</d>" +
        "<a n='6'>  12.5 </a>" +
        "</root>" +
        "<!--after-->";

    public static TheoryData<string> ReferenceExpressions =>
    [
        "/",
        "/node()",
        "/*",
        "//node()",
        "//*",
        "//text()",
        "//comment()",
        "//processing-instruction()",
        "//processing-instruction('pi')",
        "//processing-instruction('none')",
        "//@*",
        "//@n",
        "//@p:x",
        "//@xml:lang",
        "//d:a",
        "//d:a[1]",
        "//d:a[last()]",
        "//d:a[@n='6']",
        "//p:b/d:c",
        "//p:b/*",
        "//p:*",
        "//d",
        "//d/e",
        "//*[local-name()='c']",
        "//d:c/..",
        "//d:c/ancestor::*",
        "//d:c/ancestor-or-self::*",
        "//d:c[1]/following-sibling::node()",
        "//d:c[2]/preceding-sibling::node()",
        "//d:c[1]/following::node()",
        "//d:c[1]/preceding::node()",
        "//d:c[1]/following::*[1]",
        "//d:c[2]/preceding::*[1]",
        "//d:c[2]/preceding-sibling::*[1]",
        "//d:c[1]/descendant-or-self::node()",
        "//d:c[1]/self::node()",
        "//d:c[1]/self::d:a",
        "/d:root/descendant::*",
        "//text()/parent::*",
        "//@n/..",
        "//@n/following::d:c",
        "//d:c | //d:a | //d",
        "(//d:c | //d:a)[2]",
        "//*[@n > 2]",
        "//*[not(@n)]",
        "//*[lang('en')]",
        "//*[lang('fr')]",
        "//*[lang('fr-CA')]",
        "//*[text()]",
        "//*[*]",
        "//*[count(*) = 2]",
        "//*[position() mod 2 = 1]",
        "//d:a[starts-with(., 'text')]",
        "//d:a[contains(., 'cdata')]",
        "/d:root/namespace::*",
        "//d/namespace::*",
        "//d:c/namespace::*",
        "count(//node())",
        "count(//text())",
        "count(//@*)",
        "count(/d:root/namespace::*)",
        "count(//namespace::*)",
        "string(/)",
        "string(//d:a)",
        "string(//d:a[1]/text())",
        "string(//p:b)",
        "string(//comment())",
        "string(//processing-instruction())",
        "string(//@p:x)",
        "string(/d:root/namespace::p)",
        "name(//@p:x)",
        "local-name(//@p:x)",
        "namespace-uri(//@p:x)",
        "name(//p:b)",
        "name(//d:c)",
        "local-name(//d:c)",
        "namespace-uri(//d:c)",
        "namespace-uri(//d)",
        "name(//processing-instruction())",
        "name(/d:root/namespace::p)",
        "name(//comment())",
        "name(/)",
        "sum(//@n)",
        "number(//d:a[2])",
        "number(//d:a[1])",
        "normalize-space(//d:a[2])",
        "string-length(//d:a[1])",
        "boolean(//d:z)",
        "boolean(//p:b)",
        "translate(//d:c[1], 'de', 'DE')",
        "substring-before(//d:a[1], 'cdata')",
        "substring-after(//d:a[1], 'cdata')",
        "concat(//d:c[1], '-', //@p:x)",
        "//d:c[1] = 'deep'",
        "//@n = 3",
        "//@n != 3",
        "//@n < //@n",
        "count(//d:c[1]/following::text())",
        "count(//*[1])",
        "count(//d:root/*[last()])",
    ];

    [Theory]
    [MemberData(nameof(ReferenceExpressions))]
    public void Xml_MatchesTheReferenceImplementation(string expression)
    {
        var document = XmlDocumentParser.ParseDocument(ReferenceXml);
        XPathDocument reference;
        using (var reader = XmlReader.Create(new StringReader(ReferenceXml)))
        {
            reference = new XPathDocument(reader, XmlSpace.Preserve);
        }

        Assert.Equal(Evaluate(reference.CreateNavigator(), expression), Evaluate(document.CreateNavigator(), expression));

        static string Evaluate(XPathNavigator navigator, string expression)
        {
            var namespaces = new XmlNamespaceManager(new NameTable());
            namespaces.AddNamespace("d", "urn:default");
            namespaces.AddNamespace("p", "urn:p");
            var result = navigator.Evaluate(expression, namespaces);
            return Format(result);
        }
    }

    private static string Format(object? result)
    {
        switch (result)
        {
            case XPathNodeIterator iterator:
            {
                // The order of the namespace nodes of an element is implementation-defined
                var lines = new List<string>();
                var namespaces = new List<string>();
                while (iterator.MoveNext())
                {
                    var current = iterator.Current!;
                    var line = new StringBuilder().Append(current.NodeType).Append(" {").Append(current.NamespaceURI).Append('}').Append(current.Name)
                        .Append(" prefix=").Append(current.Prefix).Append(" local=").Append(current.LocalName)
                        .Append(" empty=").Append(current.IsEmptyElement)
                        .Append(" lang=").Append(current.XmlLang)
                        .Append(" = '").Append(current.Value).Append('\'').ToString();
                    if (current.NodeType == XPathNodeType.Namespace)
                    {
                        namespaces.Add(line);
                        continue;
                    }

                    FlushNamespaces();
                    lines.Add(line);
                }

                FlushNamespaces();
                return string.Join('\n', lines);

                void FlushNamespaces()
                {
                    lines.AddRange(namespaces.Order(StringComparer.Ordinal));
                    namespaces.Clear();
                }
            }

            case double number:
                return number.ToString("R", CultureInfo.InvariantCulture);

            default:
                return Convert.ToString(result, CultureInfo.InvariantCulture) ?? "";
        }
    }

    [Fact]
    public void Xml_SelectMatchesTheReferenceImplementationForEveryNodeAndAxis()
    {
        // Every axis from every node (including attributes and namespace nodes)
        var document = XmlDocumentParser.ParseDocument(ReferenceXml);
        XPathDocument reference;
        using (var reader = XmlReader.Create(new StringReader(ReferenceXml)))
        {
            reference = new XPathDocument(reader, XmlSpace.Preserve);
        }

        string[] axes = ["ancestor", "ancestor-or-self", "attribute", "child", "descendant", "descendant-or-self", "following", "following-sibling", "namespace", "parent", "preceding", "preceding-sibling", "self"];
        var expected = Collect(reference.CreateNavigator()).Order(StringComparer.Ordinal).ToList();
        var actual = Collect(document.CreateNavigator()).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }

        List<string> Collect(XPathNavigator root)
        {
            var result = new List<string>();
            var all = root.Select("/ | //node() | //@* | //namespace::*");
            while (all.MoveNext())
            {
                var context = all.Current!;
                foreach (var axis in axes)
                {
                    result.Add(context.NodeType + " " + context.Name + " " + context.Value + " / " + axis + "::node()\n" + Format(context.Select(axis + "::node()")));
                }
            }

            return result;
        }
    }

    [Fact]
    public void HtmlDocument_NodeTypes()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><html><head></head><body><!--c-->text<p class=a>x</p></body></html>");
        var navigator = document.CreateNavigator();
        Assert.Equal(XPathNodeType.Root, navigator.NodeType);
        Assert.Same(document, navigator.UnderlyingObject);

        // The doctype is not part of the data model
        Assert.True(navigator.MoveToFirstChild());
        Assert.Equal(XPathNodeType.Element, navigator.NodeType);
        Assert.Equal("html", navigator.LocalName);
        Assert.Equal("http://www.w3.org/1999/xhtml", navigator.NamespaceURI);
        Assert.False(navigator.MoveToNext());
        Assert.False(navigator.MoveToPrevious());

        var body = document.Body!;
        navigator = body.CreateNavigator();
        Assert.True(navigator.MoveToFirstChild());
        Assert.Equal(XPathNodeType.Comment, navigator.NodeType);
        Assert.Equal("c", navigator.Value);
        Assert.True(navigator.MoveToNext());
        Assert.Equal(XPathNodeType.Text, navigator.NodeType);
        Assert.Equal("text", navigator.Value);
        Assert.True(navigator.MoveToNext());
        Assert.Equal(XPathNodeType.Element, navigator.NodeType);
        Assert.Equal("p", navigator.Name);
        Assert.False(navigator.IsEmptyElement);

        Assert.True(navigator.MoveToFirstAttribute());
        Assert.Equal(XPathNodeType.Attribute, navigator.NodeType);
        Assert.Equal("class", navigator.Name);
        Assert.Equal("a", navigator.Value);
        Assert.Same(body.QuerySelector("p")!.GetAttributeNode("class"), navigator.UnderlyingObject);
        Assert.False(navigator.MoveToNextAttribute());
        Assert.False(navigator.MoveToFirstChild());
        Assert.True(navigator.MoveToParent());
        Assert.Equal("p", navigator.Name);
        Assert.True(navigator.MoveToParent());
        Assert.Same(body, navigator.UnderlyingObject);
    }

    [Fact]
    public void AdjacentTextNodesFormASingleTextNode()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p></p>");
        var p = document.QuerySelector("p")!;
        var empty1 = p.AppendChild(document.CreateTextNode(""));
        var a = p.AppendChild(document.CreateTextNode("a"));
        var b = p.AppendChild(document.CreateTextNode("b"));
        p.AppendChild(document.CreateElement("br"));
        p.AppendChild(document.CreateTextNode(""));
        p.AppendChild(document.CreateTextNode(""));
        var c = p.AppendChild(document.CreateTextNode("c"));

        var navigator = p.CreateNavigator();
        Assert.True(navigator.MoveToFirstChild());
        Assert.Same(empty1, navigator.UnderlyingObject); // The first node of the run
        Assert.Equal("ab", navigator.Value);
        Assert.True(navigator.MoveToNext());
        Assert.Equal("br", navigator.Name);
        Assert.True(navigator.MoveToNext());
        Assert.Equal("c", navigator.Value);
        Assert.False(navigator.MoveToNext());
        Assert.True(navigator.MoveToPrevious());
        Assert.Equal("br", navigator.Name);
        Assert.True(navigator.MoveToPrevious());
        Assert.Same(empty1, navigator.UnderlyingObject);
        Assert.False(navigator.MoveToPrevious());

        // A navigator created on any text node of a run is positioned on the run
        navigator = b.CreateNavigator();
        Assert.Same(empty1, navigator.UnderlyingObject);
        Assert.True(navigator.IsSamePosition(a.CreateNavigator()));
        Assert.Equal(XmlNodeOrder.Same, navigator.ComparePosition(a.CreateNavigator()));
        Assert.Equal(XmlNodeOrder.Before, navigator.ComparePosition(c.CreateNavigator()));
    }

    [Fact]
    public void EmptyTextNodesAreNotInTheDataModel()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p></p>");
        var p = document.QuerySelector("p")!;
        p.AppendChild(document.CreateTextNode(""));
        p.AppendChild(document.CreateTextNode(""));

        var navigator = p.CreateNavigator();
        Assert.True(navigator.IsEmptyElement is false); // The DOM node has children
        Assert.False(navigator.MoveToFirstChild());
        Assert.Equal(0d, navigator.Evaluate("count(node())"));
    }

    [Fact]
    public void XmlnsAttributesAreNamespaceNodes()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns='urn:d' xmlns:a='urn:a' x='1'><child a:y='2'/></root>");
        var navigator = document.DocumentElement!.CreateNavigator();
        Assert.True(navigator.MoveToFirstAttribute());
        Assert.Equal("x", navigator.Name);
        Assert.False(navigator.MoveToNextAttribute());
        navigator.MoveToParent();

        Assert.Equal("urn:a", navigator.GetNamespace("a"));
        Assert.Equal("urn:d", navigator.GetNamespace(""));
        Assert.Equal("http://www.w3.org/XML/1998/namespace", navigator.GetNamespace("xml"));

        var local = new List<string>();
        if (navigator.MoveToFirstNamespace(XPathNamespaceScope.Local))
        {
            do
            {
                Assert.Equal(XPathNodeType.Namespace, navigator.NodeType);
                Assert.Null(navigator.UnderlyingObject);
                local.Add(navigator.LocalName + "=" + navigator.Value);
            }
            while (navigator.MoveToNextNamespace(XPathNamespaceScope.Local));

            navigator.MoveToParent();
        }

        Assert.Equal(["=urn:d", "a=urn:a"], local.Order(StringComparer.Ordinal));
        Assert.Equal("root", navigator.LocalName);
    }

    [Theory]
    [InlineData("<root xmlns:a='urn:a'><child/></root>", "")]
    [InlineData("<root xmlns:a='urn:a'><child xmlns:b='urn:b'/></root>", "b=urn:b")]
    [InlineData("<root xmlns:a='urn:a'><child xmlns:a='urn:other'/></root>", "a=urn:other")]
    [InlineData("<root xmlns:a='urn:a'><child xmlns:a='urn:a'/></root>", "")]
    [InlineData("<root xmlns='urn:d'><child xmlns='urn:e'/></root>", "=urn:e")]
    [InlineData("<root xmlns:a='urn:a'><a:child/></root>", "")]
    public void NamespaceScopeLocal_OnlyReturnsTheNamespacesDeclaredByTheElement(string xml, string expected)
    {
        var document = XmlDocumentParser.ParseDocument(xml);
        var navigator = document.DocumentElement!.FirstElementChild!.CreateNavigator();
        var local = new List<string>();
        if (navigator.MoveToFirstNamespace(XPathNamespaceScope.Local))
        {
            do
            {
                local.Add(navigator.LocalName + "=" + navigator.Value);
            }
            while (navigator.MoveToNextNamespace(XPathNamespaceScope.Local));
        }

        Assert.Equal(expected, string.Join(',', local.Order(StringComparer.Ordinal)));
    }

    [Fact]
    public void XsltIdentityTransform_MatchesTheReferenceImplementation()
    {
        var xslt = new System.Xml.Xsl.XslCompiledTransform();
        using (var reader = XmlReader.Create(new StringReader("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform">
              <xsl:template match="@*|node()"><xsl:copy><xsl:apply-templates select="@*|node()"/></xsl:copy></xsl:template>
            </xsl:stylesheet>
            """)))
        {
            xslt.Load(reader);
        }

        var document = XmlDocumentParser.ParseDocument(ReferenceXml);
        XPathDocument reference;
        using (var reader = XmlReader.Create(new StringReader(ReferenceXml)))
        {
            reference = new XPathDocument(reader, XmlSpace.Preserve);
        }

        Assert.Equal(Transform(reference.CreateNavigator()), Transform(document.CreateNavigator()));

        string Transform(XPathNavigator navigator)
        {
            using var writer = new StringWriter();
            xslt.Transform(navigator, null, writer);
            return writer.ToString();
        }
    }

    [Fact]
    public void NamespaceScopeLocal_ElementCreatedWithoutXmlnsAttribute()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns:a='urn:a'/>");
        var child = document.CreateElementNS("urn:b", "b:child");
        document.DocumentElement!.AppendChild(child);
        var navigator = child.CreateNavigator();
        Assert.True(navigator.MoveToFirstNamespace(XPathNamespaceScope.Local));
        Assert.Equal("b", navigator.LocalName);
        Assert.Equal("urn:b", navigator.Value);
        Assert.False(navigator.MoveToNextNamespace(XPathNamespaceScope.Local));
        Assert.Equal("<b:child xmlns:b=\"urn:b\" />", child.CreateNavigator().OuterXml);
    }

    [Fact]
    public void DocumentFragmentIsARootNode()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        var fragment = document.CreateDocumentFragment();
        fragment.Append(document.CreateElement("a"), "text", document.CreateElement("b"));
        var navigator = fragment.CreateNavigator();
        Assert.Equal(XPathNodeType.Root, navigator.NodeType);
        Assert.False(navigator.MoveToNext());
        Assert.False(navigator.MoveToParent());
        Assert.Equal("text", navigator.Value);
        Assert.Equal(3d, navigator.Evaluate("count(node())"));

        Assert.True(navigator.MoveToFirstChild());
        Assert.True(navigator.MoveToNext());
        Assert.True(navigator.MoveToNext());
        navigator.MoveToRoot();
        Assert.Same(fragment, navigator.UnderlyingObject);
    }

    [Fact]
    public void ProcessingInstructionAndComment()
    {
        var document = XmlDocumentParser.ParseDocument("<root><?target some data?><!--comment--></root>");
        var navigator = document.DocumentElement!.CreateNavigator();
        Assert.True(navigator.MoveToFirstChild());
        Assert.Equal(XPathNodeType.ProcessingInstruction, navigator.NodeType);
        Assert.Equal("target", navigator.Name);
        Assert.Equal("target", navigator.LocalName);
        Assert.Equal("some data", navigator.Value);
        Assert.True(navigator.MoveToNext());
        Assert.Equal(XPathNodeType.Comment, navigator.NodeType);
        Assert.Equal("", navigator.Name);
        Assert.Equal("comment", navigator.Value);
    }

    [Fact]
    public void AttributeNode()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns:p='urn:p' p:a='1' b='2'/>");
        var attribute = document.DocumentElement!.GetAttributeNode("p:a")!;
        var navigator = attribute.CreateNavigator();
        Assert.Equal(XPathNodeType.Attribute, navigator.NodeType);
        Assert.Equal("p:a", navigator.Name);
        Assert.Equal("a", navigator.LocalName);
        Assert.Equal("p", navigator.Prefix);
        Assert.Equal("urn:p", navigator.NamespaceURI);
        Assert.Equal("1", navigator.Value);
        Assert.Same(attribute, navigator.UnderlyingObject);
        Assert.True(navigator.MoveToNextAttribute());
        Assert.Equal("b", navigator.Name);
        Assert.Equal(XmlNodeOrder.After, navigator.ComparePosition(attribute.CreateNavigator()));
        Assert.Equal(XmlNodeOrder.Before, document.DocumentElement.CreateNavigator().ComparePosition(navigator));
    }

    [Fact]
    public void DetachedAttributeIsARootlessNode()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        var attribute = document.CreateAttribute("a");
        attribute.Value = "v";
        var navigator = attribute.CreateNavigator();
        Assert.Same(attribute, navigator.UnderlyingObject);
        Assert.Equal(XPathNodeType.Attribute, navigator.NodeType);
        Assert.Equal("a", navigator.Name);
        Assert.Equal("a", navigator.LocalName);
        Assert.Equal("", navigator.NamespaceURI);
        Assert.Equal("v", navigator.Value);
        Assert.False(navigator.MoveToFirstChild());
        Assert.False(navigator.MoveToParent());

        var namespaced = document.CreateAttributeNS("urn:p", "p:b");
        navigator = namespaced.CreateNavigator();
        Assert.Equal("p:b", navigator.Name);
        Assert.Equal("b", navigator.LocalName);
        Assert.Equal("p", navigator.Prefix);
        Assert.Equal("urn:p", navigator.NamespaceURI);
    }

    [Fact]
    public void ComparePosition_DifferentTrees()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p></p>");
        var detached = document.CreateElement("div");
        var other = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p></p>");
        Assert.Equal(XmlNodeOrder.Unknown, document.CreateNavigator().ComparePosition(detached.CreateNavigator()));
        Assert.Equal(XmlNodeOrder.Unknown, document.CreateNavigator().ComparePosition(other.CreateNavigator()));
        Assert.Equal(XmlNodeOrder.Unknown, document.CreateNavigator().ComparePosition(CreateReferenceNavigator("<a/>")));
        Assert.False(document.CreateNavigator().IsSamePosition(other.CreateNavigator()));
        Assert.False(document.CreateNavigator().MoveTo(CreateReferenceNavigator("<a/>")));
    }

    [Fact]
    public void MoveToId()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p id=a>1</p><p id=b>2</p><p id=a>3</p>");
        var navigator = document.CreateNavigator();
        Assert.True(navigator.MoveToId("a"));
        Assert.Equal("1", navigator.Value);
        Assert.True(navigator.MoveToId("b"));
        Assert.Equal("2", navigator.Value);
        Assert.False(navigator.MoveToId("c"));
        Assert.Equal("2", navigator.Value);
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p a=1>x</p>");
        var navigator = document.QuerySelector("p")!.CreateNavigator();
        var clone = navigator.Clone();
        Assert.True(clone.MoveToFirstAttribute());
        Assert.Equal(XPathNodeType.Element, navigator.NodeType);
        Assert.False(navigator.IsSamePosition(clone));
        Assert.True(navigator.MoveTo(clone));
        Assert.Equal(XPathNodeType.Attribute, navigator.NodeType);
        Assert.True(navigator.IsSamePosition(clone));
    }

    [Fact]
    public void Select_WithNamespaceManagerOnHtmlDocument()
    {
        // The navigator does not apply the HTML rewriting: HTML elements are in the HTML namespace
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li>a<li>b</ul><svg><circle r=1 /></svg>");
        var navigator = document.CreateNavigator();
        Assert.Equal(0, navigator.Select("//li").Count);

        var namespaces = new XmlNamespaceManager(navigator.NameTable);
        namespaces.AddNamespace("h", "http://www.w3.org/1999/xhtml");
        namespaces.AddNamespace("s", "http://www.w3.org/2000/svg");
        Assert.Equal(2, navigator.Select("//h:li", namespaces).Count);
        Assert.Equal("b", navigator.SelectSingleNode("//h:li[2]", namespaces)!.Value);
        Assert.Equal("1", navigator.SelectSingleNode("//s:circle/@r", namespaces)!.Value);
        Assert.True(navigator.Matches(System.Xml.XPath.XPathExpression.Compile("h:li", namespaces)) is false);
        Assert.True(navigator.SelectSingleNode("//h:li", namespaces)!.Matches(System.Xml.XPath.XPathExpression.Compile("h:ul/h:li", namespaces)));
    }

    [Fact]
    public void OuterXml_MatchesTheReferenceImplementation()
    {
        // A single namespace declaration: the order of the namespace nodes is implementation-defined
        const string Xml = "<?pi x?><root a='1' xmlns:p='urn:p'><p:c b='2'>t<![CDATA[u]]></p:c><!--x--><e/><d xml:lang='en'> </d></root>";
        var document = XmlDocumentParser.ParseDocument(Xml);
        var reference = CreateReferenceNavigator(Xml);
        Assert.Equal(reference.OuterXml, document.CreateNavigator().OuterXml);
        Assert.Equal(reference.InnerXml, document.CreateNavigator().InnerXml);
        reference.MoveToChild(XPathNodeType.Element);
        Assert.Equal(reference.OuterXml, document.DocumentElement!.CreateNavigator().OuterXml);
        Assert.Equal(reference.InnerXml, document.DocumentElement.CreateNavigator().InnerXml);
    }

    /// <summary>Creates a navigator of System.Xml (the reference implementation), without DTD processing.</summary>
    private static XPathNavigator CreateReferenceNavigator(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return new XPathDocument(reader, XmlSpace.Preserve).CreateNavigator();
    }
}
