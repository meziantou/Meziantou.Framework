using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.Xml.Tests;

public sealed class XmlSyntaxTreeTests
{
    public static TheoryData<string> RoundTripSamples => new()
    {
        "<?xml version=\"1.0\"?><root xmlns='urn:default' xmlns:a='urn:attr'><a:item id='1'>value</a:item><!--comment--><![CDATA[data]]></root>",
        """
<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE root>
<root attr = 'value'>
  <child />
  <?pi data?>
</root>
""",
        """
<root xmlns='urn:default' xmlns:a='urn:attr'>
  <item id='1' a:flag='on'>text</item>
  <plain xmlns='' id='2' />
</root>
""",
    };

    [Fact]
    public void ParseText_RoundTripsValidXml()
    {
        const string Text = "<?xml version=\"1.0\"?><root attr=\"value\"><child>sample</child><!--c--><![CDATA[data]]></root>";
        var tree = XmlSyntaxTree.ParseText(Text);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(Text, tree.Root.ToFullString());
    }

    [Fact]
    public void ParseText_InvalidXml_DoesNotThrowAndReturnsDiagnostics()
    {
        var exception = Record.Exception(() => XmlSyntaxTree.ParseText("<root><child></root>"));
        var tree = XmlSyntaxTree.ParseText("<root><child></root>");

        Assert.Null(exception);
        Assert.NotEmpty(tree.Diagnostics);
        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "XML0001" || diagnostic.Id == "XML0002");
        Assert.All(tree.Diagnostics, diagnostic => Assert.Equal(XmlDiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void SyntaxFactory_CreatesNodes()
    {
        var element = SyntaxFactory.Element(
            "book",
            [SyntaxFactory.Attribute("id", "42")],
            [SyntaxFactory.Text("hello")],
            isSelfClosing: false);

        Assert.Equal("<book id=\"42\">hello</book>", element.ToFullString());
    }

    [Fact]
    public void ReplaceNode_ReplacesElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a>1</a><b>2</b></root>");
        var oldNode = tree.Root.DescendantNodes().OfType<XmlElementSyntax>().First(node => node.Name == "a");
        var replacement = SyntaxFactory.Element("a", [SyntaxFactory.Text("updated")]);

        var updated = tree.Root.ReplaceNode(oldNode, replacement);

        Assert.Equal("<root><a>updated</a><b>2</b></root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_ReplacesExactInstance_WhenNodeTextIsDuplicated()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a>1</a><a>1</a></root>");
        var oldNode = tree.Root.DescendantNodes().OfType<XmlElementSyntax>().Where(node => node.Name == "a").Skip(1).First();
        var replacement = oldNode.WithInnerText("2");

        var updated = tree.Root.ReplaceNode(oldNode, replacement);

        Assert.Equal("<root><a>1</a><a>2</a></root>", updated.ToFullString());
    }

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void Parse_Save_RoundTripsSamples(string text)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<root />")]
    [InlineData("<?xml version=\"1.0\" standalone=\"yes\" encoding=\"UTF-8\" ?>\n<root />")]
    [InlineData("<?xml version=\"1.0\"\n encoding=\"UTF-8\"\n standalone=\"yes\" ?>\n<root />")]
    public void Parse_Save_RoundTripsXmlDeclarationVariants(string text)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Fact]
    public void Parse_Edit_Save_DeclarationWithVersion_PreservesWhitespace()
    {
        const string Text = "<?xml version = '1.0' ?>\n<root />";
        var tree = XmlSyntaxTree.ParseText(Text);
        var declaration = Assert.IsType<XmlDeclarationSyntax>(tree.Root.ChildNodes[0]);

        var updated = tree.Root.ReplaceNode(declaration, declaration.WithVersion("2.0"));

        Assert.Equal("<?xml version = '2.0' ?>\n<root />", updated.ToFullString());
    }

    [Fact]
    public void Parse_Edit_Save_DeclarationWithVersion_PreservesAttributeOrder()
    {
        const string Text = "<?xml version=\"1.0\" standalone=\"yes\" encoding=\"UTF-8\" ?>\n<root />";
        var tree = XmlSyntaxTree.ParseText(Text);
        var declaration = Assert.IsType<XmlDeclarationSyntax>(tree.Root.ChildNodes[0]);

        var updated = tree.Root.ReplaceNode(declaration, declaration.WithVersion("2.0"));

        Assert.Equal("<?xml version=\"2.0\" standalone=\"yes\" encoding=\"UTF-8\" ?>\n<root />", updated.ToFullString());
    }

    [Fact]
    public void XmlDeclaration_ExposesEditableAttributeNodes()
    {
        const string Text = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<root />";
        var tree = XmlSyntaxTree.ParseText(Text);
        var declaration = Assert.IsType<XmlDeclarationSyntax>(tree.Root.ChildNodes[0]);
        var versionAttribute = Assert.IsType<XmlAttributeSyntax>(declaration.VersionAttribute);

        var updated = tree.Root.ReplaceNode(
            versionAttribute,
            versionAttribute.WithLeadingTrivia([SyntaxFactory.Trivia(XmlSyntaxKind.WhitespaceTrivia, "  ")]));

        Assert.Equal("<?xml  version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<root />", updated.ToFullString());
    }

    [Fact]
    public void Parse_Edit_Save_UpdatesNamespacedAttributeOnly()
    {
        const string Text = "<root xmlns:a='urn:a' xmlns:b='urn:b'><item a:value='1' b:value='2' value='3' /></root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("a", "urn:a");
        namespaceManager.AddNamespace("b", "urn:b");

        var updated = tree.Root.ReplaceNode("//item/@b:value", namespaceManager, static node => ((XmlAttributeSyntax)node).WithValue("9"));

        Assert.Equal("<root xmlns:a='urn:a' xmlns:b='urn:b'><item a:value='1' b:value='9' value='3' /></root>", updated.ToFullString());
    }

    [Fact]
    public void Parse_Edit_Save_UpdatesElementValueOnly()
    {
        const string Text = """
<root xmlns='urn:default'>
  <item version='1'>old-value</item>
</root>
""";

        var tree = XmlSyntaxTree.ParseText(Text);
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("d", "urn:default");

        var updated = tree.Root.ReplaceNode("//d:item", namespaceManager, static node => ((XmlElementSyntax)node).WithInnerText("new-value"));

        Assert.Equal(
            """
<root xmlns='urn:default'>
  <item version='1'>new-value</item>
</root>
""",
            updated.ToFullString());
    }

    [Fact]
    public void Formatter_IsDeterministic()
    {
        const string Text = "<root><a>1</a><b>2</b></root>";
        var formatted1 = Formatter.Format(XmlSyntaxTree.ParseText(Text));
        var formatted2 = Formatter.Format(XmlSyntaxTree.ParseText(formatted1.ToFullString()));

        Assert.Equal(formatted1.ToFullString(), formatted2.ToFullString());
    }

    [Fact]
    public void SourceText_ExposesLines()
    {
        var tree = XmlSyntaxTree.ParseText("<root>\n  <a />\n</root>");

        Assert.Equal(3, tree.SourceText.Lines.Count);
        Assert.Equal("<root>", tree.SourceText.Lines[0].Text);
    }

    [Fact]
    public void XPathNavigator_SelectsNodes()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book id=\"1\"/><book id=\"2\"/></root>");
        var nodes = tree.Root.SelectNodes("//book[@id='2']").ToList();

        Assert.Single(nodes);
        Assert.Equal(XPathNodeType.Element, nodes[0].NodeType);
        Assert.Equal("book", nodes[0].Name);
    }

    [Fact]
    public void SelectSyntaxNodes_SelectsCommentNodes()
    {
        var tree = XmlSyntaxTree.ParseText("<root><!--first--><book id='1' /><!--second--></root>");

        var comments = tree.Root.SelectSyntaxNodes("//comment()").Cast<XmlCommentSyntax>().ToList();

        Assert.Equal(2, comments.Count);
        Assert.Equal("first", comments[0].Text);
        Assert.Equal("second", comments[1].Text);
    }

    [Fact]
    public void SelectSyntaxNodes_SelectsTextNodesIncludingCData()
    {
        var tree = XmlSyntaxTree.ParseText("<root><item>alpha</item><item><![CDATA[beta]]></item></root>");

        var textNodes = tree.Root.SelectSyntaxNodes("//item/text()").ToList();

        Assert.Equal(2, textNodes.Count);
        Assert.Equal("alpha", Assert.IsType<XmlTextSyntax>(textNodes[0]).Text);
        Assert.Equal("beta", Assert.IsType<XmlCDataSectionSyntax>(textNodes[1]).Text);
    }

    [Fact]
    public void XPathNavigator_UsesSyntaxNodeAsUnderlyingObject()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book version='1.0.0' /></root>");
        var navigator = tree.Root.SelectSingleNode("//book/@version");

        var attribute = Assert.IsType<XmlAttributeSyntax>(navigator?.UnderlyingObject);
        Assert.Equal("version", attribute.Name);
    }

    [Fact]
    public void XPathNavigator_SelectsFromInvalidXml()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book id='1'></root>");

        var node = tree.Root.SelectSingleSyntaxNode("//book/@id");

        var attribute = Assert.IsType<XmlAttributeSyntax>(node);
        Assert.Equal("1", attribute.Value);
    }

    [Fact]
    public void SelectSingleSyntaxNode_SelectsElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book id=\"1\"/><book id=\"2\"/></root>");

        var node = tree.Root.SelectSingleSyntaxNode("//book[@id='2']");

        var element = Assert.IsType<XmlElementSyntax>(node);
        Assert.Equal("book", element.Name);
        Assert.Equal("2", element.GetAttribute("id")?.Value);
    }

    [Fact]
    public void SelectSingleSyntaxNode_SelectsAttribute()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book version = '1.0.0' /></root>");

        var node = tree.Root.SelectSingleSyntaxNode("//book/@version");

        var attribute = Assert.IsType<XmlAttributeSyntax>(node);
        Assert.Equal("version", attribute.Name);
        Assert.Equal("1.0.0", attribute.Value);
    }

    [Fact]
    public void ReplaceNode_UsingXPathSelection_PreservesFormatting()
    {
        const string Text = "<root>\n  <book version = '1.0.0' />\n</root>";
        var tree = XmlSyntaxTree.ParseText(Text);

        var updated = tree.Root.ReplaceNode("//book/@version", static node => ((XmlAttributeSyntax)node).WithValue("2.0.0"));

        Assert.Equal("<root>\n  <book version = '2.0.0' />\n</root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceToken_WithLeadingTrivia_AddsSpaceBeforeAttributeName()
    {
        const string Text = "<root>\n  <book version = '1.0.0' />\n</root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var attribute = Assert.IsType<XmlAttributeSyntax>(tree.Root.SelectSingleSyntaxNode("//book/@version"));
        var attributeNameToken = Assert.Single(attribute.Tokens, static token => token.Kind == XmlSyntaxKind.IdentifierToken);
        var updatedNameToken = attributeNameToken.WithLeadingTrivia([SyntaxFactory.Trivia(XmlSyntaxKind.WhitespaceTrivia, "  ")]);

        var updated = tree.Root.ReplaceToken(attributeNameToken, updatedNameToken);

        Assert.Equal("<root>\n  <book   version = '1.0.0' />\n</root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceToken_WithTrailingTrivia_AddsSpaceBeforeEqualsSign()
    {
        const string Text = "<root>\n  <book version = '1.0.0' />\n</root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var attribute = Assert.IsType<XmlAttributeSyntax>(tree.Root.SelectSingleSyntaxNode("//book/@version"));
        var attributeNameToken = Assert.Single(attribute.Tokens, static token => token.Kind == XmlSyntaxKind.IdentifierToken);
        var updatedNameToken = attributeNameToken.WithTrailingTrivia([SyntaxFactory.Trivia(XmlSyntaxKind.WhitespaceTrivia, " ")]);

        var updated = tree.Root.ReplaceToken(attributeNameToken, updatedNameToken);

        Assert.Equal("<root>\n  <book version  = '1.0.0' />\n</root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_WithLeadingTrivia_AddsSpaceBeforeAttributeName()
    {
        const string Text = "<root>\n  <book version = '1.0.0' />\n</root>";
        var tree = XmlSyntaxTree.ParseText(Text);

        var updated = tree.Root.ReplaceNode("//book/@version", static node => ((XmlAttributeSyntax)node).WithLeadingTrivia([SyntaxFactory.Trivia(XmlSyntaxKind.WhitespaceTrivia, "  ")]));

        Assert.Equal("<root>\n  <book   version = '1.0.0' />\n</root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_WithTrailingTrivia_OnElementName_AdjustsSpacingBeforeFirstAttribute()
    {
        const string Text = "<root><book id='1' /></root>";
        var tree = XmlSyntaxTree.ParseText(Text);

        var updated = tree.Root.ReplaceNode("//book", static node => ((XmlElementSyntax)node).WithTrailingTrivia([SyntaxFactory.Trivia(XmlSyntaxKind.WhitespaceTrivia, "  ")]));

        Assert.Equal("<root><book  id='1' /></root>", updated.ToFullString());
    }

    [Fact]
    public void SelectSingleSyntaxNode_MixedContent_SelectsExpectedElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root>prefix<item id=\"1\"/>suffix<item id=\"2\"/></root>");

        var node = tree.Root.SelectSingleSyntaxNode("//item[@id='2']");

        var element = Assert.IsType<XmlElementSyntax>(node);
        Assert.Equal("item", element.Name);
        Assert.Equal("2", element.GetAttribute("id")?.Value);
    }

    [Fact]
    public void SelectSingleSyntaxNode_WithXPathConditions_SelectsExpectedElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root><item id='a'>alpha</item><item id='b'>beta</item><item id='c'>gamma</item></root>");

        var byTextAndAttribute = tree.Root.SelectSingleSyntaxNode("//item[@id='a' and contains(text(),'alp')]");
        var byPosition = tree.Root.SelectSingleSyntaxNode("/root/item[position()=2]");
        var byNotCondition = tree.Root.SelectSingleSyntaxNode("//item[not(@id='a') and text()='beta']");

        Assert.Equal("a", Assert.IsType<XmlElementSyntax>(byTextAndAttribute).GetAttribute("id")?.Value);
        Assert.Equal("b", Assert.IsType<XmlElementSyntax>(byPosition).GetAttribute("id")?.Value);
        Assert.Equal("b", Assert.IsType<XmlElementSyntax>(byNotCondition).GetAttribute("id")?.Value);
    }

    [Fact]
    public void SelectSingleSyntaxNode_WithNamespacePrefix_SelectsElementAndAttribute()
    {
        var tree = XmlSyntaxTree.ParseText("<root xmlns:pkg='urn:test'><pkg:item pkg:version='1.0.0' /></root>");
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("pkg", "urn:test");

        var elementNode = tree.Root.SelectSingleSyntaxNode("//pkg:item", namespaceManager);
        var attributeNode = tree.Root.SelectSingleSyntaxNode("//pkg:item/@pkg:version", namespaceManager);

        var element = Assert.IsType<XmlElementSyntax>(elementNode);
        var attribute = Assert.IsType<XmlAttributeSyntax>(attributeNode);
        Assert.Equal("pkg:item", element.Name);
        Assert.Equal("pkg:version", attribute.Name);
    }

    [Fact]
    public void SelectSingleSyntaxNode_WithDefaultNamespace_SelectsElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root xmlns='urn:default'><item id='1' /></root>");
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("d", "urn:default");

        var node = tree.Root.SelectSingleSyntaxNode("//d:item", namespaceManager);

        var element = Assert.IsType<XmlElementSyntax>(node);
        Assert.Equal("item", element.Name);
    }

    [Fact]
    public void SelectNodes_WithNamespaceResolver_ReturnsNamespaceUri()
    {
        var tree = XmlSyntaxTree.ParseText("<root xmlns:pkg='urn:test'><pkg:item /></root>");
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("pkg", "urn:test");

        var node = tree.Root.SelectSingleNode("//pkg:item", namespaceManager);

        Assert.NotNull(node);
        Assert.Equal("urn:test", node.NamespaceURI);
    }

    [Fact]
    public void SelectSingleSyntaxNode_WithMultipleNamespacesAndSameLocalNameAttributes()
    {
        var tree = XmlSyntaxTree.ParseText("<root xmlns:a='urn:a' xmlns:b='urn:b'><a:item a:value='1' b:value='2' value='0' /></root>");
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("a", "urn:a");
        namespaceManager.AddNamespace("b", "urn:b");

        var namespacedElement = tree.Root.SelectSingleSyntaxNode("//a:item", namespaceManager);
        var attributeInNamespaceA = tree.Root.SelectSingleSyntaxNode("//a:item/@a:value", namespaceManager);
        var attributeInNamespaceB = tree.Root.SelectSingleSyntaxNode("//a:item/@b:value", namespaceManager);
        var attributeWithoutNamespace = tree.Root.SelectSingleSyntaxNode("//a:item/@value", namespaceManager);

        _ = Assert.IsType<XmlElementSyntax>(namespacedElement);
        Assert.Equal("1", Assert.IsType<XmlAttributeSyntax>(attributeInNamespaceA).Value);
        Assert.Equal("2", Assert.IsType<XmlAttributeSyntax>(attributeInNamespaceB).Value);
        Assert.Equal("0", Assert.IsType<XmlAttributeSyntax>(attributeWithoutNamespace).Value);
    }

    [Fact]
    public void SelectNodes_WithMultipleNamespacesAndSameLocalNameAttributes()
    {
        var tree = XmlSyntaxTree.ParseText("<root xmlns:a='urn:a' xmlns:b='urn:b'><a:item a:value='1' b:value='2' /></root>");
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("a", "urn:a");
        namespaceManager.AddNamespace("b", "urn:b");

        var attributes = tree.Root.SelectSyntaxNodes("//a:item/@a:value | //a:item/@b:value", namespaceManager).Cast<XmlAttributeSyntax>().ToList();

        Assert.Equal(2, attributes.Count);
        Assert.Contains(attributes, attribute => attribute.Name == "a:value" && attribute.Value == "1");
        Assert.Contains(attributes, attribute => attribute.Name == "b:value" && attribute.Value == "2");
    }

    [Fact]
    public void SelectSingleSyntaxNode_NamespaceCoverage_ElementsWithDifferentScopes()
    {
        const string Text = """
<root xmlns='urn:default' xmlns:r='urn:root' xmlns:a='urn:attr'>
  <item id='root-default' a:flag='1' />
  <sub xmlns='urn:sub' xmlns:s='urn:sub-attr'>
    <item id='sub-default' s:flag='2' />
    <plain xmlns='' id='sub-no-ns' a:flag='3' flag='local' />
    <r:item id='prefixed-root-ns' />
  </sub>
  <plain xmlns='' id='root-no-ns' r:flag='4' />
</root>
""";

        var tree = XmlSyntaxTree.ParseText(Text);
        var namespaceManager = CreateNamespaceManager();

        Assert.IsType<XmlElementSyntax>(tree.Root.SelectSingleSyntaxNode("/d:root", namespaceManager));
        Assert.IsType<XmlElementSyntax>(tree.Root.SelectSingleSyntaxNode("//d:item[@id='root-default']", namespaceManager));
        Assert.IsType<XmlElementSyntax>(tree.Root.SelectSingleSyntaxNode("//sub:item[@id='sub-default']", namespaceManager));
        Assert.IsType<XmlElementSyntax>(tree.Root.SelectSingleSyntaxNode("//r:item[@id='prefixed-root-ns']", namespaceManager));
        Assert.IsType<XmlElementSyntax>(tree.Root.SelectSingleSyntaxNode("//plain[@id='sub-no-ns']", namespaceManager));
        Assert.IsType<XmlElementSyntax>(tree.Root.SelectSingleSyntaxNode("//plain[@id='root-no-ns']", namespaceManager));
        Assert.Null(tree.Root.SelectSingleSyntaxNode("//d:plain[@id='root-no-ns']", namespaceManager));
        Assert.Null(tree.Root.SelectSingleSyntaxNode("//sub:plain[@id='sub-no-ns']", namespaceManager));
    }

    [Fact]
    public void SelectSyntaxNodes_NamespaceCoverage_AttributesWithAndWithoutNamespaces()
    {
        const string Text = """
<root xmlns='urn:default' xmlns:r='urn:root' xmlns:a='urn:attr'>
  <item id='root-default' a:flag='1' />
  <sub xmlns='urn:sub' xmlns:s='urn:sub-attr'>
    <item id='sub-default' s:flag='2' />
    <plain xmlns='' id='sub-no-ns' a:flag='3' flag='local' />
  </sub>
  <plain xmlns='' id='root-no-ns' r:flag='4' />
</root>
""";

        var tree = XmlSyntaxTree.ParseText(Text);
        var namespaceManager = CreateNamespaceManager();

        var namespacedAttributes = tree.Root.SelectSyntaxNodes("//@a:flag | //@s:flag | //@r:flag", namespaceManager).Cast<XmlAttributeSyntax>().ToList();
        Assert.Equal(4, namespacedAttributes.Count);
        Assert.Contains(namespacedAttributes, attribute => attribute.Name == "a:flag" && attribute.Value == "1");
        Assert.Contains(namespacedAttributes, attribute => attribute.Name == "a:flag" && attribute.Value == "3");
        Assert.Contains(namespacedAttributes, attribute => attribute.Name == "s:flag" && attribute.Value == "2");
        Assert.Contains(namespacedAttributes, attribute => attribute.Name == "r:flag" && attribute.Value == "4");

        var nonNamespacedAttributes = tree.Root.SelectSyntaxNodes("//@id | //@flag", namespaceManager).Cast<XmlAttributeSyntax>().ToList();
        Assert.Equal(5, nonNamespacedAttributes.Count);
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "id" && attribute.Value == "root-default");
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "id" && attribute.Value == "sub-default");
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "id" && attribute.Value == "sub-no-ns");
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "id" && attribute.Value == "root-no-ns");
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "flag" && attribute.Value == "local");
    }

    private static XmlNamespaceManager CreateNamespaceManager()
    {
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("d", "urn:default");
        namespaceManager.AddNamespace("sub", "urn:sub");
        namespaceManager.AddNamespace("r", "urn:root");
        namespaceManager.AddNamespace("a", "urn:attr");
        namespaceManager.AddNamespace("s", "urn:sub-attr");
        return namespaceManager;
    }
}
