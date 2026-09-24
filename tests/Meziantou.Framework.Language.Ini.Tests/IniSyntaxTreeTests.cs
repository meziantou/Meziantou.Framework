namespace Meziantou.Framework.Language.Ini.Tests;

public sealed class IniSyntaxTreeTests
{
    public static TheoryData<string> RoundTripSamples => new()
    {
        "name=value",
        """
; leading comment
[database]
server=localhost
port: 5432

# another comment
[features]
enabled=true
empty=
""",
        """
[invalid
key without separator
]=value
""",
    };

    [Theory]
    [MemberData(nameof(RoundTripSamples))]
    public void Parse_Save_RoundTripsSamples(string text)
    {
        var tree = IniSyntaxTree.ParseText(text);

        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void ParseText_BuildsIniTree()
    {
        const string Text = """
[database]
server=localhost
port: 5432
""";

        var tree = IniSyntaxTree.ParseText(Text);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Collection(
            tree.GetRoot().Entries,
            entry =>
            {
                var section = Assert.IsType<IniSectionSyntax>(entry);
                Assert.Equal("database", section.Name);
            },
            entry =>
            {
                var property = Assert.IsType<IniPropertySyntax>(entry);
                Assert.Equal("server", property.Key);
                Assert.Equal("localhost", property.Value);
                Assert.Equal(SyntaxKind.EqualsToken, property.SeparatorToken.Kind());
            },
            entry =>
            {
                var property = Assert.IsType<IniPropertySyntax>(entry);
                Assert.Equal("port", property.Key);
                Assert.Equal("5432", property.Value);
                Assert.Equal(SyntaxKind.ColonToken, property.SeparatorToken.Kind());
                Assert.Equal(" ", property.ValueToken.LeadingTrivia.ToFullString());
            });
    }

    [Fact]
    public void ParseText_CommentsAreTriviaWithSourceLocations()
    {
        const string Text = """
; comment
[section]
key=value
""";

        var tree = IniSyntaxTree.ParseText(Text);
        var comment = Assert.Single(tree.GetRoot().DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.CommentTrivia);
        var section = Assert.IsType<IniSectionSyntax>(tree.GetRoot().Entries[0]);

        Assert.Equal("; comment", comment.ToString());
        Assert.Equal(Text.IndexOf(';', StringComparison.Ordinal), comment.Span.Start);
        Assert.Equal(Text.IndexOf('[', StringComparison.Ordinal), section.Span.Start);
    }

    [Fact]
    public void ParseText_InvalidIni_DoesNotThrowAndKeepsSkippedText()
    {
        const string Text = """
[missing
key without separator
]=value
""";

        var exception = Record.Exception(() => IniSyntaxTree.ParseText(Text));
        var tree = IniSyntaxTree.ParseText(Text);

        Assert.Null(exception);
        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.True(tree.GetRoot().ContainsSkippedText);
        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.All(tree.GetDiagnostics(), diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void ReplaceNode_ReplacesExactInstance_WhenNodeTextIsDuplicated()
    {
        var tree = IniSyntaxTree.ParseText("a=1\na=1");
        var properties = tree.GetRoot().Entries.OfType<IniPropertySyntax>().ToArray();
        IniDocumentSyntax updated = tree.GetRoot().ReplaceNode(properties[1], SyntaxFactory.IniProperty("a", "2"));

        Assert.Equal("a=1\na=2", updated.ToFullString());
    }

    [Fact]
    public void Rewriter_CanUpdatePropertyValues()
    {
        var tree = IniSyntaxTree.ParseText("name=old");
        var rewriter = new RenameValueRewriter();

        var updated = Assert.IsType<IniDocumentSyntax>(rewriter.Visit(tree.GetRoot()));

        Assert.Equal("name=new", updated.ToFullString());
    }

    private sealed class RenameValueRewriter : IniSyntaxRewriter
    {
        public override SyntaxNode? VisitIniProperty(IniPropertySyntax node)
        {
            if (node.Key == "name")
                return node.WithValue("new");

            return base.VisitIniProperty(node);
        }
    }
}
