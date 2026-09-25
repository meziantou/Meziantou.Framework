using Meziantou.Framework.Language.Toml;
using Meziantou.Framework.Language;

namespace Meziantou.Framework.Language.Toml.Tests;

public sealed class TomlSyntaxTreeTests
{
    [Fact]
    public void ParseDocumentPreservesTomlText()
    {
        const string Text = "title = \"TOML Example\"\n[owner]\nname = \"Tom\"\nports = [8000, 8001]\n# comment\n";
        var tree = TomlSyntaxTree.ParseText(Text);

        Assert.Equal(Text, tree.GetText().ToString());
        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.Equal(4, tree.GetRoot().Entries.Count);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ParseArrayOfTablesAndInlineValues()
    {
        const string Text = "[[products]]\nname = 'Hammer'\ndetails = { color = \"gray\", weight = 1 }\n";
        var tree = TomlSyntaxTree.ParseText(Text);

        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.IsType<TomlTableSyntax>(tree.GetRoot().Entries[0]);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ParseQuotedKeysAndValuesWithEscapedBackslashes()
    {
        const string Text = "\"site\" . \"google.com\" = \"C:\\\\\"\nliteral = 'C:\\\\'\n";
        var tree = TomlSyntaxTree.ParseText(Text);

        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ReportsMalformedValues()
    {
        var tree = TomlSyntaxTree.ParseText("answer = definitely-not-a-value\n");

        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Fact]
    public void PreservesCommentsInsideArrays()
    {
        const string Text = "values = [1, # keep this\n 2]\n";
        var tree = TomlSyntaxTree.ParseText(Text);

        Assert.Equal(Text, tree.GetRoot().ToFullString());
        var comment = Assert.Single(tree.GetRoot().DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.CommentTrivia);
        Assert.Equal("# keep this", comment.ToString());
        Assert.Empty(tree.GetDiagnostics());

        var walker = new CommentWalker();
        walker.Visit(tree.GetRoot());
        Assert.Equal("# keep this", Assert.Single(walker.Comments).ToString());
    }

    [Fact]
    public void FactoryCreatesArrayOfTables()
    {
        var table = SyntaxFactory.TomlArrayOfTables("products");

        Assert.Equal("[[products]]", table.ToFullString());
    }

    [Theory]
    [InlineData("[[products]")]
    [InlineData("[products]]")]
    public void ReportsMismatchedTableDelimiters(string text)
    {
        Assert.NotEmpty(TomlSyntaxTree.ParseText(text).GetDiagnostics());
    }

    [Theory]
    [InlineData("invalid key = 1")]
    [InlineData("invalid@key = 1")]
    public void ReportsInvalidBareKeys(string text)
    {
        Assert.NotEmpty(TomlSyntaxTree.ParseText(text).GetDiagnostics());
    }

    [Fact]
    public void AllowsWhitespaceAroundDottedKeySeparators()
    {
        Assert.Empty(TomlSyntaxTree.ParseText("invalid . key = 1").GetDiagnostics());
    }

    [Fact]
    public void ArrayContentsExposeNestedArraysAsNodes()
    {
        var property = Assert.IsType<TomlPropertySyntax>(Assert.Single(TomlSyntaxTree.ParseText("values = [[1]]").GetRoot().Entries));
        var array = Assert.IsType<TomlArraySyntax>(property.ValueNode.AsNode());

        var nested = Assert.Single(array.Contents, item => item.IsNode);
        Assert.IsType<TomlArraySyntax>(nested.AsNode());
    }

    [Fact]
    public void PropertyValueExcludesOuterTrivia()
    {
        var property = Assert.IsType<TomlPropertySyntax>(Assert.Single(TomlSyntaxTree.ParseText("value =  42 # comment").GetRoot().Entries));

        Assert.Equal("42", property.Value);
        Assert.Equal("value =  43 # comment", property.WithValue("43").ToFullString());
    }

    [Fact]
    public void WithValuePreservesOuterTrivia()
    {
        var property = Assert.IsType<TomlPropertySyntax>(Assert.Single(TomlSyntaxTree.ParseText("value =  [1] # comment").GetRoot().Entries));

        Assert.Equal("[1]", property.Value);
        Assert.Equal("value =  2 # comment", property.WithValue("2").ToFullString());
    }

    [Fact]
    public void RewriterVisitsNestedArraysAndRetainsRequiredArray()
    {
        var property = Assert.IsType<TomlPropertySyntax>(Assert.Single(TomlSyntaxTree.ParseText("value = [[1]]").GetRoot().Entries));
        var rewriter = new NullArrayRewriter();

        var rewritten = Assert.IsType<TomlPropertySyntax>(rewriter.VisitTomlProperty(property));

        Assert.Equal("value = [[1]]", rewritten.ToFullString());
        Assert.Equal(2, rewriter.ArrayCount);
    }

    private sealed class NullArrayRewriter : TomlSyntaxRewriter
    {
        public int ArrayCount { get; private set; }

        public override SyntaxNode? VisitTomlArray(TomlArraySyntax node)
        {
            ArrayCount++;
            base.VisitTomlArray(node);
            return node.Parent is TomlPropertySyntax ? null : node;
        }
    }

    private sealed class CommentWalker : TomlSyntaxWalker
    {
        public List<SyntaxTrivia> Comments { get; } = [];

        public CommentWalker()
            : base(SyntaxWalkerDepth.Trivia)
        {
        }

        public override void VisitTrivia(SyntaxTrivia trivia)
        {
            if (trivia.Kind() == SyntaxKind.CommentTrivia)
                Comments.Add(trivia);
        }
    }
}
