namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class ShellSyntaxVisitorTests
{
    [Fact]
    public void Visitor_VisitsEveryNodeKind()
    {
        var tree = ShellSyntaxTree.ParseText("FOO=1 echo \"$BAR\" $(date) > out.txt", ShellDialect.Bash);
        var collector = new KindCollector();

        collector.Visit(tree.GetRoot());

        Assert.Contains(SyntaxKind.ShellScript, collector.Kinds);
        Assert.Contains(SyntaxKind.Command, collector.Kinds);
        Assert.Contains(SyntaxKind.Assignment, collector.Kinds);
        Assert.Contains(SyntaxKind.QuotedString, collector.Kinds);
        Assert.Contains(SyntaxKind.VariableReference, collector.Kinds);
        Assert.Contains(SyntaxKind.CommandSubstitution, collector.Kinds);
        Assert.Contains(SyntaxKind.Redirection, collector.Kinds);
    }

    [Fact]
    public void Visitor_WithResult_CountsCommands()
    {
        var tree = ShellSyntaxTree.ParseText("a | b && c; d", ShellDialect.Bash);

        Assert.Equal(4, new CommandCounter().Visit(tree.GetRoot()));
    }

    [Fact]
    public void Rewriter_ThatChangesNothing_ReturnsTheSameInstance()
    {
        var tree = ShellSyntaxTree.ParseText("echo hello | grep h", ShellDialect.Bash);

        Assert.Same(tree.GetRoot(), new ShellSyntaxRewriter().Visit(tree.GetRoot()));
    }

    [Fact]
    public void Rewriter_RenamingOneCommand_LeavesTheRestByteForByte()
    {
        const string Text = "# keep me\necho  hello   world > out.txt # and me\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        var rewritten = new CommandRenamer("echo", "printf").Visit(tree.GetRoot());

        var result = Assert.IsType<ShellScriptSyntax>(rewritten).ToFullString();
        Assert.Equal(Text.Replace("echo", "printf", StringComparison.Ordinal), result);
    }

    [Fact]
    public void Rewriter_RewritesInsideCommandSubstitutions()
    {
        var tree = ShellSyntaxTree.ParseText("x=$(echo inner)", ShellDialect.Bash);

        var rewritten = new CommandRenamer("echo", "printf").Visit(tree.GetRoot());

        Assert.Equal("x=$(printf inner)", Assert.IsType<ShellScriptSyntax>(rewritten).ToFullString());
    }

    [Fact]
    public void Rewriter_DocumentationExample()
    {
        var tree = ShellSyntaxTree.ParseText("ls -l | wc -l", ShellDialect.Bash);

        var rewritten = new RenameCommand().Visit(tree.GetRoot());

        Assert.Equal("dir -l | wc -l", rewritten?.ToFullString());
    }

    [Fact]
    public void Walker_DocumentationExample()
    {
        var tree = ShellSyntaxTree.ParseText("ls -l | wc -l && echo $(date)", ShellDialect.Bash);
        var counter = new SimpleCommandCounter();

        counter.Visit(tree.GetRoot());

        Assert.Equal(4, counter.Count);
    }

    [Fact]
    public void DescendantTokens_AreInSourceOrder()
    {
        var tree = ShellSyntaxTree.ParseText("echo $(a b) tail", ShellDialect.Bash);

        var starts = tree.GetRoot().DescendantTokens().Where(token => !token.IsMissing).Select(token => token.Span.Start).ToArray();

        Assert.Equal(starts.OrderBy(start => start), starts);
    }

    [Fact]
    public void DescendantNodesAndTokens_AreInSourceOrder()
    {
        var tree = ShellSyntaxTree.ParseText("a | b > c", ShellDialect.Bash);

        var starts = tree.GetRoot().DescendantNodesAndTokens().Select(item => item.FullSpan.Start).ToArray();

        Assert.Equal(starts.OrderBy(start => start), starts);
    }

    [Fact]
    public void DescendantNodes_DocumentationExample()
    {
        #region ShellSyntaxNode_DescendantNodes
        var tree = ShellSyntaxTree.ParseText("ls -l", ShellDialect.Bash);

        var kinds = new List<SyntaxKind>();
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            kinds.Add(node.Kind());
        }

        Assert.Equal([SyntaxKind.StatementList, SyntaxKind.Command, SyntaxKind.Word, SyntaxKind.LiteralWordPart, SyntaxKind.Word, SyntaxKind.LiteralWordPart], kinds);
        #endregion
    }

    [Fact]
    public void SyntaxKind_DocumentationExample()
    {
        #region SyntaxKind_CompareKind
        var tree = ShellSyntaxTree.ParseText("ls -l | wc -l && echo done", ShellDialect.Bash);

        var commandCount = 0;
        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node.Kind() == SyntaxKind.Command)
            {
                // handle a simple command
                commandCount++;
            }
        }

        Assert.Equal(3, commandCount);
        #endregion
    }

    [Fact]
    public void AncestorsAndSelf_WalksUpToTheRoot()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi", ShellDialect.Bash);
        var literal = tree.GetRoot().DescendantNodes().OfType<ShellLiteralWordPartSyntax>().First();

        Assert.Contains(tree.GetRoot(), literal.Ancestors());
        Assert.Same(literal, literal.AncestorsAndSelf().First());
    }

    private sealed class KindCollector : ShellSyntaxWalker
    {
        public List<SyntaxKind> Kinds { get; } = [];

        public override void DefaultVisit(ShellSyntaxNode node)
        {
            Kinds.Add(node.Kind());
            base.DefaultVisit(node);
        }
    }

    private sealed class CommandCounter : ShellSyntaxVisitor<int>
    {
        public override int VisitCommand(ShellCommandSyntax node) => 1 + DefaultVisit(node);

        public override int DefaultVisit(ShellSyntaxNode node)
        {
            var total = 0;
            foreach (var child in node.ChildNodes())
            {
                total += Visit((ShellSyntaxNode)child);
            }

            return total;
        }
    }

    private sealed class CommandRenamer(string oldName, string newName) : ShellSyntaxRewriter
    {
        public override SyntaxNode? VisitCommand(ShellCommandSyntax node)
        {
            if (node.NameValue != oldName || node.Name is null)
                return base.VisitCommand(node);

            var renamed = SyntaxFactory.Word(SyntaxFactory.ShellLiteralWordPart(
                SyntaxFactory.Token(SyntaxKind.BareTextToken, newName).WithTriviaFrom(node.Name.Parts.OfType<ShellLiteralWordPartSyntax>().First().TextToken)));

            var elements = node.ChildNodes().Select(child => ReferenceEquals(child, node.Name) ? renamed : child);

            return node.WithElements(new SyntaxList<ShellSyntaxNode>(elements.Cast<ShellSyntaxNode>()));
        }
    }

    #region ShellSyntaxRewriter_RenameCommand
    private sealed class RenameCommand : ShellSyntaxRewriter
    {
        public override SyntaxNode? VisitCommand(ShellCommandSyntax node)
        {
            if (node.Name is { } name && node.NameValue == "ls")
                return node.ReplaceNode(name, name.WithText("dir"));

            return base.VisitCommand(node);
        }
    }
    #endregion

    #region ShellSyntaxWalker_SimpleCommandCounter
    private sealed class SimpleCommandCounter : ShellSyntaxWalker
    {
        public int Count { get; private set; }

        public override void VisitCommand(ShellCommandSyntax node)
        {
            Count++;
            base.VisitCommand(node);
        }
    }
    #endregion

    [Fact]
    public void Rewriter_UsingWithText_KeepsLeadingCommentsAndIndentation()
    {
        const string Text = "# header\n  echo old | grep x\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        var rewritten = new WordRenamer("echo", "printf").Visit(tree.GetRoot());

        Assert.Equal("# header\n  printf old | grep x\n", Assert.IsType<ShellScriptSyntax>(rewritten).ToFullString());
    }

    [Fact]
    public void WithText_ReplacesTheWordAndKeepsItsLeadingTrivia()
    {
        var tree = ShellSyntaxTree.ParseText("echo   'quoted value'", ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[0]);

        Assert.Equal("   plain", command.Arguments[0].WithText("plain").ToFullString());
    }

    private sealed class WordRenamer(string oldName, string newName) : ShellSyntaxRewriter
    {
        public override SyntaxNode? VisitCommand(ShellCommandSyntax node)
        {
            if (node.NameValue != oldName || node.Name is null)
                return base.VisitCommand(node);

            var renamed = node.Name.WithText(newName);

            return node.WithElements(new SyntaxList<ShellSyntaxNode>(node.Elements.Select(child => ReferenceEquals(child, node.Name) ? (ShellSyntaxNode)renamed : child)));
        }
    }
}
