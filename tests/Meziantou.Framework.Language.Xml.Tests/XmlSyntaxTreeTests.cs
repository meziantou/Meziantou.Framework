using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.Language.Xml.Tests;

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

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(Text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void ParseText_InvalidXml_DoesNotThrowAndReturnsDiagnostics()
    {
        var exception = Record.Exception(() => XmlSyntaxTree.ParseText("<root><child></root>"));
        var tree = XmlSyntaxTree.ParseText("<root><child></root>");

        Assert.Null(exception);
        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "XML0001" || diagnostic.Id == "XML0002");
        Assert.All(tree.GetDiagnostics(), diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void SyntaxFactory_CreatesNodes()
    {
        var element = SyntaxFactory.XmlElement(
            "book",
            [SyntaxFactory.XmlAttribute("id", "42")],
            [SyntaxFactory.XmlText("hello")]);

        Assert.Equal("<book id=\"42\">hello</book>", element.ToFullString());
    }

    [Fact]
    public void ReplaceNode_ReplacesElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a>1</a><b>2</b></root>");
        var oldNode = tree.GetRoot().DescendantNodes().OfType<XmlElementSyntax>().First(node => node.Name == "a");
        var replacement = SyntaxFactory.XmlElement("a", SyntaxFactory.XmlText("updated"));

        var updated = tree.GetRoot().ReplaceNode(oldNode, replacement);

        Assert.Equal("<root><a>updated</a><b>2</b></root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_ReplacesExactInstance_WhenNodeTextIsDuplicated()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a>1</a><a>1</a></root>");
        var oldNode = tree.GetRoot().DescendantNodes().OfType<XmlElementSyntax>().Where(node => node.Name == "a").Skip(1).First();
        var replacement = oldNode.WithInnerText("2");

        var updated = tree.GetRoot().ReplaceNode(oldNode, replacement);

        Assert.Equal("<root><a>1</a><a>2</a></root>", updated.ToFullString());
    }

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void Parse_Save_RoundTripsSamples(string text)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<root />")]
    [InlineData("<?xml version=\"1.0\" standalone=\"yes\" encoding=\"UTF-8\" ?>\n<root />")]
    [InlineData("<?xml version=\"1.0\"\n encoding=\"UTF-8\"\n standalone=\"yes\" ?>\n<root />")]
    public void Parse_Save_RoundTripsXmlDeclarationVariants(string text)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void Parse_Edit_Save_DeclarationWithVersion_PreservesWhitespace()
    {
        const string Text = "<?xml version = '1.0' ?>\n<root />";
        var tree = XmlSyntaxTree.ParseText(Text);
        var declaration = Assert.IsType<XmlDeclarationSyntax>(tree.GetRoot().Nodes[0]);

        var updated = tree.GetRoot().ReplaceNode(declaration, declaration.WithVersion("2.0"));

        Assert.Equal("<?xml version = '2.0' ?>\n<root />", updated.ToFullString());
    }

    [Fact]
    public void Parse_Edit_Save_DeclarationWithVersion_PreservesAttributeOrder()
    {
        const string Text = "<?xml version=\"1.0\" standalone=\"yes\" encoding=\"UTF-8\" ?>\n<root />";
        var tree = XmlSyntaxTree.ParseText(Text);
        var declaration = Assert.IsType<XmlDeclarationSyntax>(tree.GetRoot().Nodes[0]);

        var updated = tree.GetRoot().ReplaceNode(declaration, declaration.WithVersion("2.0"));

        Assert.Equal("<?xml version=\"2.0\" standalone=\"yes\" encoding=\"UTF-8\" ?>\n<root />", updated.ToFullString());
    }

    [Fact]
    public void XmlDeclaration_ExposesEditableAttributeNodes()
    {
        const string Text = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n<root />";
        var tree = XmlSyntaxTree.ParseText(Text);
        var declaration = Assert.IsType<XmlDeclarationSyntax>(tree.GetRoot().Nodes[0]);
        var versionAttribute = Assert.IsType<XmlAttributeSyntax>(declaration.VersionAttribute);

        var updated = tree.GetRoot().ReplaceNode(
            versionAttribute,
            versionAttribute.WithLeadingTrivia([SyntaxFactory.Trivia(SyntaxKind.WhitespaceTrivia, "  ")]));

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

        var updated = tree.GetRoot().ReplaceNode("//item/@b:value", namespaceManager, static node => ((XmlAttributeSyntax)node).WithValue("9"));

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

        var updated = tree.GetRoot().ReplaceNode("//d:item", namespaceManager, static node => ((XmlElementSyntax)node).WithInnerText("new-value"));

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
    public void SourceText_ToStringReturnsTheText()
    {
        const string Text = "<root>\n  <a />\n</root>";

        Assert.Equal(Text, XmlSyntaxTree.ParseText(Text).GetText().ToString());
    }

    [Fact]
    public void SourceText_ExposesLines()
    {
        var tree = XmlSyntaxTree.ParseText("<root>\n  <a />\n</root>");

        Assert.HasCount(3, tree.GetText().Lines);
        Assert.Equal("<root>", tree.GetText().Lines[0].Text);
    }

    [Fact]
    public void XPathNavigator_SelectsNodes()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book id=\"1\"/><book id=\"2\"/></root>");
        var nodes = tree.GetRoot().SelectNodes("//book[@id='2']").ToList();

        Assert.Single(nodes);
        Assert.Equal(XPathNodeType.Element, nodes[0].NodeType);
        Assert.Equal("book", nodes[0].Name);
    }

    [Fact]
    public void SelectSyntaxNodes_SelectsCommentNodes()
    {
        var tree = XmlSyntaxTree.ParseText("<root><!--first--><book id='1' /><!--second--></root>");

        var comments = tree.GetRoot().SelectSyntaxNodes("//comment()").Cast<XmlCommentSyntax>().ToList();

        Assert.HasCount(2, comments);
        Assert.Equal("first", comments[0].Text);
        Assert.Equal("second", comments[1].Text);
    }

    [Fact]
    public void SelectSyntaxNodes_SelectsTextNodesIncludingCData()
    {
        var tree = XmlSyntaxTree.ParseText("<root><item>alpha</item><item><![CDATA[beta]]></item></root>");

        var textNodes = tree.GetRoot().SelectSyntaxNodes("//item/text()").ToList();

        Assert.HasCount(2, textNodes);
        Assert.Equal("alpha", Assert.IsType<XmlTextSyntax>(textNodes[0]).Text);
        Assert.Equal("beta", Assert.IsType<XmlCDataSectionSyntax>(textNodes[1]).Text);
    }

    [Fact]
    public void XPathNavigator_UsesSyntaxNodeAsUnderlyingObject()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book version='1.0.0' /></root>");
        var navigator = tree.GetRoot().SelectSingleNode("//book/@version");

        var attribute = Assert.IsType<XmlAttributeSyntax>(navigator?.UnderlyingObject);
        Assert.Equal("version", attribute.Name);
    }

    [Fact]
    public void XPathNavigator_SelectsFromInvalidXml()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book id='1'></root>");

        var node = tree.GetRoot().SelectSingleSyntaxNode("//book/@id");

        var attribute = Assert.IsType<XmlAttributeSyntax>(node);
        Assert.Equal("1", attribute.Value);
    }

    [Fact]
    public void SelectSingleSyntaxNode_SelectsElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book id=\"1\"/><book id=\"2\"/></root>");

        var node = tree.GetRoot().SelectSingleSyntaxNode("//book[@id='2']");

        var element = Assert.IsType<XmlEmptyElementSyntax>(node);
        Assert.Equal("book", element.Name);
        Assert.Equal("2", element.GetAttribute("id")?.Value);
    }

    [Fact]
    public void SelectSingleSyntaxNode_SelectsAttribute()
    {
        var tree = XmlSyntaxTree.ParseText("<root><book version = '1.0.0' /></root>");

        var node = tree.GetRoot().SelectSingleSyntaxNode("//book/@version");

        var attribute = Assert.IsType<XmlAttributeSyntax>(node);
        Assert.Equal("version", attribute.Name);
        Assert.Equal("1.0.0", attribute.Value);
    }

    [Fact]
    public void ReplaceNode_UsingXPathSelection_PreservesFormatting()
    {
        const string Text = "<root>\n  <book version = '1.0.0' />\n</root>";
        var tree = XmlSyntaxTree.ParseText(Text);

        var updated = tree.GetRoot().ReplaceNode("//book/@version", static node => ((XmlAttributeSyntax)node).WithValue("2.0.0"));

        Assert.Equal("<root>\n  <book version = '2.0.0' />\n</root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceToken_WithLeadingTrivia_AddsSpaceBeforeAttributeName()
    {
        const string Text = "<root>\n  <book version = '1.0.0' />\n</root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var attribute = Assert.IsType<XmlAttributeSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//book/@version"));
        var updatedNameToken = attribute.NameToken.WithLeadingTrivia([SyntaxFactory.Trivia(SyntaxKind.WhitespaceTrivia, "  ")]);

        var updated = tree.GetRoot().ReplaceToken(attribute.NameToken, updatedNameToken);

        // The space in front of the name is that token's own trivia now, so setting the trivia replaces it.
        Assert.Equal("<root>\n  <book  version = '1.0.0' />\n</root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceToken_WithTrailingTrivia_AddsSpaceBeforeEqualsSign()
    {
        const string Text = "<root>\n  <book version = '1.0.0' />\n</root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var attribute = Assert.IsType<XmlAttributeSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//book/@version"));
        var updatedNameToken = attribute.NameToken.WithTrailingTrivia([SyntaxFactory.Trivia(SyntaxKind.WhitespaceTrivia, " ")]);

        var updated = tree.GetRoot().ReplaceToken(attribute.NameToken, updatedNameToken);

        Assert.Equal("<root>\n  <book version  = '1.0.0' />\n</root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_WithLeadingTrivia_AddsSpaceBeforeAttributeName()
    {
        const string Text = "<root>\n  <book version = '1.0.0' />\n</root>";
        var tree = XmlSyntaxTree.ParseText(Text);

        var updated = tree.GetRoot().ReplaceNode("//book/@version", static node => node.WithLeadingTrivia(SyntaxFactory.Trivia(SyntaxKind.WhitespaceTrivia, "  ")));

        Assert.Equal("<root>\n  <book  version = '1.0.0' />\n</root>", updated.ToFullString());
    }

    [Fact]
    public void ReplaceToken_WithTrailingTrivia_OnElementName_AdjustsSpacingBeforeFirstAttribute()
    {
        const string Text = "<root><book id='1' /></root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var element = Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//book"));

        var updated = tree.GetRoot().ReplaceToken(element.NameToken, element.NameToken.WithTrailingTrivia(SyntaxFactory.Trivia(SyntaxKind.WhitespaceTrivia, "  ")));

        Assert.Equal("<root><book   id='1' /></root>", updated.ToFullString());
    }

    [Fact]
    public void SelectSingleSyntaxNode_MixedContent_SelectsExpectedElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root>prefix<item id=\"1\"/>suffix<item id=\"2\"/></root>");

        var node = tree.GetRoot().SelectSingleSyntaxNode("//item[@id='2']");

        var element = Assert.IsType<XmlEmptyElementSyntax>(node);
        Assert.Equal("item", element.Name);
        Assert.Equal("2", element.GetAttribute("id")?.Value);
    }

    [Fact]
    public void SelectSingleSyntaxNode_WithXPathConditions_SelectsExpectedElement()
    {
        var tree = XmlSyntaxTree.ParseText("<root><item id='a'>alpha</item><item id='b'>beta</item><item id='c'>gamma</item></root>");

        var byTextAndAttribute = tree.GetRoot().SelectSingleSyntaxNode("//item[@id='a' and contains(text(),'alp')]");
        var byPosition = tree.GetRoot().SelectSingleSyntaxNode("/root/item[position()=2]");
        var byNotCondition = tree.GetRoot().SelectSingleSyntaxNode("//item[not(@id='a') and text()='beta']");

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

        var elementNode = tree.GetRoot().SelectSingleSyntaxNode("//pkg:item", namespaceManager);
        var attributeNode = tree.GetRoot().SelectSingleSyntaxNode("//pkg:item/@pkg:version", namespaceManager);

        var element = Assert.IsType<XmlEmptyElementSyntax>(elementNode);
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

        var node = tree.GetRoot().SelectSingleSyntaxNode("//d:item", namespaceManager);

        var element = Assert.IsType<XmlEmptyElementSyntax>(node);
        Assert.Equal("item", element.Name);
    }

    [Fact]
    public void SelectNodes_WithNamespaceResolver_ReturnsNamespaceUri()
    {
        var tree = XmlSyntaxTree.ParseText("<root xmlns:pkg='urn:test'><pkg:item /></root>");
        var namespaceManager = new XmlNamespaceManager(new NameTable());
        namespaceManager.AddNamespace("pkg", "urn:test");

        var node = tree.GetRoot().SelectSingleNode("//pkg:item", namespaceManager);

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

        var namespacedElement = tree.GetRoot().SelectSingleSyntaxNode("//a:item", namespaceManager);
        var attributeInNamespaceA = tree.GetRoot().SelectSingleSyntaxNode("//a:item/@a:value", namespaceManager);
        var attributeInNamespaceB = tree.GetRoot().SelectSingleSyntaxNode("//a:item/@b:value", namespaceManager);
        var attributeWithoutNamespace = tree.GetRoot().SelectSingleSyntaxNode("//a:item/@value", namespaceManager);

        _ = Assert.IsType<XmlEmptyElementSyntax>(namespacedElement);
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

        var attributes = tree.GetRoot().SelectSyntaxNodes("//a:item/@a:value | //a:item/@b:value", namespaceManager).Cast<XmlAttributeSyntax>().ToList();

        Assert.HasCount(2, attributes);
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

        Assert.IsType<XmlElementSyntax>(tree.GetRoot().SelectSingleSyntaxNode("/d:root", namespaceManager));
        Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//d:item[@id='root-default']", namespaceManager));
        Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//sub:item[@id='sub-default']", namespaceManager));
        Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//r:item[@id='prefixed-root-ns']", namespaceManager));
        Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//plain[@id='sub-no-ns']", namespaceManager));
        Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//plain[@id='root-no-ns']", namespaceManager));
        Assert.Null(tree.GetRoot().SelectSingleSyntaxNode("//d:plain[@id='root-no-ns']", namespaceManager));
        Assert.Null(tree.GetRoot().SelectSingleSyntaxNode("//sub:plain[@id='sub-no-ns']", namespaceManager));
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

        var namespacedAttributes = tree.GetRoot().SelectSyntaxNodes("//@a:flag | //@s:flag | //@r:flag", namespaceManager).Cast<XmlAttributeSyntax>().ToList();
        Assert.HasCount(4, namespacedAttributes);
        Assert.Contains(namespacedAttributes, attribute => attribute.Name == "a:flag" && attribute.Value == "1");
        Assert.Contains(namespacedAttributes, attribute => attribute.Name == "a:flag" && attribute.Value == "3");
        Assert.Contains(namespacedAttributes, attribute => attribute.Name == "s:flag" && attribute.Value == "2");
        Assert.Contains(namespacedAttributes, attribute => attribute.Name == "r:flag" && attribute.Value == "4");

        var nonNamespacedAttributes = tree.GetRoot().SelectSyntaxNodes("//@id | //@flag", namespaceManager).Cast<XmlAttributeSyntax>().ToList();
        Assert.HasCount(5, nonNamespacedAttributes);
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "id" && attribute.Value == "root-default");
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "id" && attribute.Value == "sub-default");
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "id" && attribute.Value == "sub-no-ns");
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "id" && attribute.Value == "root-no-ns");
        Assert.Contains(nonNamespacedAttributes, attribute => attribute.Name == "flag" && attribute.Value == "local");
    }

    [Fact]
    public void Diagnostics_AreLocatedInTheTreesOwnSourceText()
    {
        var tree = XmlSyntaxTree.ParseText("<root>\n  <item>\n</root>");

        Assert.All(tree.GetDiagnostics(), diagnostic => Assert.Same(tree.GetText(), diagnostic.Location.SourceText));

        // The mismatched end tag on the third line, then the unclosed <item> indented on the second.
        Assert.Equal(new LinePosition(2, 0), tree.GetDiagnostics()[0].Location.GetLineSpan().Start);
        Assert.Equal(new LinePosition(1, 2), tree.GetDiagnostics()[1].Location.GetLineSpan().Start);
    }

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void Positions_AreAbsoluteOffsetsIntoTheSourceText(string text)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        foreach (var node in EnumerateNodes(tree.GetRoot()))
        {
            Assert.Equal(node.ToFullString(), text.Substring(node.FullSpan.Start, node.FullSpan.Length));
        }

        foreach (var token in tree.GetRoot().DescendantTokens())
        {
            Assert.Equal(token.Text, text.Substring(token.Span.Start, token.Span.Length));
        }
    }

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void ChildrenTileTheirParentExactly(string text)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        foreach (var node in EnumerateNodes(tree.GetRoot()))
        {
            var position = node.FullSpan.Start;
            foreach (var child in node.ChildNodesAndTokens())
            {
                Assert.Equal(position, child.FullSpan.Start);
                position = child.FullSpan.End;
            }

            if (node.ChildNodesAndTokens().Count > 0)
            {
                Assert.Equal(node.FullSpan.End, position);
            }
        }
    }

    [Fact]
    public void Positions_NestedElements()
    {
        const string Text = "<root><a>x</a></root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var root = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);
        var inner = Assert.IsType<XmlElementSyntax>(root.Content[0]);

        AssertSpan(0, 21, root.FullSpan);
        AssertSpan(6, 8, inner.FullSpan);
        AssertSpan(6, 3, inner.StartTag.FullSpan);
        AssertSpan(7, 1, inner.StartTag.NameToken.Span);
        AssertSpan(9, 1, inner.Content[0].FullSpan);

        var endTag = Assert.IsType<XmlElementEndTagSyntax>(inner.EndTag);
        AssertSpan(10, 4, endTag.FullSpan);
        AssertSpan(12, 1, endTag.NameToken.Span);
    }

    [Fact]
    public void Positions_EveryAttributeIsAnchoredToItsOwnStart()
    {
        const string Text = "<book id='1' name='x' />";
        var tree = XmlSyntaxTree.ParseText(Text);
        var element = Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().Nodes[0]);

        // An attribute's full span starts at the whitespace separating it from what precedes it, which is the
        // leading trivia of its name token.
        AssertSpan(5, 7, element.Attributes[0].FullSpan);
        AssertSpan(6, 2, element.Attributes[0].NameToken.Span);
        AssertSpan(10, 1, element.Attributes[0].ValueToken.Span);

        AssertSpan(12, 9, element.Attributes[1].FullSpan);
        AssertSpan(13, 4, element.Attributes[1].NameToken.Span);
        AssertSpan(19, 1, element.Attributes[1].ValueToken.Span);
    }

    [Fact]
    public void Positions_DeclarationPseudoAttributes()
    {
        const string Text = "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n<root />";
        var tree = XmlSyntaxTree.ParseText(Text);
        var declaration = Assert.IsType<XmlDeclarationSyntax>(tree.GetRoot().Nodes[0]);
        var versionAttribute = Assert.IsType<XmlAttributeSyntax>(declaration.VersionAttribute);
        var encodingAttribute = Assert.IsType<XmlAttributeSyntax>(declaration.EncodingAttribute);

        AssertSpan(0, 39, declaration.FullSpan);

        // A pseudo-attribute segment starts at the whitespace separating it from what precedes it, so the name token
        // is what lines up with the name in the source.
        Assert.Equal(Text.IndexOf("version", StringComparison.Ordinal), versionAttribute.NameToken.Span.Start);
        Assert.Equal(Text.IndexOf("encoding", StringComparison.Ordinal), encodingAttribute.NameToken.Span.Start);
        Assert.Equal(versionAttribute.ToFullString(), Text.Substring(versionAttribute.FullSpan.Start, versionAttribute.FullSpan.Length));
        Assert.Equal(encodingAttribute.ToFullString(), Text.Substring(encodingAttribute.FullSpan.Start, encodingAttribute.FullSpan.Length));

        AssertSpan(40, 8, tree.GetRoot().Nodes[2].FullSpan);
    }

    [Fact]
    public void Positions_CommentSkipsItsOpeningDelimiter()
    {
        var tree = XmlSyntaxTree.ParseText("<a><!--c--></a>");
        var element = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);
        var comment = Assert.IsType<XmlCommentSyntax>(element.Content[0]);

        AssertSpan(3, 8, comment.FullSpan);
        AssertSpan(7, 1, comment.TextToken.Span);
    }

    [Fact]
    public void Positions_CDataSectionSkipsItsOpeningDelimiter()
    {
        var tree = XmlSyntaxTree.ParseText("<a><![CDATA[d]]></a>");
        var element = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);
        var cdata = Assert.IsType<XmlCDataSectionSyntax>(element.Content[0]);

        AssertSpan(3, 13, cdata.FullSpan);
        AssertSpan(12, 1, cdata.TextToken.Span);
    }

    [Fact]
    public void Positions_LeadingWhitespaceShiftsTheRootElement()
    {
        var tree = XmlSyntaxTree.ParseText("\n  <root/>");

        AssertSpan(0, 3, tree.GetRoot().Nodes[0].FullSpan);
        AssertSpan(3, 7, tree.GetRoot().Nodes[1].FullSpan);
    }

    [Fact]
    public void Positions_UnclosedElementCoversTheRecoveredText()
    {
        const string Text = "<root><a></root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var root = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);

        AssertSpan(0, 16, root.FullSpan);

        var inner = Assert.IsType<XmlElementSyntax>(root.Content[0]);
        AssertSpan(6, 10, inner.FullSpan);

        var skipped = Assert.IsType<XmlSkippedTextSyntax>(inner.Content[0]);
        AssertSpan(9, 7, skipped.FullSpan);
    }

    [Fact]
    public void Positions_UnexpectedEndTagMatchesItsDiagnostic()
    {
        var tree = XmlSyntaxTree.ParseText("</a>");
        var skipped = Assert.IsType<XmlSkippedTextSyntax>(tree.GetRoot().Nodes[0]);
        var diagnostic = Assert.Single(tree.GetDiagnostics());

        AssertSpan(0, 4, skipped.FullSpan);
        AssertSpan(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length, skipped.FullSpan);
    }

    [Fact]
    public void Positions_UnterminatedComment()
    {
        var tree = XmlSyntaxTree.ParseText("<a><!--x");
        var element = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);
        var comment = Assert.IsType<XmlCommentSyntax>(element.Content[0]);

        AssertSpan(3, 5, comment.FullSpan);
        AssertSpan(7, 1, comment.TextToken.Span);
    }

    [Fact]
    public void Positions_InvalidStartTag()
    {
        var tree = XmlSyntaxTree.ParseText("<1bad>");
        var skipped = Assert.IsType<XmlSkippedTextSyntax>(tree.GetRoot().Nodes[0]);

        AssertSpan(0, 6, skipped.FullSpan);
    }

    [Fact]
    public void Positions_DetachedNodesStartAtZeroAndAreRestoredWhenReinserted()
    {
        const string Text = "<root><item /></root>";
        var tree = XmlSyntaxTree.ParseText(Text);
        var root = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);
        var item = Assert.IsType<XmlEmptyElementSyntax>(root.Content[0]);

        AssertSpan(6, 8, item.FullSpan);

        var renamed = item.WithName("other");
        Assert.Equal(0, renamed.FullSpan.Start);

        var updated = tree.GetRoot().ReplaceNode(item, renamed);
        var updatedRoot = Assert.IsType<XmlElementSyntax>(updated.Nodes[0]);
        var reinserted = Assert.IsType<XmlEmptyElementSyntax>(updatedRoot.Content[0]);

        Assert.Equal(6, reinserted.FullSpan.Start);
        Assert.Equal(reinserted.ToFullString(), updated.ToFullString().Substring(reinserted.FullSpan.Start, reinserted.FullSpan.Length));
    }

    [Fact]
    public void DescendantTokens_IncludesTheNodesOwnTokens()
    {
        var tree = XmlSyntaxTree.ParseText("<root id='1'><item /></root>");
        var root = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);

        // Every token of the element, in source order: its start tag, then its content, then its end tag.
        Assert.Equal(["<", "root", "id", "=", "'", "1", "'", ">", "<", "item", "/>", "</", "root", ">"], root.DescendantTokens().Select(token => token.Text));
    }

    [Fact]
    public void DescendantNodesAndTokens_ReportsNodesAndTokensInSourceOrder()
    {
        var tree = XmlSyntaxTree.ParseText("<root id='1'><item /></root>");
        var root = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);

        var children = root.ChildNodesAndTokens().Select(item => item.IsNode ? item.AsNode()!.Kind().ToString() : item.AsToken().Text);
        Assert.Equal(["XmlElementStartTag", "XmlEmptyElement", "XmlElementEndTag"], children);
    }

    [Fact]
    public void DocumentType_WithName_RenamesTheDeclaration()
    {
        var tree = XmlSyntaxTree.ParseText("<!DOCTYPE html><root />");
        var documentType = Assert.IsType<XmlDocumentTypeSyntax>(tree.GetRoot().Nodes[0]);

        var renamed = documentType.WithName("other");

        Assert.Equal("other", renamed.Name);
        Assert.Equal("<!DOCTYPE other>", renamed.ToFullString());
    }

    [Fact]
    public void DocumentType_WithName_KeepsTheExternalIdentifier()
    {
        var tree = XmlSyntaxTree.ParseText("<!DOCTYPE html PUBLIC \"-//W3C//DTD\"><root />");
        var documentType = Assert.IsType<XmlDocumentTypeSyntax>(tree.GetRoot().Nodes[0]);

        var renamed = documentType.WithName("other");

        Assert.Equal("other", renamed.Name);
        Assert.Equal("<!DOCTYPE other PUBLIC \"-//W3C//DTD\">", renamed.ToFullString());
    }

    [Fact]
    public void DocumentType_RenderedTextAlwaysCarriesTheName()
    {
        Assert.Equal("<!DOCTYPE html>", SyntaxFactory.XmlDocumentType("html").ToFullString());

        // A value that is only the part after the name gets the name put back in front of it.
        Assert.Equal("<!DOCTYPE html PUBLIC \"x\">", SyntaxFactory.XmlDocumentType("html", "PUBLIC \"x\"").ToFullString());

        // A value that is already the whole content, as the parser produces, is used as it stands.
        Assert.Equal("<!DOCTYPE html PUBLIC \"x\">", SyntaxFactory.XmlDocumentType("html", "html PUBLIC \"x\"").ToFullString());

        // A value merely starting with the same characters is not the name.
        Assert.Equal("<!DOCTYPE html htmlx>", SyntaxFactory.XmlDocumentType("html", "htmlx").ToFullString());
    }

    [Fact]
    public void DocumentType_RoundTripsThroughTheParser()
    {
        var documentType = SyntaxFactory.XmlDocumentType("html", "PUBLIC \"x\"");
        var reparsed = Assert.IsType<XmlDocumentTypeSyntax>(XmlSyntaxTree.ParseText(documentType.ToFullString()).GetRoot().Nodes[0]);

        Assert.Equal(documentType.Name, reparsed.Name);
        Assert.Equal(documentType.ToFullString(), reparsed.ToFullString());
    }

    public static TheoryData<string> RecoverySamples => new()
    {
        "", "plain text", "<root><a></root>", "</a>", "<a><!--x", "<1bad>", "<a><![CDATA[d",
        "<?xml version", "<?pi", "<!DOCTYPE html", "<a b>", "<a b=c>x</a>", "<a @@@>", "<a></a >",
        "<a></a junk>", "<a>&amp;</a>", "<a b='1' c=\"2\" />", "<a\n  b='1'\n/>", "<!DOCTYPE a [<!ENTITY x \"y\">]><a/>",
        "<a\u00B7b/>", "<\U00010330 x\u0300='1'/>", "<\u00AA/>", "</\U00010330>", "<?\U00010330 d?>", "<!DOCTYPE \U00010330>",
    };

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    [MemberData(nameof(RecoverySamples))]
    public void ParseText_ReproducesItsSourceExactly(string text)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());

        // Concatenating the tokens has to reproduce the source too: the round trip alone can hide a token that was
        // dropped and a node whose text was written twice.
        Assert.Equal(text, string.Concat(tree.GetRoot().DescendantTokens().Select(token => token.ToFullString())));
    }

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    [MemberData(nameof(RecoverySamples))]
    public void ParseText_ReproducesEveryPrefixAndEverySingleCharacterDeletion(string text)
    {
        for (var length = 0; length <= text.Length; length++)
        {
            var prefix = text[..length];
            Assert.Equal(prefix, XmlSyntaxTree.ParseText(prefix).GetRoot().ToFullString());
        }

        for (var index = 0; index < text.Length; index++)
        {
            var damaged = text.Remove(index, 1);
            Assert.Equal(damaged, XmlSyntaxTree.ParseText(damaged).GetRoot().ToFullString());
        }
    }

    /// <summary>
    /// Names follow the XML NameStartChar and NameChar productions rather than "is it a Unicode letter", which is a
    /// different set in both directions.
    /// </summary>
    [Theory]
    // The middle dot and the combining marks may appear in a name, though not start one.
    [InlineData("<a\u00B7b/>", "a\u00B7b")]
    [InlineData("<a\u0300/>", "a\u0300")]
    [InlineData("<a\u203F b/>", "a\u203F")]
    // A name may hold a character from outside the basic plane, written as a surrogate pair.
    [InlineData("<\U00010330/>", "\U00010330")]
    [InlineData("<a\U00010330b/>", "a\U00010330b")]
    // Ordinary names still work.
    [InlineData("<a:b-c.d/>", "a:b-c.d")]
    [InlineData("<_x/>", "_x")]
    public void ElementNamesFollowTheXmlNameProductions(string text, string expectedName)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        var element = Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().Nodes[0]);
        Assert.Equal(expectedName, element.Name);
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Theory]
    // A digit, the middle dot, and a combining mark may follow a name character but not begin one.
    [InlineData("<1bad/>")]
    [InlineData("<\u00B7bad/>")]
    [InlineData("<\u0300bad/>")]
    // These are Unicode letters that the XML productions leave out.
    [InlineData("<\u00AA/>")]
    [InlineData("<\u00B5/>")]
    public void AnElementNameThatCannotStartIsSkippedText(string text)
    {
        var tree = XmlSyntaxTree.ParseText(text);

        Assert.IsType<XmlSkippedTextSyntax>(tree.GetRoot().Nodes[0]);
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    /// <summary>
    /// A lone surrogate is not a scalar value, so it can be no part of a name. It is built here rather than passed
    /// as test data, which would replace it with U+FFFD -- a character XML does allow in a name.
    /// </summary>
    [Fact]
    public void AnElementNameCannotStartWithALoneSurrogate()
    {
        var text = "<" + (char)0xD800 + "/>";
        var tree = XmlSyntaxTree.ParseText(text);

        Assert.IsType<XmlSkippedTextSyntax>(tree.GetRoot().Nodes[0]);
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void AttributeNamesFollowTheXmlNameProductionsToo()
    {
        const string Text = "<root a\u00B7b='1' \U00010330='2' />";
        var tree = XmlSyntaxTree.ParseText(Text);

        var element = Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().Nodes[0]);
        Assert.Equal(["a\u00B7b", "\U00010330"], element.Attributes.Select(attribute => attribute.Name));
        Assert.Equal(Text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void WhitespaceIsTriviaInsideATagAndContentBetweenTags()
    {
        var tree = XmlSyntaxTree.ParseText("<root>\n  <book id = '1' />\n</root>");
        var book = Assert.IsType<XmlEmptyElementSyntax>(tree.GetRoot().SelectSingleSyntaxNode("//book"));

        // Inside the tag the space in front of a token is that token's own trivia.
        Assert.Equal(" ", book.Attributes[0].NameToken.LeadingTrivia.ToFullString());
        Assert.Equal(" ", book.Attributes[0].EqualsToken.LeadingTrivia.ToFullString());

        // Between tags it is character data, which XML says is significant.
        Assert.Equal("\n  ", Assert.IsType<XmlTextSyntax>(Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]).Content[0]).Text);
    }

    [Fact]
    public void ReplaceNode_KeepsEveryNodeItDidNotTouch()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a>1</a><b>2</b><c>3</c></root>");
        var root = Assert.IsType<XmlElementSyntax>(tree.GetRoot().Nodes[0]);
        var b = root.Content.OfType<XmlElementSyntax>().Single(element => element.Name == "b");

        var updated = tree.GetRoot().ReplaceNode(b, b.WithInnerText("two"));
        var updatedRoot = Assert.IsType<XmlElementSyntax>(updated.Nodes[0]);

        Assert.Equal("<root><a>1</a><b>two</b><c>3</c></root>", updated.ToFullString());
        Assert.True(root.Content[0].IsIncrementallyIdenticalTo(updatedRoot.Content[0]));
        Assert.True(root.Content[2].IsIncrementallyIdenticalTo(updatedRoot.Content[2]));
        Assert.False(root.Content[1].IsIncrementallyIdenticalTo(updatedRoot.Content[1]));
    }

    [Fact]
    public void ReplaceNode_ReturnsTheSameTreeWhenNothingChanged()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a>1</a></root>");
        var root = tree.GetRoot();
        var a = root.DescendantNodes().OfType<XmlElementSyntax>().Single(element => element.Name == "a");

        Assert.True(root.IsIncrementallyIdenticalTo(root.ReplaceNode(a, a)));
    }

    [Fact]
    public void Annotations_SurviveAnEditElsewhereInTheDocument()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a>1</a><b>2</b></root>");
        var root = tree.GetRoot();
        var a = root.DescendantNodes().OfType<XmlElementSyntax>().Single(element => element.Name == "a");
        var b = root.DescendantNodes().OfType<XmlElementSyntax>().Single(element => element.Name == "b");
        var marker = new SyntaxAnnotation();

        var marked = root.ReplaceNode(a, a.WithAdditionalAnnotations(marker));
        var edited = marked.ReplaceNode(marked.DescendantNodes().OfType<XmlElementSyntax>().Single(element => element.Name == "b"), b.WithInnerText("two"));

        Assert.Equal("<a>1</a>", edited.GetAnnotatedNodes(marker).Single().ToFullString());
        Assert.Equal("<root><a>1</a><b>two</b></root>", edited.ToFullString());
    }

    [Fact]
    public void Walker_VisitsEveryNodeAndTheVisitorVisitsOnlyOne()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a id='1'>x</a></root>");

        var walker = new NodeCounter();
        walker.Visit(tree.GetRoot());

        var visitor = new NodeCounter { AsVisitor = true };
        ((XmlSyntaxVisitor)visitor).Visit(tree.GetRoot());

        Assert.Equal(tree.GetRoot().DescendantNodesAndSelf().Count(), walker.Count);
        Assert.Equal(1, visitor.Count);
    }

    [Fact]
    public void FindToken_AndFindNode_LocateWhatCoversAPosition()
    {
        const string Text = "<root><a id='1'>x</a></root>";
        var tree = XmlSyntaxTree.ParseText(Text);

        Assert.Equal("id", tree.GetRoot().FindToken(Text.IndexOf("id", StringComparison.Ordinal)).Text);

        // FindNode returns the smallest node covering the span, which for the opening angle bracket is the start tag.
        Assert.Equal("<a id='1'>", tree.GetRoot().FindNode(new TextSpan(Text.IndexOf("<a", StringComparison.Ordinal), 2)).ToFullString());
        Assert.Equal("<a id='1'>x</a>", tree.GetRoot().FindNode(new TextSpan(Text.IndexOf("<a", StringComparison.Ordinal), "<a id='1'>x</a>".Length)).ToFullString());
    }

    [Fact]
    public void Rewriter_ReplacesEveryMatchingNodeAndKeepsTheRest()
    {
        var tree = XmlSyntaxTree.ParseText("<root><a>1</a><a>2</a><b>3</b></root>");

        var rewritten = new RenameElement("a", "z").Visit(tree.GetRoot());

        Assert.Equal("<root><z>1</z><z>2</z><b>3</b></root>", rewritten?.ToFullString());
    }

    private sealed class NodeCounter : XmlSyntaxWalker
    {
        public int Count { get; private set; }

        public bool AsVisitor { get; init; }

        public override void DefaultVisit(XmlSyntaxNode node)
        {
            Count++;
            if (!AsVisitor)
            {
                base.DefaultVisit(node);
            }
        }
    }

    private sealed class RenameElement(string from, string to) : XmlSyntaxRewriter
    {
        public override SyntaxNode? VisitElement(XmlElementSyntax node)
        {
            var visited = (XmlElementSyntax?)base.VisitElement(node);

            return visited?.Name == from ? visited.WithName(to) : visited;
        }
    }

    private static void AssertSpan(int expectedStart, int expectedLength, TextSpan actual)
    {
        Assert.Equal(expectedStart, actual.Start);
        Assert.Equal(expectedLength, actual.Length);
    }

    private static IEnumerable<XmlSyntaxNode> EnumerateNodes(XmlSyntaxNode root) => root.DescendantNodesAndSelf().Cast<XmlSyntaxNode>();

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
