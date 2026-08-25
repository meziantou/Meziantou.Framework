namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class ShellSyntaxVisitorTests
{
    [Fact]
    public void Visitor_VisitsEveryNodeKind()
    {
        var tree = ShellSyntaxTree.ParseText("FOO=1 echo \"$BAR\" $(date) > out.txt", ShellDialect.Bash);
        var collector = new KindCollector();

        collector.Visit(tree.Root);

        Assert.Contains(ShellSyntaxKind.ShellScript, collector.Kinds);
        Assert.Contains(ShellSyntaxKind.Command, collector.Kinds);
        Assert.Contains(ShellSyntaxKind.Assignment, collector.Kinds);
        Assert.Contains(ShellSyntaxKind.QuotedString, collector.Kinds);
        Assert.Contains(ShellSyntaxKind.VariableReference, collector.Kinds);
        Assert.Contains(ShellSyntaxKind.CommandSubstitution, collector.Kinds);
        Assert.Contains(ShellSyntaxKind.Redirection, collector.Kinds);
    }

    [Fact]
    public void Visitor_WithResult_CountsCommands()
    {
        var tree = ShellSyntaxTree.ParseText("a | b && c; d", ShellDialect.Bash);

        Assert.Equal(4, new CommandCounter().Visit(tree.Root));
    }

    [Fact]
    public void Rewriter_ThatChangesNothing_ReturnsTheSameInstance()
    {
        var tree = ShellSyntaxTree.ParseText("echo hello | grep h", ShellDialect.Bash);

        Assert.Same(tree.Root, new ShellSyntaxRewriter().Visit(tree.Root));
    }

    [Fact]
    public void Rewriter_RenamingOneCommand_LeavesTheRestByteForByte()
    {
        const string Text = "# keep me\necho  hello   world > out.txt # and me\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        var rewritten = new CommandRenamer("echo", "printf").Visit(tree.Root);

        var result = Assert.IsType<ShellScriptSyntax>(rewritten).ToFullString();
        Assert.Equal(Text.Replace("echo", "printf", StringComparison.Ordinal), result);
    }

    [Fact]
    public void Rewriter_RewritesInsideCommandSubstitutions()
    {
        var tree = ShellSyntaxTree.ParseText("x=$(echo inner)", ShellDialect.Bash);

        var rewritten = new CommandRenamer("echo", "printf").Visit(tree.Root);

        Assert.Equal("x=$(printf inner)", Assert.IsType<ShellScriptSyntax>(rewritten).ToFullString());
    }

    [Fact]
    public void DescendantTokens_AreInSourceOrder()
    {
        var tree = ShellSyntaxTree.ParseText("echo $(a b) tail", ShellDialect.Bash);

        var starts = tree.Root.DescendantTokens().Where(token => !token.IsMissing).Select(token => token.Span.Start).ToArray();

        Assert.Equal(starts.OrderBy(start => start), starts);
    }

    [Fact]
    public void DescendantNodesAndTokens_AreInSourceOrder()
    {
        var tree = ShellSyntaxTree.ParseText("a | b > c", ShellDialect.Bash);

        var starts = tree.Root.DescendantNodesAndTokens().Select(item => item.FullSpan.Start).ToArray();

        Assert.Equal(starts.OrderBy(start => start), starts);
    }

    [Fact]
    public void AncestorsAndSelf_WalksUpToTheRoot()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi", ShellDialect.Bash);
        var literal = tree.Root.DescendantNodes().OfType<ShellLiteralWordPartSyntax>().First();

        Assert.Contains(tree.Root, literal.Ancestors());
        Assert.Same(literal, literal.AncestorsAndSelf().First());
    }

    private sealed class KindCollector : ShellSyntaxVisitor
    {
        public List<ShellSyntaxKind> Kinds { get; } = [];

        protected override void DefaultVisit(ShellSyntaxNode node)
        {
            Kinds.Add(node.Kind);
            base.DefaultVisit(node);
        }
    }

    private sealed class CommandCounter : ShellSyntaxVisitor<int>
    {
        public override int VisitCommand(ShellCommandSyntax node) => 1 + DefaultVisit(node);

        protected override int DefaultVisit(ShellSyntaxNode node)
        {
            var total = 0;
            foreach (var child in node.ChildNodes)
            {
                total += Visit(child);
            }

            return total;
        }
    }

    private sealed class CommandRenamer(string oldName, string newName) : ShellSyntaxRewriter
    {
        public override ShellSyntaxNode? VisitCommand(ShellCommandSyntax node)
        {
            if (node.NameValue != oldName || node.Name is null)
                return base.VisitCommand(node);

            var renamed = new ShellWordSyntax([new ShellLiteralWordPartSyntax(
                node.Name.Parts.OfType<ShellLiteralWordPartSyntax>().First().TextToken.WithText(newName))]);

            var elements = node.ChildNodes.Select(child => ReferenceEquals(child, node.Name) ? renamed : child);

            return node.WithChildNodes(elements);
        }
    }

    [Fact]
    public void Rewriter_UsingWithText_KeepsLeadingCommentsAndIndentation()
    {
        const string Text = "# header\n  echo old | grep x\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        var rewritten = new WordRenamer("echo", "printf").Visit(tree.Root);

        Assert.Equal("# header\n  printf old | grep x\n", Assert.IsType<ShellScriptSyntax>(rewritten).ToFullString());
    }

    [Fact]
    public void WithText_ReplacesTheWordAndKeepsItsLeadingTrivia()
    {
        var tree = ShellSyntaxTree.ParseText("echo   'quoted value'", ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.Root.Statements.Statements[0]);

        Assert.Equal("   plain", command.Arguments[0].WithText("plain").ToFullString());
    }

    private sealed class WordRenamer(string oldName, string newName) : ShellSyntaxRewriter
    {
        public override ShellSyntaxNode? VisitCommand(ShellCommandSyntax node)
        {
            if (node.NameValue != oldName || node.Name is null)
                return base.VisitCommand(node);

            var renamed = node.Name.WithText(newName);

            return node.WithChildNodes(node.ChildNodes.Select(child => ReferenceEquals(child, node.Name) ? renamed : child));
        }
    }
}
