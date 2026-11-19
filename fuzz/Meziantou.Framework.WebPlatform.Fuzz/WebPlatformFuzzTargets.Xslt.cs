using System.Text;
using System.Xml;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Xml;
using Meziantou.Framework.WebPlatform.Xslt;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// XSLTProcessor (https://dom.spec.whatwg.org/#interface-xsltprocessor): the compilation of a stylesheet and the transformations
public static partial class WebPlatformFuzzTargets
{
    // The input is the stylesheet, a NUL character, then the source document: the longer inputs are truncated, as the cost of
    // a transformation is polynomial in the size of the documents (the nesting of the loops and of the predicates)
    private const int XsltMaxLength = 4096;

    // The transformations that do not end (an exponential recursion, a loop over a large node-set) stop at these limits with
    // the error of the transformation
    private static readonly XsltLimits FuzzXsltLimits = new() { MaxSteps = 200_000, MaxOutputNodes = 20_000, MaxTemplateDepth = 256, MaxDocuments = 4 };

    /// <summary>The tokens of the XSLT target: the instructions, the attributes, the functions of XSLT and of XPath.</summary>
    private static string[] XsltTokens =>
    [
        "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\">", "</xsl:stylesheet>", "xsl:version=\"1.0\"",
        "<xsl:template match=\"", "<xsl:template name=\"", "</xsl:template>", "<xsl:apply-templates select=\"", "<xsl:apply-templates/>", "</xsl:apply-templates>",
        "<xsl:call-template name=\"", "</xsl:call-template>", "<xsl:apply-imports/>", "<xsl:for-each select=\"", "</xsl:for-each>", "<xsl:sort select=\"",
        "<xsl:value-of select=\"", "<xsl:copy-of select=\"", "<xsl:copy>", "</xsl:copy>", "<xsl:if test=\"", "</xsl:if>", "<xsl:choose>", "</xsl:choose>",
        "<xsl:when test=\"", "</xsl:when>", "<xsl:otherwise>", "</xsl:otherwise>", "<xsl:variable name=\"", "</xsl:variable>", "<xsl:param name=\"", "</xsl:param>",
        "<xsl:with-param name=\"", "<xsl:element name=\"", "</xsl:element>", "<xsl:attribute name=\"", "</xsl:attribute>", "<xsl:text>", "</xsl:text>",
        "<xsl:comment>", "<xsl:processing-instruction name=\"", "<xsl:number", "level=\"any\"", "level=\"multiple\"", "count=\"", "from=\"", "format=\"",
        "<xsl:key name=\"", "use=\"", "<xsl:output method=\"html\"/>", "<xsl:output method=\"text\"/>", "<xsl:output method=\"xml\"/>", "<xsl:strip-space elements=\"*\"/>",
        "<xsl:preserve-space elements=\"", "<xsl:attribute-set name=\"", "use-attribute-sets=\"", "<xsl:decimal-format", "<xsl:namespace-alias stylesheet-prefix=\"",
        "<xsl:message terminate=\"yes\">", "<xsl:fallback>", "<xsl:include href=\"", "<xsl:import href=\"", "mode=\"", "priority=\"", "data-type=\"number\"", "order=\"descending\"",
        "exclude-result-prefixes=\"", "extension-element-prefixes=\"", "xmlns:exsl=\"http://exslt.org/common\"", "exsl:node-set(", "exsl:object-type(",
        "current()", "key(", "document(", "format-number(", "generate-id(", "system-property(", "element-available(", "function-available(", "unparsed-entity-uri(",
        "count(", "position()", "last()", "name()", "local-name()", "concat(", "substring(", "translate(", "normalize-space(", "string(", "number(", "sum(",
        "node()", "text()", "comment()", "processing-instruction()", "@*", "*", "/", "//", "..", ".", "|", "[", "]", "$", "{", "}", "'", "\"", "\0",
    ];

    private static IEnumerable<FuzzSeed> GetXsltSeeds()
    {
        static string Sheet(string body, string attributes = "") => "<xsl:stylesheet version=\"1.0\" xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"" + attributes + ">" + body + "</xsl:stylesheet>";
        (string Stylesheet, string Source)[] inputs =
        [
            (Sheet("""<xsl:template match="/"><ul><xsl:for-each select="catalog/book"><xsl:sort select="@year" data-type="number"/><li><xsl:value-of select="title"/></li></xsl:for-each></ul></xsl:template>"""), "<catalog><book year='2004'><title>Second</title></book><book year='1999'><title>First</title></book></catalog>"),
            (Sheet("""<xsl:output method="xml"/><xsl:param name="greeting" select="'Hello'"/><xsl:template match="/"><message><xsl:value-of select="concat($greeting, ', ', name)"/></message></xsl:template>"""), "<name>Alice</name>"),
            (Sheet("""<xsl:template match="/"><HTML><body><p>a</p></body></HTML></xsl:template>"""), "<root/>"),
            (Sheet("""<xsl:output method="html" doctype-system="about:legacy-compat"/><xsl:template match="/"><p>a</p><a/><b/></xsl:template>"""), "<root/>"),
            (Sheet("""<xsl:output method="text"/><xsl:template match="/">a &lt; b</xsl:template>"""), "<root/>"),
            (Sheet("""<xsl:template match="/"><xsl:apply-templates select="//b"/></xsl:template><xsl:template match="*">*</xsl:template><xsl:template match="b">b</xsl:template><xsl:template match="a/b">a/b</xsl:template><xsl:template match="b[@k]" priority="-1">low</xsl:template>"""), "<root><a><b/></a><b/><b k='1'/></root>"),
            (Sheet("""<xsl:template match="/"><xsl:apply-templates/>|<xsl:apply-templates select="//b" mode="m"><xsl:with-param name="suffix" select="'!'"/></xsl:apply-templates></xsl:template><xsl:template match="b" mode="m"><xsl:param name="suffix"/><xsl:param name="default" select="'?'"/><xsl:value-of select="concat(., $suffix, $default)"/></xsl:template><xsl:template match="c"/>"""), "<root><a><b>x</b></a><b>y</b><c>skipped</c></root>"),
            (Sheet("""<xsl:variable name="separator" select="','"/><xsl:template match="/"><xsl:call-template name="count"><xsl:with-param name="n" select="3"/></xsl:call-template></xsl:template><xsl:template name="count"><xsl:param name="n"/><xsl:if test="$n &gt; 0"><xsl:value-of select="$n"/><xsl:value-of select="$separator"/><xsl:call-template name="count"><xsl:with-param name="n" select="$n - 1"/></xsl:call-template></xsl:if></xsl:template>"""), "<root/>"),
            (Sheet("""<xsl:template match="/"><xsl:apply-templates select="."/></xsl:template>"""), "<root/>"),
            (Sheet("""<xsl:attribute-set name="base"><xsl:attribute name="class">c</xsl:attribute></xsl:attribute-set><xsl:template match="/"><r xsl:use-attribute-sets="base" id="{root/@id}-{{x}}"><xsl:element name="p:e" namespace="urn:e"><xsl:attribute name="a">1</xsl:attribute></xsl:element><xsl:copy-of select="root/item"/><xsl:for-each select="root/item"><xsl:copy><xsl:value-of select="@n"/></xsl:copy></xsl:for-each><xsl:comment>c</xsl:comment><xsl:processing-instruction name="pi">d</xsl:processing-instruction></r></xsl:template>""", """ xmlns:p="urn:p" exclude-result-prefixes="p" """), "<root id='a'><item n='1'>x</item></root>"),
            (Sheet("""<xsl:key name="k" match="item" use="@type"/><xsl:decimal-format name="fr" decimal-separator="," grouping-separator=" "/><xsl:template match="/"><xsl:value-of select="count(key('k', 'a'))"/>|<xsl:value-of select="format-number(1234.5, '#,##0.00')"/>|<xsl:value-of select="format-number(1234.5, '# ##0,0', 'fr')"/>|<xsl:value-of select="generate-id(root)"/>|<xsl:value-of select="system-property('xsl:vendor')"/>|<xsl:for-each select="root/item"><xsl:number format="i. "/></xsl:for-each>|<xsl:number value="28" format="A"/>|<xsl:number value="1234567" grouping-separator="," grouping-size="3"/></xsl:template>"""), "<root><item type='a'>x</item><item type='b'>y</item><item type='a'>z</item></root>"),
            (Sheet("""<xsl:template match="/"><xsl:for-each select="root/i"><xsl:sort select="." order="descending"/><xsl:value-of select="."/></xsl:for-each><xsl:variable name="fragment"><a>1</a><a>2</a></xsl:variable><xsl:value-of select="count(exsl:node-set($fragment)/a)"/><xsl:value-of select="exsl:object-type($fragment)"/></xsl:template>""", """ xmlns:exsl="http://exslt.org/common" """), "<root><i>10</i><i>9</i><i>3</i><i>1</i></root>"),
            (Sheet("""<xsl:strip-space elements="*"/><xsl:preserve-space elements="keep"/><xsl:template match="/"><xsl:value-of select="count(//text())"/></xsl:template>"""), "<root> <a> </a> <keep> </keep> <b>x</b></root>"),
            (Sheet("""<xsl:template match="/"><xsl:for-each select="//*"><xsl:number level="multiple" count="*" format="1.1 "/><xsl:number level="any" from="a"/></xsl:for-each></xsl:template>"""), "<r><a><b/><b><c/></b></a><a/></r>"),
            (Sheet("""<xsl:template match="*"><xsl:value-of select="name(.)"/></xsl:template>"""), "<items><item>Item 1</item><item>Item 2</item></items>"),
            (Sheet("""<xsl:template match="/"><result><xsl:apply-templates select="document('resources/test.xml')//static"/><xsl:apply-templates select="foo"/><count><xsl:value-of select="count(document('', /foo))"/></count></result></xsl:template><xsl:template match="foo"><success/></xsl:template>"""), "<foo><bar>x</bar></foo>"),
            (Sheet("""<xsl:output method="html" encoding="utf-8" version="5"/><xsl:template match="/"><xsl:choose><xsl:when test='function-available("current")'><div>available</div></xsl:when><xsl:otherwise><div>unavailable</div></xsl:otherwise></xsl:choose></xsl:template>"""), "<x/>"),
            (Sheet("""<xsl:output method="xml"/><xsl:template match="/"><xsl:copy-of select="/*"/></xsl:template>"""), "<svg xmlns='http://www.w3.org/2000/svg'><script>window.mark = 1;</script></svg>"),
            (Sheet("""<xsl:import/><xsl:template match="/"><result>ok</result></xsl:template>"""), "<source/>"),
            ("""<xsl:transform version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"><xsl:output indent="no" omit-xml-declaration="no" encoding="bad-encoding" standalone="yes"/></xsl:transform>""", "<?xml version='1.0'?><root/>"),
            (Sheet("""<xsl:template match="/"><r><xsl:for-each select="//*"><e><xsl:attribute name="a" xml:space="preserve">    </xsl:attribute></e></xsl:for-each></r></xsl:template>"""), "<r><a></a><b></b></r>"),
            (Sheet("""<xsl:strip-space elements="s"/><xsl:template match="/"><xsl:variable name="space"><s><xsl:text> </xsl:text><e/><xsl:text> </xsl:text></s></xsl:variable><xsl:apply-templates select="exsl:node-set($space)/s"/></xsl:template><xsl:template match="s"><r><xsl:variable name="text-nodes" select="text()"/><xsl:apply-templates/><xsl:copy-of select="$text-nodes"/></r></xsl:template><xsl:template match="node()"/>""", """ xmlns:exsl="http://exslt.org/common" extension-element-prefixes="exsl" """), "<root/>"),
            (Sheet("""<xsl:variable name="w"><xsl:apply-templates select="/r/n" mode="num"/></xsl:variable><xsl:variable name="v"><xsl:text>v</xsl:text></xsl:variable><xsl:template match="n" mode="num"><xsl:number level="single" count="never | n[@id = '1'][boolean($v)]"/></xsl:template><xsl:template match="/"><html><body><xsl:value-of select="$w"/></body></html></xsl:template>"""), "<r><n id='1'/></r>"),
            ("""<root xsl:version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform"><xsl:value-of select="."/></root>""", "<a>text</a>"),
            (Sheet("""<xsl:template match="/">[<xsl:apply-templates select="root/item"/>][<xsl:for-each select="root/item"><xsl:sort select="."/></xsl:for-each>]</xsl:template><xsl:template match="item"><xsl:param name="p"/></xsl:template>"""), "<root><item>b</item><item>a</item></root>"),
            (Sheet("""<xsl:attribute-set name="a" use-attribute-sets="b c"><xsl:attribute name="v">a</xsl:attribute></xsl:attribute-set><xsl:attribute-set name="b" use-attribute-sets="c"><xsl:attribute name="b">1</xsl:attribute></xsl:attribute-set><xsl:attribute-set name="c" use-attribute-sets="d d"><xsl:attribute name="v">c</xsl:attribute></xsl:attribute-set><xsl:attribute-set name="d"><xsl:attribute name="d"><xsl:value-of select="count(//*)"/></xsl:attribute></xsl:attribute-set><xsl:attribute-set name="e" use-attribute-sets="f"/><xsl:attribute-set name="f" use-attribute-sets="e"/><xsl:template match="/"><r xsl:use-attribute-sets="a" w="r"/><xsl:if test="root/@cycle"><r xsl:use-attribute-sets="e"/></xsl:if></xsl:template>"""), "<root><x/></root>"),
        ];

        return inputs.Select((input, index) => new FuzzSeed($"xslt-{index}", (byte[])[.. Encoding.UTF8.GetBytes(input.Stylesheet), 0, .. Encoding.UTF8.GetBytes(input.Source)]));
    }

    /// <summary>
    /// Compiles a stylesheet and transforms a source document with it (the stylesheet, a NUL character, then the source). The
    /// errors of the stylesheet and of the transformation are the documented <c>OperationError</c>; the properties checked:
    /// transformToFragment() and transformToDocument() agree (they fail together, and the XML result of both is the same
    /// tree), and a transformation gives the same result when it runs again.
    /// </summary>
    public static void Xslt(ReadOnlySpan<byte> data)
    {
        if (data.Length > XsltMaxLength)
        {
            data = data[..XsltMaxLength];
        }

        var stylesheetText = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
        var sourceText = FuzzText.Decode(data);
        Document stylesheet;
        Document source;
        try
        {
            stylesheet = XmlDocumentParser.ParseDocument(stylesheetText);
            source = XmlDocumentParser.ParseDocument(sourceText);
        }
        catch (XmlException)
        {
            return;
        }

        var processor = new XsltProcessor { Limits = FuzzXsltLimits };
        processor.ImportStylesheet(stylesheet);
        var output = XmlDocumentParser.ParseDocument("<output/>");
        string? fragmentMarkup = null;
        DocumentFragment? fragment = null;
        try
        {
            fragment = processor.TransformToFragment(source, output);
            fragmentMarkup = XmlSerialization.SerializeChildren(fragment, requireWellFormed: false);
        }
        catch (DomException ex) when (ex.Name == "OperationError")
        {
        }

        Document? document = null;
        try
        {
            document = processor.TransformToDocument(source);
        }
        catch (DomException ex) when (ex.Name == "OperationError")
        {
        }

        // A transformation that ends in an error fails with both methods (the limits of the transformations count the same steps)
        FuzzAssert.Equal(fragmentMarkup is null, document is null, $"transformToFragment() and transformToDocument() disagree on {FuzzAssert.Format(stylesheetText)}");
        if (fragment is null || document is null)
            return;

        // The transformation is deterministic: it gives the same result when it runs again (the compiled stylesheet is reused)
        var again = XmlSerialization.SerializeChildren(processor.TransformToFragment(source, output), requireWellFormed: false);
        FuzzAssert.Equal(fragmentMarkup, again, $"Transforming again with {FuzzAssert.Format(stylesheetText)}");

        // An XML result that is a well-formed document is the same tree in the fragment and in the document
        var elements = fragment.ChildNodes.OfType<Element>().ToList();
        if (document.ContentType != "text/html" && elements.Count == 1 && document.DocumentElement is { } root && root.NamespaceUri != "http://www.mozilla.org/TransforMiix" && !fragment.ChildNodes.Any(node => node is Text text && text.Data.AsSpan().Trim(" \t\r\n").Length > 0))
        {
            FuzzAssert.Equal(XmlSerialization.Serialize(elements[0], requireWellFormed: false), XmlSerialization.Serialize(root, requireWellFormed: false), $"The element of the fragment and of the document of {FuzzAssert.Format(stylesheetText)}");
        }
    }
}
