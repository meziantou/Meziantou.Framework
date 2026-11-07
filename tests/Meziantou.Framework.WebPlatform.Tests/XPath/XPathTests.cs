using System.Xml.XPath;
using System.Xml.Xsl;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.XPath;

public sealed class XPathTests
{
    private static List<Node> Snapshot(Document document, string expression, Node? context = null, IXPathNSResolver? resolver = null)
    {
        var result = document.Evaluate(expression, context ?? document, resolver, XPathResult.OrderedNodeSnapshotType);
        return Enumerable.Range(0, (int)result.SnapshotLength).Select(i => result.SnapshotItem((uint)i)!).ToList();
    }

    [Fact]
    public void HtmlDocument_UsesTheHtmlNamespaceAndCaseInsensitiveNames()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=a title=x><span></span></div><svg><path refX=1 /></svg>");
        Assert.Single(Snapshot(document, "//DIV"));
        Assert.Empty(Snapshot(document, "//path"));
        Assert.Single(Snapshot(document, "//div[@ID='a']"));
        Assert.Single(Snapshot(document, "//*[@refX]"));
        Assert.Empty(Snapshot(document, "//*[@refx]"));
        Assert.Equal(2, Snapshot(document, "//div/span | //div").Count);
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => document.Evaluate("//svg:path", document)).Name);
    }

    [Fact]
    public void XmlDocument_NamesAreCaseSensitive()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns:p='urn:p'><Item/><p:item a='1'/></root>");
        Assert.Single(Snapshot(document, "//Item"));
        Assert.Empty(Snapshot(document, "//item"));
        Assert.Single(Snapshot(document, "//p:item", resolver: document.DocumentElement));
    }

    [Theory]
    [InlineData("1 + 1", 2)]
    [InlineData("count(//li)", 3)]
    [InlineData("sum(//li)", 6)]
    [InlineData("string-length('abc')", 3)]
    public void NumberResults(string expression, double expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li>1<li>2<li>3</ul>");
        var result = document.Evaluate(expression, document);
        Assert.Equal(XPathResult.NumberType, result.ResultType);
        Assert.Equal(expected, result.NumberValue);
    }

    [Fact]
    public void Conversions()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a<b>b</b>c</p>");
        Assert.Equal("abc", document.Evaluate("//p", document, type: XPathResult.StringType).StringValue);
        Assert.True(document.Evaluate("//p", document, type: XPathResult.BooleanType).BooleanValue);
        Assert.Equal("0.5", document.Evaluate("1 div 2", document, type: XPathResult.StringType).StringValue);
        Assert.Equal("100000000000000000000", document.Evaluate("100000000000000000000", document, type: XPathResult.StringType).StringValue);
        Assert.Throws<TypeErrorException>(() => document.Evaluate("1", document, type: XPathResult.OrderedNodeSnapshotType));
        Assert.Throws<TypeErrorException>(() => document.Evaluate("1", document).StringValue);

        // A path from a value that is not a node-set (found by fuzzing): the error is reported when the nodes are collected
        Assert.Throws<TypeErrorException>(() => document.Evaluate("0/p", document));
        Assert.Throws<TypeErrorException>(() => document.Evaluate("string(.)/p", document, type: XPathResult.OrderedNodeSnapshotType));
    }

    [Fact]
    public void Iterator_IsInvalidatedByMutations()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a</p><p>b</p>");
        var result = document.Evaluate("//p", document, type: XPathResult.OrderedNodeIteratorType);
        Assert.NotNull(result.IterateNext());
        document.Body!.Append("x");
        Assert.True(result.InvalidIteratorState);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => result.IterateNext()).Name);
    }

    [Fact]
    public void TextNodesAreMerged()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a</p>");
        var p = document.QuerySelector("p")!;
        p.AppendChild(document.CreateTextNode("b"));
        p.AppendChild(document.CreateTextNode(""));
        var texts = Snapshot(document, "//p/text()");
        Assert.Single(texts);
        Assert.Equal("ab", document.Evaluate("string(//p/text())", document).StringValue);
    }

    [Fact]
    public void Union_OfNodesOfDifferentTrees()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=target></div>");
        var target = document.GetElementById("target")!;
        var attribute = document.CreateAttribute("data-test");
        attribute.Value = "hello";

        var nodes = Snapshot(document, "(id('target') | .)", attribute);
        Assert.Equal(2, nodes.Count);
        Assert.Contains(target, nodes);
        Assert.Contains(attribute, nodes);
        Assert.Equal([attribute], Snapshot(document, "(. | .)", attribute));
        Assert.Equal("hello", document.Evaluate("string((. | .))", attribute, type: XPathResult.StringType).StringValue);
        foreach (var expression in (string[])["parent::*", "parent::node()", "ancestor::*", "ancestor-or-self::*", "following::*", "following::node()", "preceding::*", "/"])
        {
            Assert.Empty(Snapshot(document, expression, attribute));
        }

        Assert.Equal([attribute], Snapshot(document, "ancestor-or-self::node()", attribute));

        var parent = document.CreateElement("div");
        parent.SetAttribute("data-test", "val");
        parent.Append(document.CreateElement("span"), document.CreateElement("p"));
        Assert.Equal(5, Snapshot(document, "(id('target') | . | @* | ./*)", parent).Count);
    }

    [Fact]
    public void SyntaxError()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => document.CreateExpression("//[")).Name);
    }

    [Fact]
    public void Navigator_CanBeUsedWithXslt()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li>a<li>b</ul>");
        var xslt = new XslCompiledTransform();
        using (var reader = System.Xml.XmlReader.Create(new StringReader("""
            <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:h="http://www.w3.org/1999/xhtml">
              <xsl:output method="text"/>
              <xsl:template match="/"><xsl:for-each select="//h:li"><xsl:value-of select="."/>;</xsl:for-each></xsl:template>
            </xsl:stylesheet>
            """)))
        {
            xslt.Load(reader);
        }

        using var writer = new StringWriter();
        xslt.Transform(document.CreateNavigator(), null, writer);
        Assert.Equal("a;b;", writer.ToString());
    }

    private static string Ids(IEnumerable<Node> nodes) => string.Join(",", nodes.Select(node => node switch
    {
        Element element => element.GetAttribute("id") ?? element.LocalName,
        Attr attribute => "@" + attribute.Name,
        Text text => "'" + text.Data + "'",
        Comment comment => "<!--" + comment.Data + "-->",
        ProcessingInstruction pi => "<?" + pi.Target + "?>",
        Document => "#document",
        _ => node.NodeName,
    }));

    private const string AxesHtml = "<!DOCTYPE html><div id=r><p id=p1><b id=b1></b></p><p id=p2><b id=b2></b><i id=i2></i></p><p id=p3></p></div>";

    [Theory]
    [InlineData("//*[@id='p2']/child::*", "b2,i2")]
    [InlineData("//*[@id='p2']/*", "b2,i2")]
    [InlineData("//*[@id='r']/descendant::b", "b1,b2")]
    [InlineData("/html/body/div/descendant-or-self::*[@id][position() < 3]", "r,p1")]
    [InlineData("//*[@id='r']/descendant-or-self::*[position() < 3]", "r,p1")]
    [InlineData("//*[@id='b2']/parent::*", "p2")]
    [InlineData("//*[@id='b2']/..", "p2")]
    [InlineData("//*[@id='b2']/ancestor::*", "html,body,r,p2")]
    [InlineData("//*[@id='b2']/ancestor-or-self::*[@id]", "r,p2,b2")]
    [InlineData("//*[@id='p2']/following-sibling::*", "p3")]
    [InlineData("//*[@id='p2']/preceding-sibling::*", "p1")]
    [InlineData("//*[@id='b2']/following::*", "i2,p3")]
    [InlineData("//*[@id='b2']/preceding::*", "head,p1,b1")]
    [InlineData("//*[@id='b2']/self::*", "b2")]
    [InlineData("//*[@id='b2']/self::b", "b2")]
    [InlineData("//*[@id='b2']/self::i", "")]
    [InlineData("//*[@id='r']/attribute::*", "@id")]
    [InlineData("//p/@id", "@id,@id,@id")]
    [InlineData("//p[2]", "p2")]
    [InlineData("//p[last()]", "p3")]
    [InlineData("//p[position() > 1]", "p2,p3")]
    [InlineData("//p[b][i]", "p2")]
    [InlineData("//p[not(*)]", "p3")]
    [InlineData("//p[count(*) = 1]", "p1")]
    [InlineData("(//b)[2]", "b2")]
    [InlineData("(//b | //i)[last()]", "i2")]
    [InlineData("//p/*[1]", "b1,b2")]
    [InlineData("(//p/*)[1]", "b1")]
    [InlineData("//*[@id='p3']/preceding-sibling::*[1]", "p2")]
    [InlineData("//*[@id='p3']/preceding::*[1]", "i2")]
    [InlineData("//*[@id='p3']/ancestor::*[1]", "r")]
    [InlineData("//*[@id='p3']/ancestor::*[last()]", "html")]
    [InlineData("//i | //b | //p[1]", "p1,b1,b2,i2")]
    [InlineData("//*[starts-with(@id, 'b')]", "b1,b2")]
    [InlineData("//*[contains(@id, '2')]", "p2,b2,i2")]
    [InlineData("//*[@id = 'p1' or @id = 'p3']", "p1,p3")]
    [InlineData("//*[@id != 'r' and parent::*[@id = 'r']]", "p1,p2,p3")]
    [InlineData("//body//*[string-length(@id) = 1]", "r")]
    [InlineData("id('p1 i2')", "p1,i2")]
    [InlineData("id('missing')", "")]
    [InlineData("id(//*[@id='r']/@id)", "r")]
    public void Axes(string expression, string expected)
    {
        var document = HtmlDocumentParser.ParseDocument(AxesHtml);
        Assert.Equal(expected, Ids(Snapshot(document, expression)));
    }

    [Theory]
    [InlineData("b", "b2")]
    [InlineData("*", "b2,i2")]
    [InlineData(".", "p2")]
    [InlineData("..", "r")]
    [InlineData("../*", "p1,p2,p3")]
    [InlineData("/html", "html")]
    [InlineData("//b", "b1,b2")]
    [InlineData(".//b", "b2")]
    [InlineData("preceding::*", "head,p1,b1")]
    public void ElementContextNode(string expression, string expected)
    {
        var document = HtmlDocumentParser.ParseDocument(AxesHtml);
        var context = document.GetElementById("p2")!;
        Assert.Equal(expected, Ids(Snapshot(document, expression, context)));
    }

    [Fact]
    public void AttributeContextNode()
    {
        var document = HtmlDocumentParser.ParseDocument(AxesHtml);
        var attribute = document.GetElementById("p2")!.GetAttributeNode("id")!;
        Assert.Equal("@id", Ids(Snapshot(document, ".", attribute)));
        Assert.Equal("@id", Ids(Snapshot(document, "self::node()", attribute)));
        Assert.Equal("p2", Ids(Snapshot(document, "..", attribute)));
        Assert.Equal("", Ids(Snapshot(document, "*", attribute)));
        Assert.Equal("", Ids(Snapshot(document, "following-sibling::node()", attribute)));
        Assert.Equal("b2,i2,p3", Ids(Snapshot(document, "following::*", attribute)));
        Assert.Equal("html,body,r,p2", Ids(Snapshot(document, "ancestor::*", attribute)));
        Assert.Equal("p2", document.Evaluate("string(.)", attribute).StringValue);
    }

    [Fact]
    public void DetachedAttributeContextNode()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        var attribute = document.CreateAttribute("a");
        attribute.Value = "v";
        Assert.Equal("@a", Ids(Snapshot(document, ".", attribute)));
        Assert.Equal("@a", Ids(Snapshot(document, "self::node()", attribute)));
        Assert.Equal("", Ids(Snapshot(document, "..", attribute)));
        Assert.Equal("v", document.Evaluate("string(.)", attribute).StringValue);
        Assert.Equal("a", document.Evaluate("name(.)", attribute).StringValue);
    }

    [Fact]
    public void TextContextNode()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p id=a>x<b>y</b>z</p>");
        var x = (Text)document.GetElementById("a")!.FirstChild!;
        Assert.Equal("'x'", Ids(Snapshot(document, ".", x)));
        Assert.Equal("a", Ids(Snapshot(document, "..", x)));
        Assert.Equal("b,'z'", Ids(Snapshot(document, "following-sibling::node()", x)));
        Assert.Equal("'y','z'", Ids(Snapshot(document, "following::text()", x)));
    }

    [Fact]
    public void DetachedSubtreeContextNode()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><span id=attached></span>");
        var div = document.CreateElement("div");
        div.InnerHtml = "<span id=s1></span><p><span id=s2></span></p>";
        Assert.Equal("s1,s2", Ids(Snapshot(document, ".//span", div)));
        Assert.Equal("s1,s2", Ids(Snapshot(document, "descendant::span", div)));
        Assert.Equal("s1,s2", Ids(Snapshot(document, "//span", div)));
        Assert.Equal("", Ids(Snapshot(document, "..", div)));
        Assert.Equal(2d, document.Evaluate("count(//span)", div.QuerySelector("p")!).NumberValue);
    }

    [Fact]
    public void DocumentFragmentContextNode()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><span id=attached></span>");
        var fragment = document.CreateDocumentFragment();
        var p = document.CreateElement("p");
        p.SetAttribute("id", "p");
        p.AppendChild(document.CreateElement("span"));
        fragment.Append(p, "text");
        Assert.Equal("p,'text'", Ids(Snapshot(document, "node()", fragment)));
        Assert.Equal("span", Ids(Snapshot(document, "//span", fragment)));
        Assert.Equal("span", Ids(Snapshot(document, "/p/span", p)));
        Assert.Equal(XPathResult.StringType, document.Evaluate("string(.)", fragment).ResultType);
        Assert.Equal("text", document.Evaluate("string(.)", fragment).StringValue);
    }

    [Fact]
    public void ShadowTreesAreNotTraversed()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=host><span id=light></span></div>");
        var shadow = document.GetElementById("host")!.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.InnerHtml = "<span id=shadow></span>";
        Assert.Equal("light", Ids(Snapshot(document, "//span")));
        Assert.Equal("shadow", Ids(Snapshot(document, "//span", shadow)));
        Assert.Equal("shadow", Ids(Snapshot(document, "span", shadow)));
    }

    [Fact]
    public void NodeTypeTests()
    {
        var document = XmlDocumentParser.ParseDocument("<?pi1 a?><!--c0--><root>t1<![CDATA[t2]]>t3<?pi2 b?><!--c1--><e/>t4</root>");
        Assert.Equal("<?pi1?>,<!--c0-->,root", Ids(Snapshot(document, "/node()")));
        Assert.Equal("'t1','t4'", Ids(Snapshot(document, "//text()")));
        Assert.Equal("<!--c0-->,<!--c1-->", Ids(Snapshot(document, "//comment()")));
        Assert.Equal("<?pi1?>,<?pi2?>", Ids(Snapshot(document, "//processing-instruction()")));
        Assert.Equal("<?pi2?>", Ids(Snapshot(document, "//processing-instruction('pi2')")));
        Assert.Equal("'t1',<?pi2?>,<!--c1-->,e,'t4'", Ids(Snapshot(document, "/root/node()")));
        Assert.Equal("t1t2t3", document.Evaluate("string(/root/text()[1])", document).StringValue);
        Assert.Equal(2d, document.Evaluate("count(/root/text())", document).NumberValue);
        Assert.Equal("b", document.Evaluate("string(//processing-instruction('pi2'))", document).StringValue);
        Assert.Equal("pi2", document.Evaluate("name(//processing-instruction('pi2'))", document).StringValue);
    }

    [Fact]
    public void DoctypeIsNotInTheDataModel()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><!--c--><html></html>");
        Assert.Equal("<!--c-->,html", Ids(Snapshot(document, "/node()")));
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => document.Evaluate(".", document.Doctype!)).Name);
    }

    [Theory]
    [InlineData("string(//p)", "Hello World")]
    [InlineData("normalize-space('  a  b  ')", "a b")]
    [InlineData("concat('a', 'b', 'c')", "abc")]
    [InlineData("substring('12345', 2, 3)", "234")]
    [InlineData("substring('12345', 1.5, 2.6)", "234")]
    [InlineData("substring('12345', 0, 3)", "12")]
    [InlineData("substring('12345', 0 div 0, 3)", "")]
    [InlineData("substring-before('1999/04/01', '/')", "1999")]
    [InlineData("substring-after('1999/04/01', '/')", "04/01")]
    [InlineData("translate('bar', 'abc', 'ABC')", "BAr")]
    [InlineData("translate('--aaa--', 'abc-', 'ABC')", "AAA")]
    [InlineData("local-name(//p)", "p")]
    [InlineData("namespace-uri(//p)", "http://www.w3.org/1999/xhtml")]
    [InlineData("local-name(//*[@viewBox])", "svg")]
    [InlineData("namespace-uri(//*[@viewBox])", "http://www.w3.org/2000/svg")]
    [InlineData("local-name(//@viewBox)", "viewBox")]
    [InlineData("local-name(//*[@xlink:href])", "use")]
    [InlineData("string(//@xlink:href)", "#a")]
    [InlineData("namespace-uri(//@xlink:href)", "http://www.w3.org/1999/xlink")]
    [InlineData("name(//@xlink:href)", "xlink:href")]
    [InlineData("string(1 div 3)", "0.3333333333333333")]
    [InlineData("string(-0.5)", "-0.5")]
    [InlineData("string(1 = 1)", "true")]
    [InlineData("string(//missing)", "")]
    [InlineData("string(//li)", "1")]
    public void StringFunctions(string expression, string expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>Hello <b>World</b></p><ul><li>1</li><li>2</li></ul><svg viewBox='0 0 1 1'><use xlink:href='#a'/></svg>");
        var result = document.Evaluate(expression, document, new Resolver(("xlink", "http://www.w3.org/1999/xlink")));
        Assert.Equal(XPathResult.StringType, result.ResultType);
        Assert.Equal(expected, result.StringValue);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("7 mod 3", 1)]
    [InlineData("-7 mod 3", -1)]
    [InlineData("7 div 2", 3.5)]
    [InlineData("- - 1", 1)]
    [InlineData("floor(1.5)", 1)]
    [InlineData("floor(-1.5)", -2)]
    [InlineData("ceiling(1.2)", 2)]
    [InlineData("round(2.5)", 3)]
    [InlineData("round(-2.5)", -2)]
    [InlineData("number('  42  ')", 42)]
    [InlineData("number(true())", 1)]
    [InlineData("count(//li)", 2)]
    [InlineData("sum(//li)", 30)]
    [InlineData("number(//li[2])", 20)]
    [InlineData("count(//li[. > 15])", 1)]
    [InlineData("string-length()", 4)]
    [InlineData("last()", 1)]
    [InlineData("position()", 1)]
    public void NumberFunctions(string expression, double expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li>10</li><li>20</li></ul>");
        var result = document.Evaluate(expression, document);
        Assert.Equal(XPathResult.NumberType, result.ResultType);
        Assert.Equal(expected, result.NumberValue);
    }

    [Theory]
    [InlineData("0 div 0", double.NaN)]
    [InlineData("1 div 0", double.PositiveInfinity)]
    [InlineData("-1 div 0", double.NegativeInfinity)]
    [InlineData("number('abc')", double.NaN)]
    [InlineData("sum(//li) + number('x')", double.NaN)]
    public void NumberFunctions_SpecialValues(string expression, double expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li>10</li></ul>");
        Assert.Equal(expected, document.Evaluate(expression, document).NumberValue);
    }

    [Theory]
    [InlineData("true()", true)]
    [InlineData("false()", false)]
    [InlineData("not(false())", true)]
    [InlineData("boolean(//li)", true)]
    [InlineData("boolean(//missing)", false)]
    [InlineData("boolean('')", false)]
    [InlineData("boolean('false')", true)]
    [InlineData("boolean(0)", false)]
    [InlineData("boolean(0 div 0)", false)]
    [InlineData("boolean(-1)", true)]
    [InlineData("1 < 2", true)]
    [InlineData("2 <= 1", false)]
    [InlineData("'2' > 1", true)]
    [InlineData("1 = 1.0", true)]
    [InlineData("'a' = 'a'", true)]
    [InlineData("'a' != 'b'", true)]
    [InlineData("//li = '20'", true)]
    [InlineData("//li = '30'", false)]
    [InlineData("//li != '10'", true)]
    [InlineData("//li = 20", true)]
    [InlineData("//li > 15", true)]
    [InlineData("//li = //li", true)]
    [InlineData("//li = //missing", false)]
    [InlineData("//missing != 'x'", false)]
    [InlineData("//li = true()", true)]
    [InlineData("//missing = false()", true)]
    [InlineData("true() and false()", false)]
    [InlineData("true() or false()", true)]
    [InlineData("lang('en')", false)]
    [InlineData("starts-with('abc', 'ab')", true)]
    [InlineData("contains('abc', 'd')", false)]
    public void BooleanFunctions(string expression, bool expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li>10</li><li>20</li></ul>");
        var result = document.Evaluate(expression, document);
        Assert.Equal(XPathResult.BooleanType, result.ResultType);
        Assert.Equal(expected, result.BooleanValue);
    }

    [Theory]
    [InlineData("//*[lang('en')]", "html,a,b")]
    [InlineData("//*[lang('EN')]", "html,a,b")]
    [InlineData("//*[lang('en-US')]", "b")]
    [InlineData("//*[lang('fr')]", "c")]
    [InlineData("//*[lang('de')]", "")]
    public void Lang(string expression, string expected)
    {
        var document = XmlDocumentParser.ParseDocument("<html xml:lang='en'><a id='a'><b id='b' xml:lang='en-US'/><c id='c' xml:lang='fr'/></a></html>");
        Assert.Equal(expected, Ids(Snapshot(document, expression)));
    }

    [Fact]
    public void HtmlDocument_ElementNameTestsOnlyMatchHtmlElements()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=html></div><svg id=svg><a id=svga></a></svg><math id=math></math>");
        var body = document.Body!;
        var nullNs = document.CreateElementNS(null, "div");
        nullNs.SetAttribute("id", "nullns");
        body.AppendChild(nullNs);
        var upper = document.CreateElementNS("http://www.w3.org/1999/xhtml", "DIV");
        upper.SetAttribute("id", "upper");
        body.AppendChild(upper);

        Assert.Equal("html", Ids(Snapshot(document, "//div")));
        Assert.Equal("html", Ids(Snapshot(document, "//DiV")));
        Assert.Equal("", Ids(Snapshot(document, "//svg")));
        Assert.Equal("", Ids(Snapshot(document, "//math")));
        Assert.Equal("svg,svga,math,nullns,upper", Ids(Snapshot(document, "//body//*[not(self::div)]")));

        var resolver = new Resolver(("s", "http://www.w3.org/2000/svg"), ("m", "http://www.w3.org/1998/Math/MathML"), ("h", "http://www.w3.org/1999/xhtml"));
        Assert.Equal("svg", Ids(Snapshot(document, "//s:svg", resolver: resolver)));
        Assert.Equal("svga", Ids(Snapshot(document, "//s:a", resolver: resolver)));
        Assert.Equal("", Ids(Snapshot(document, "//a")));
        Assert.Equal("math", Ids(Snapshot(document, "//m:math", resolver: resolver)));
        Assert.Equal("html", Ids(Snapshot(document, "//h:div", resolver: resolver)));
        Assert.Equal("upper", Ids(Snapshot(document, "//h:DIV", resolver: resolver)));
        Assert.Equal("svg,svga", Ids(Snapshot(document, "//s:*", resolver: resolver)));
        Assert.Equal("nullns", Ids(Snapshot(document, "//*[local-name() = 'div' and namespace-uri() = '']")));
    }

    [Fact]
    public void HtmlDocument_AttributeNameTests()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=d DATA-X=1 title=t></div><svg id=s viewBox='0 0 1 1'><foreignObject id=f></foreignObject></svg>");
        var div = document.GetElementById("d")!;
        div.SetAttributeNS("urn:ns", "ns:title", "namespaced");

        Assert.Equal("d", Ids(Snapshot(document, "//*[@data-x]")));
        Assert.Equal("d", Ids(Snapshot(document, "//*[@DATA-X]")));
        Assert.Equal("d", Ids(Snapshot(document, "//*[@Data-X='1']")));
        Assert.Equal("t", document.Evaluate("string(//div/@TITLE)", document).StringValue);
        Assert.Equal("s", Ids(Snapshot(document, "//*[@viewBox]")));
        Assert.Equal("", Ids(Snapshot(document, "//*[@viewbox]")));
        Assert.Equal("", Ids(Snapshot(document, "//*[@VIEWBOX]")));
        Assert.Equal("s", Ids(Snapshot(document, "//*[attribute::viewBox]")));
        Assert.Equal(4d, document.Evaluate("count(//div/@*)", document).NumberValue);

        // Unprefixed attribute name tests only match attributes in no namespace
        Assert.Equal("@title", Ids(Snapshot(document, "//div/@Title")));
        Assert.Equal("@ns:title", Ids(Snapshot(document, "//div/@n:title", resolver: new Resolver(("n", "urn:ns")))));
    }

    [Theory]
    [InlineData("_html-default-element-ns")]
    [InlineData("_html-xpath-fn")]
    public void Resolver_CanUseFormerHtmlHelperPrefixesInHtml(string prefix)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        var item = document.CreateElementNS("urn:test", prefix + ":item");
        document.Body!.AppendChild(item);

        Assert.Same(item, Assert.Single(Snapshot(document, "//" + prefix + ":item", resolver: new Resolver((prefix, "urn:test")))));
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => Snapshot(document, "//" + prefix + ":item")).Name);
    }

    [Theory]
    [InlineData("_html-default-element-ns")]
    [InlineData("_html-xpath-fn")]
    public void Resolver_CanUseFormerHtmlHelperPrefixesInXml(string prefix)
    {
        var document = XmlDocumentParser.ParseDocument($"<root xmlns:{prefix}='urn:test'><{prefix}:item/></root>");

        Assert.Single(Snapshot(document, "//" + prefix + ":item", resolver: new Resolver((prefix, "urn:test"))));
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => Snapshot(document, "//" + prefix + ":item")).Name);
    }

    [Fact]
    public void XmlDocument_NoRewriting()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns='http://www.w3.org/1999/xhtml'><DIV Id='a'/></root>");
        Assert.Empty(Snapshot(document, "//DIV"));
        Assert.Single(Snapshot(document, "//h:DIV", resolver: new Resolver(("h", "http://www.w3.org/1999/xhtml"))));
        Assert.Empty(Snapshot(document, "//h:div", resolver: new Resolver(("h", "http://www.w3.org/1999/xhtml"))));
        Assert.Single(Snapshot(document, "//*[@Id]"));
        Assert.Empty(Snapshot(document, "//*[@id]"));
    }

    [Fact]
    public void XmlDocument_NamespaceAxisAndXmlPrefix()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns:a='urn:a' xml:lang='en' b='1'/>");
        Assert.Equal(2d, document.Evaluate("count(/root/namespace::*)", document).NumberValue); // a and xml
        Assert.Equal("urn:a", document.Evaluate("string(/root/namespace::a)", document).StringValue);
        Assert.Equal("en", document.Evaluate("string(/root/@xml:lang)", document).StringValue);
        Assert.Equal(2d, document.Evaluate("count(/root/@*)", document).NumberValue); // xmlns attributes are not attributes

        // Namespace nodes are not DOM nodes
        Assert.Equal(0u, document.Evaluate("/root/namespace::*", document, type: XPathResult.OrderedNodeSnapshotType).SnapshotLength);
        Assert.Equal("@b", Ids(Snapshot(document, "/root/namespace::* | /root/@b")));
    }

    [Fact]
    public void Resolver_CustomImplementation()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns:p='urn:p'><p:item/><p:item/></root>");
        var resolver = new Resolver(("q", "urn:p"));
        Assert.Equal(2, Snapshot(document, "//q:item", resolver: resolver).Count);
        Assert.Equal(new string?[] { "q" }, resolver.Lookups.Distinct());
    }

    [Fact]
    public void Resolver_NodeResolver()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns:p='urn:p'><child xmlns:c='urn:c'><c:x/></child><p:item/></root>");
        var child = document.DocumentElement!.FirstElementChild!;
        Assert.Same(child, document.CreateNSResolver(child));
        Assert.Same(child, new XPathEvaluator().CreateNSResolver(child));
        Assert.Single(Snapshot(document, "//c:x", resolver: child));
        Assert.Single(Snapshot(document, "//p:item", resolver: child));
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => document.Evaluate("//c:x", document, document.DocumentElement)).Name);
    }

    [Fact]
    public void Resolver_UnresolvedPrefix()
    {
        var document = XmlDocumentParser.ParseDocument("<root/>");
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => document.Evaluate("//p:item", document)).Name);
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => document.Evaluate("//p:item", document, new Resolver())).Name);
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => document.Evaluate("//@p:a", document, new Resolver())).Name);
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => document.Evaluate("//p:*", document, new Resolver())).Name);
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => document.Evaluate("//p:item", document, new ThrowingResolver())).Name);

        // The expression can be created: the prefixes are resolved on evaluation
        var expression = document.CreateExpression("//p:item");
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => expression.Evaluate(document)).Name);
    }

    [Fact]
    public void Resolver_ResolvesPrefixesOnEachEvaluation()
    {
        var document = XmlDocumentParser.ParseDocument("<root xmlns:a='urn:a' xmlns:b='urn:b'><a:x/><b:x/><b:x/></root>");
        var resolver = new Resolver(("p", "urn:a"));
        var expression = document.CreateExpression("count(//p:x)", resolver);
        Assert.Equal(1d, expression.Evaluate(document).NumberValue);
        resolver.Namespaces["p"] = "urn:b";
        Assert.Equal(2d, expression.Evaluate(document).NumberValue);
    }

    [Theory]
    [InlineData("//[")]
    [InlineData("")]
    [InlineData("1 +")]
    [InlineData("//a[")]
    [InlineData("//a]")]
    [InlineData("'unterminated")]
    [InlineData("//a::b")]
    [InlineData("unknown::a")]
    [InlineData("@")]
    [InlineData("1 2")]
    public void SyntaxErrors(string expression)
    {
        var html = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        var xml = XmlDocumentParser.ParseDocument("<root/>");
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => html.CreateExpression(expression)).Name);
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => html.Evaluate(expression, html)).Name);
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => xml.Evaluate(expression, xml)).Name);
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => new XPathEvaluator().CreateExpression(expression)).Name);
    }

    [Fact]
    public void UnknownFunction()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => document.Evaluate("unknown()", document)).Name);
        Assert.Equal("SyntaxError", Assert.Throws<DomException>(() => document.Evaluate("p:unknown()", document, new Resolver(("p", "urn:p")))).Name);
    }

    [Fact]
    public void Variables_AreNotSupported()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        Assert.Equal("NamespaceError", Assert.Throws<DomException>(() => document.Evaluate("$x", document)).Name);
    }

    [Fact]
    public void ArgumentValidation()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        Assert.Throws<ArgumentNullException>(() => document.Evaluate(null!, document));
        Assert.Throws<ArgumentNullException>(() => document.Evaluate("/", null!));
        Assert.Throws<ArgumentNullException>(() => document.CreateExpression(null!));
        Assert.Throws<ArgumentNullException>(() => document.CreateExpression("/").Evaluate(null!));
        Assert.Throws<ArgumentNullException>(() => document.CreateNSResolver(null!));
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => document.Evaluate("/", document, type: 10)).Name);
        Assert.Equal("NotSupportedError", Assert.Throws<DomException>(() => document.Evaluate("/", document, type: ushort.MaxValue)).Name);
    }

    [Fact]
    public void AnyType_ResultTypes()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a</p><p>b</p>");
        Assert.Equal(XPathResult.NumberType, document.Evaluate("1", document).ResultType);
        Assert.Equal(XPathResult.StringType, document.Evaluate("'1'", document).ResultType);
        Assert.Equal(XPathResult.BooleanType, document.Evaluate("true()", document).ResultType);

        var nodes = document.Evaluate("//p", document);
        Assert.Equal(XPathResult.UnorderedNodeIteratorType, nodes.ResultType);
        Assert.Equal("a", nodes.IterateNext()!.TextContent);
        Assert.Equal("b", nodes.IterateNext()!.TextContent);
        Assert.Null(nodes.IterateNext());
        Assert.Null(nodes.IterateNext());
    }

    [Theory]
    [InlineData(XPathResult.UnorderedNodeIteratorType)]
    [InlineData(XPathResult.OrderedNodeIteratorType)]
    [InlineData(XPathResult.UnorderedNodeSnapshotType)]
    [InlineData(XPathResult.OrderedNodeSnapshotType)]
    [InlineData(XPathResult.AnyUnorderedNodeType)]
    [InlineData(XPathResult.FirstOrderedNodeType)]
    public void NodeSetResultTypes(ushort type)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p id=a><b id=b></b></p><i id=c></i>");
        var result = document.Evaluate("//i | //b | //p", document, type: type);
        Assert.Equal(type, result.ResultType);
        Assert.Throws<TypeErrorException>(() => result.NumberValue);
        Assert.Throws<TypeErrorException>(() => result.StringValue);
        Assert.Throws<TypeErrorException>(() => result.BooleanValue);

        var isIterator = type is XPathResult.UnorderedNodeIteratorType or XPathResult.OrderedNodeIteratorType;
        var isSnapshot = type is XPathResult.UnorderedNodeSnapshotType or XPathResult.OrderedNodeSnapshotType;
        var isSingle = type is XPathResult.AnyUnorderedNodeType or XPathResult.FirstOrderedNodeType;

        if (isIterator)
        {
            Assert.Equal("a", ((Element)result.IterateNext()!).Id);
            Assert.Equal("b", ((Element)result.IterateNext()!).Id);
            Assert.Equal("c", ((Element)result.IterateNext()!).Id);
            Assert.Null(result.IterateNext());
            Assert.False(result.InvalidIteratorState);
        }
        else
        {
            Assert.Throws<TypeErrorException>(() => result.IterateNext());
            Assert.False(result.InvalidIteratorState);
        }

        if (isSnapshot)
        {
            Assert.Equal(3u, result.SnapshotLength);
            Assert.Equal("a", ((Element)result.SnapshotItem(0)!).Id);
            Assert.Equal("b", ((Element)result.SnapshotItem(1)!).Id);
            Assert.Equal("c", ((Element)result.SnapshotItem(2)!).Id);
            Assert.Null(result.SnapshotItem(3));
            Assert.Null(result.SnapshotItem(uint.MaxValue));
        }
        else
        {
            Assert.Throws<TypeErrorException>(() => result.SnapshotLength);
            Assert.Throws<TypeErrorException>(() => result.SnapshotItem(0));
        }

        if (isSingle)
        {
            Assert.Equal("a", ((Element)result.SingleNodeValue!).Id);
        }
        else
        {
            Assert.Throws<TypeErrorException>(() => result.SingleNodeValue);
        }
    }

    [Theory]
    [InlineData(XPathResult.UnorderedNodeIteratorType)]
    [InlineData(XPathResult.OrderedNodeIteratorType)]
    [InlineData(XPathResult.UnorderedNodeSnapshotType)]
    [InlineData(XPathResult.OrderedNodeSnapshotType)]
    [InlineData(XPathResult.AnyUnorderedNodeType)]
    [InlineData(XPathResult.FirstOrderedNodeType)]
    public void NodeSetResultTypes_EmptyNodeSet(ushort type)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        var result = document.Evaluate("//missing", document, type: type);
        switch (type)
        {
            case XPathResult.UnorderedNodeIteratorType or XPathResult.OrderedNodeIteratorType:
                Assert.Null(result.IterateNext());
                break;
            case XPathResult.UnorderedNodeSnapshotType or XPathResult.OrderedNodeSnapshotType:
                Assert.Equal(0u, result.SnapshotLength);
                Assert.Null(result.SnapshotItem(0));
                break;
            default:
                Assert.Null(result.SingleNodeValue);
                break;
        }
    }

    [Theory]
    [InlineData("1", XPathResult.NumberType)]
    [InlineData("'a'", XPathResult.StringType)]
    [InlineData("true()", XPathResult.BooleanType)]
    public void ScalarResultTypes_AccessorsOfOtherTypesThrow(string expression, ushort type)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        var result = document.Evaluate(expression, document);
        Assert.Equal(type, result.ResultType);
        if (type != XPathResult.NumberType)
            Assert.Throws<TypeErrorException>(() => result.NumberValue);

        if (type != XPathResult.StringType)
            Assert.Throws<TypeErrorException>(() => result.StringValue);

        if (type != XPathResult.BooleanType)
            Assert.Throws<TypeErrorException>(() => result.BooleanValue);

        Assert.Throws<TypeErrorException>(() => result.SingleNodeValue);
        Assert.Throws<TypeErrorException>(() => result.SnapshotLength);
        Assert.Throws<TypeErrorException>(() => result.SnapshotItem(0));
        Assert.Throws<TypeErrorException>(() => result.IterateNext());
        Assert.False(result.InvalidIteratorState);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("'a'")]
    [InlineData("true()")]
    public void ScalarExpressions_CannotBeConvertedToNodeSets(string expression)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>");
        for (var type = XPathResult.UnorderedNodeIteratorType; type <= XPathResult.FirstOrderedNodeType; type++)
        {
            Assert.Throws<TypeErrorException>(() => document.Evaluate(expression, document, type: type));
        }
    }

    [Theory]
    // Strings to numbers use the XPath Number production (no exponent, no leading '+', no thousands separator)
    [InlineData("'  12 '", 12)]
    [InlineData("'-1.5'", -1.5)]
    [InlineData("'.5'", 0.5)]
    [InlineData("'5.'", 5)]
    [InlineData("'\t\r\n7\n'", 7)]
    [InlineData("''", double.NaN)]
    [InlineData("'   '", double.NaN)]
    [InlineData("'abc'", double.NaN)]
    [InlineData("'.'", double.NaN)]
    [InlineData("'-'", double.NaN)]
    [InlineData("'1.2.3'", double.NaN)]
    [InlineData("'+1'", double.NaN)]
    [InlineData("'1e3'", double.NaN)]
    [InlineData("'1,000'", double.NaN)]
    [InlineData("'Infinity'", double.NaN)]
    [InlineData("'- 1'", double.NaN)]
    [InlineData("true()", 1)]
    [InlineData("false()", 0)]
    [InlineData("//li", 3)]
    [InlineData("//li[2]", double.NaN)]
    [InlineData("//li | //p", 3)]
    [InlineData("//missing", double.NaN)]
    public void Conversions_ToNumber(string expression, double expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li> 3 </li><li>x</li></ul><p>4</p>");
        Assert.Equal(expected, document.Evaluate(expression, document, type: XPathResult.NumberType).NumberValue);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("-0", "0")]
    [InlineData("1", "1")]
    [InlineData("-1", "-1")]
    [InlineData("1.5", "1.5")]
    [InlineData("0.1 + 0.2", "0.30000000000000004")]
    [InlineData("0.0000001", "0.0000001")]
    [InlineData("1 div 3", "0.3333333333333333")]
    [InlineData("12345678901234567", "12345678901234568")]
    [InlineData("100000000000000000000", "100000000000000000000")]
    [InlineData("100000000000000000000000", "100000000000000000000000")]
    [InlineData("-1000000000000000000000.5", "-1000000000000000000000")]
    [InlineData("123456789012345.6", "123456789012345.6")]
    [InlineData("0.00000000012345678901234567", "0.00000000012345678901234568")]
    [InlineData("-0.000001", "-0.000001")]
    [InlineData("0 div 0", "NaN")]
    [InlineData("1 div 0", "Infinity")]
    [InlineData("-1 div 0", "-Infinity")]
    [InlineData("true()", "true")]
    [InlineData("false()", "false")]
    [InlineData("//li", "a")]
    [InlineData("//p | //li", "a")]
    [InlineData("//missing", "")]
    public void Conversions_ToString(string expression, string expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li>a</li><li>b</li></ul><p>c</p>");
        Assert.Equal(expected, document.Evaluate(expression, document, type: XPathResult.StringType).StringValue);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("-0", false)]
    [InlineData("0 div 0", false)]
    [InlineData("0.5", true)]
    [InlineData("1 div 0", true)]
    [InlineData("''", false)]
    [InlineData("' '", true)]
    [InlineData("'0'", true)]
    [InlineData("//li", true)]
    [InlineData("//missing", false)]
    [InlineData("//li[. = '']", true)]
    public void Conversions_ToBoolean(string expression, bool expected)
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul><li></li></ul>");
        Assert.Equal(expected, document.Evaluate(expression, document, type: XPathResult.BooleanType).BooleanValue);
    }

    [Fact]
    public void Results_AreInDocumentOrder()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><div id=a title=t><span id=b></span><span id=c></span></div><span id=d></span>");
        Assert.Equal("a,@title,b,c,d", Ids(Snapshot(document, "//span | //@title | //div")));
        Assert.Equal("a,@id,@title", Ids(Snapshot(document, "//div/@* | //div")));
        Assert.Equal("a", ((Element)document.Evaluate("//span | //div", document, type: XPathResult.FirstOrderedNodeType).SingleNodeValue!).Id);
        Assert.Equal("b", ((Element)document.Evaluate("//span[@id='d']/preceding::span", document, type: XPathResult.FirstOrderedNodeType).SingleNodeValue!).Id);
        Assert.Equal("a", document.Evaluate("string(//span[@id='d']/preceding::*/@id)", document).StringValue);
        Assert.Equal("b,c", Ids(Snapshot(document, "//span[@id='d']/preceding::span")));
    }

    [Fact]
    public void Results_NodeSetsDoNotContainDuplicates()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p id=a><b id=b></b></p>");
        Assert.Equal("a,b", Ids(Snapshot(document, "//p | //* [@id] | //b | //b/..")));
    }

    [Fact]
    public void Iterator_IsInvalidatedByAttributeAndTextMutations()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a</p><p>b</p>");
        var result = document.Evaluate("//p", document, type: XPathResult.UnorderedNodeIteratorType);
        document.QuerySelector("p")!.SetAttribute("x", "1");
        Assert.True(result.InvalidIteratorState);
        Assert.Equal("InvalidStateError", Assert.Throws<DomException>(() => result.IterateNext()).Name);

        result = document.Evaluate("//p", document, type: XPathResult.OrderedNodeIteratorType);
        ((Text)document.QuerySelector("p")!.FirstChild!).Data = "changed";
        Assert.True(result.InvalidIteratorState);
    }

    [Fact]
    public void Iterator_IsNotInvalidatedByMutationsOfAnotherDocument()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a</p>");
        var other = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a</p>");
        var result = document.Evaluate("//p", document, type: XPathResult.OrderedNodeIteratorType);
        other.Body!.Append("x");
        Assert.False(result.InvalidIteratorState);
        Assert.NotNull(result.IterateNext());
    }

    [Fact]
    public void Snapshot_IsNotInvalidatedByMutations()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p>a</p><p>b</p>");
        var result = document.Evaluate("//p", document, type: XPathResult.OrderedNodeSnapshotType);
        var first = result.SnapshotItem(0)!;
        first.ParentNode!.RemoveChild(first);
        Assert.False(result.InvalidIteratorState);
        Assert.Equal(2u, result.SnapshotLength);
        Assert.Same(first, result.SnapshotItem(0));
    }

    [Fact]
    public void Expression_CanBeEvaluatedAgainstSeveralDocuments()
    {
        var expression = new XPathEvaluator().CreateExpression("count(//item)");
        var html = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><item></item><ITEM></ITEM>");
        var xml = XmlDocumentParser.ParseDocument("<root><item/><ITEM/><item/></root>");
        Assert.Equal(2d, expression.Evaluate(html).NumberValue);
        Assert.Equal(2d, expression.Evaluate(xml).NumberValue);
        Assert.Equal(2d, expression.Evaluate(html.Body!).NumberValue);

        // The result argument is ignored: a new result is returned
        var previous = expression.Evaluate(html);
        Assert.NotSame(previous, expression.Evaluate(html, XPathResult.AnyType, previous));
    }

    [Fact]
    public void Expression_IsEvaluatedRelativeToTheContextNode()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><ul id=a><li>1<li>2</ul><ul id=b><li>3</ul>");
        var expression = document.CreateExpression("count(li)");
        Assert.Equal(2d, expression.Evaluate(document.GetElementById("a")!).NumberValue);
        Assert.Equal(1d, expression.Evaluate(document.GetElementById("b")!).NumberValue);
        Assert.Equal(0d, expression.Evaluate(document).NumberValue);
    }

    [Fact]
    public void XPathEvaluator_Evaluate()
    {
        var document = HtmlDocumentParser.ParseDocument("<!DOCTYPE html><p id=x></p>");
        var evaluator = new XPathEvaluator();
        var result = evaluator.Evaluate("//P[@ID='x']", document, type: XPathResult.FirstOrderedNodeType);
        Assert.Same(document.GetElementById("x"), result.SingleNodeValue);
    }

    [Fact]
    public void CreatedDocuments()
    {
        var html = HtmlDocumentParser.ParseDocument("<!DOCTYPE html>").Implementation.CreateHtmlDocument("t");
        html.Body!.AppendChild(html.CreateElement("p"));
        Assert.Single(Snapshot(html, "//P"));

        var xml = html.Implementation.CreateDocument("http://www.w3.org/1999/xhtml", "html");
        xml.DocumentElement!.AppendChild(xml.CreateElementNS("http://www.w3.org/1999/xhtml", "p"));
        Assert.Empty(Snapshot(xml, "//p"));
        Assert.Single(Snapshot(xml, "//h:p", resolver: new Resolver(("h", "http://www.w3.org/1999/xhtml"))));
    }

    [Fact]
    public void LargeDocument()
    {
        var builder = new System.Text.StringBuilder("<!DOCTYPE html><ul>");
        for (var i = 0; i < 2000; i++)
        {
            builder.Append("<li data-i=").Append(i).Append('>').Append(i).Append("</li>");
        }

        var document = HtmlDocumentParser.ParseDocument(builder.Append("</ul>").ToString());
        Assert.Equal(2000d, document.Evaluate("count(//li)", document).NumberValue);
        Assert.Equal(1999000d, document.Evaluate("sum(//li)", document).NumberValue);
        Assert.Equal("1500", document.Evaluate("string(//li[@data-i = 1500])", document).StringValue);
        Assert.Equal("1999", document.Evaluate("string(//li[last()])", document).StringValue);
        var snapshot = document.Evaluate("//li[position() mod 2 = 0]", document, type: XPathResult.OrderedNodeSnapshotType);
        Assert.Equal(1000u, snapshot.SnapshotLength);
        Assert.Equal("999", snapshot.SnapshotItem(499)!.TextContent);
    }

    private sealed class Resolver(params (string Prefix, string Namespace)[] namespaces) : IXPathNSResolver
    {
        public Dictionary<string, string> Namespaces { get; } = namespaces.ToDictionary(item => item.Prefix, item => item.Namespace, StringComparer.Ordinal);

        public List<string?> Lookups { get; } = [];

        public string? LookupNamespaceUri(string? prefix)
        {
            Lookups.Add(prefix);
            return prefix is not null && Namespaces.TryGetValue(prefix, out var ns) ? ns : null;
        }
    }

    private sealed class ThrowingResolver : IXPathNSResolver
    {
        public string? LookupNamespaceUri(string? prefix) => throw new InvalidOperationException("resolver error");
    }

    [Fact]
    public void Documentation_XPathSnapshot()
    {
        #region WebPlatform_XPath
        var document = HtmlDocumentParser.ParseDocument("""
            <ul>
              <li data-price="12">Pen</li>
              <li data-price="45">Bag</li>
              <li data-price="30">Book</li>
            </ul>
            """);

        // A snapshot of the matching nodes, in document order
        var result = document.Evaluate("//li[@data-price > 20]", document, type: XPathResult.OrderedNodeSnapshotType);
        var names = new List<string>();
        for (var i = 0u; i < result.SnapshotLength; i++)
        {
            names.Add(result.SnapshotItem(i)!.TextContent!);
        }

        Assert.Equal(["Bag", "Book"], names);

        // The other result types: numbers, strings and booleans
        var total = document.Evaluate("sum(//li/@data-price)", document, type: XPathResult.NumberType).NumberValue;
        Assert.Equal(87, total);
        #endregion
    }
}
