namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// The rewriter descends into every node type, not just the shared ones. Each case here rewrites a command that sits
/// inside a dialect-specific construct, which is where a rewriter that only understood the shared node set would
/// silently do nothing.
/// </summary>
public sealed class ShellSyntaxRewriterDescentTests
{
    private static string Rewrite(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxTree.ParseText(text, dialect);
        var rewritten = new CommandRenamer("echo", "printf").Visit(tree.Root);

        return Assert.IsType<ShellScriptSyntax>(rewritten).ToFullString();
    }

    [Theory]
    [InlineData("if true; then echo a; fi\n", "if true; then printf a; fi\n")]
    [InlineData("while x; do echo a; done\n", "while x; do printf a; done\n")]
    [InlineData("until x; do echo a; done\n", "until x; do printf a; done\n")]
    [InlineData("for f in a; do echo $f; done\n", "for f in a; do printf $f; done\n")]
    [InlineData("case $x in a) echo hi;; esac\n", "case $x in a) printf hi;; esac\n")]
    [InlineData("f() { echo a; }\n", "f() { printf a; }\n")]
    [InlineData("if a; then b; elif c; then echo d; fi\n", "if a; then b; elif c; then printf d; fi\n")]
    [InlineData("if a; then b; else echo c; fi\n", "if a; then b; else printf c; fi\n")]
    [InlineData("( echo a )\n", "( printf a )\n")]
    [InlineData("{ echo a; }\n", "{ printf a; }\n")]
    [InlineData("time echo a\n", "time printf a\n")]
    public void Posix_RewritesInsideEveryCompound(string text, string expected)
    {
        Assert.Equal(expected, Rewrite(text, ShellDialect.Bash));
    }

    [Theory]
    [InlineData("foreach f (a)\necho $f\nend\n", "foreach f (a)\nprintf $f\nend\n")]
    [InlineData("for f (a) echo $f\n", "for f (a) printf $f\n")]
    [InlineData("repeat 3 echo hi\n", "repeat 3 printf hi\n")]
    [InlineData("repeat 3 do echo hi; done\n", "repeat 3 do printf hi; done\n")]
    [InlineData("{ echo a } always { echo b }\n", "{ printf a } always { printf b }\n")]
    [InlineData("() { echo a }\n", "() { printf a }\n")]
    public void Zsh_RewritesInsideEveryCompound(string text, string expected)
    {
        Assert.Equal(expected, Rewrite(text, ShellDialect.Zsh));
    }

    [Theory]
    [InlineData("if ($a) { echo x }\n", "if ($a) { printf x }\n")]
    [InlineData("if ($a) { b } else { echo c }\n", "if ($a) { b } else { printf c }\n")]
    [InlineData("while ($a) { echo x }\n", "while ($a) { printf x }\n")]
    [InlineData("do { echo x } while ($a)\n", "do { printf x } while ($a)\n")]
    [InlineData("for ($i = 0; $i -lt 1; $i++) { echo x }\n", "for ($i = 0; $i -lt 1; $i++) { printf x }\n")]
    [InlineData("foreach ($i in $x) { echo $i }\n", "foreach ($i in $x) { printf $i }\n")]
    [InlineData("switch ($x) { 1 { echo a } }\n", "switch ($x) { 1 { printf a } }\n")]
    [InlineData("try { echo a } catch { echo b } finally { echo c }\n", "try { printf a } catch { printf b } finally { printf c }\n")]
    [InlineData("function f { echo x }\n", "function f { printf x }\n")]
    [InlineData("trap { echo x }\n", "trap { printf x }\n")]
    [InlineData("begin { echo x }\n", "begin { printf x }\n")]
    public void PowerShell_RewritesInsideEveryCompound(string text, string expected)
    {
        Assert.Equal(expected, Rewrite(text, ShellDialect.PowerShellCore));
    }

    [Theory]
    [InlineData("if exist a echo hi\r\n", "if exist a printf hi\r\n")]
    [InlineData("if exist a (echo y) else (echo n)\r\n", "if exist a (printf y) else (printf n)\r\n")]
    [InlineData("for %%i in (a) do echo %%i\r\n", "for %%i in (a) do printf %%i\r\n")]
    [InlineData("(\r\necho a\r\n)\r\n", "(\r\nprintf a\r\n)\r\n")]
    [InlineData("call echo hi\r\n", "call printf hi\r\n")]
    public void Cmd_RewritesInsideEveryCompound(string text, string expected)
    {
        Assert.Equal(expected, Rewrite(text, ShellDialect.Cmd));
    }

    [Fact]
    public void RewritingNothing_ReturnsTheSameInstance()
    {
        var tree = ShellSyntaxTree.ParseText("if true; then echo a; fi\n", ShellDialect.Bash);

        Assert.Same(tree.Root, new ShellSyntaxRewriter().Visit(tree.Root));
    }

    [Fact]
    public void RewritingKeepsTriviaAndUntouchedText()
    {
        const string Text = "# header\nif true; then\n  echo   a  # trailing\nfi\n";

        Assert.Equal(
            "# header\nif true; then\n  printf   a  # trailing\nfi\n",
            Rewrite(Text, ShellDialect.Bash));
    }

    [Fact]
    public void RewritingEveryMatchInADeeplyNestedScript()
    {
        const string Text = "for f in a; do\n  if x; then\n    case $f in y) echo deep;; esac\n  fi\ndone\n";

        Assert.Equal(
            "for f in a; do\n  if x; then\n    case $f in y) printf deep;; esac\n  fi\ndone\n",
            Rewrite(Text, ShellDialect.Bash));
    }

    [Fact]
    public void ReplacingAnOuterNodeSkipsItsChildren()
    {
        // The whole if-statement is replaced, so the `echo` inside it must not also be rewritten.
        var tree = ShellSyntaxTree.ParseText("if true; then echo a; fi\n", ShellDialect.Bash);
        var rewritten = new StatementReplacer().Visit(tree.Root);

        Assert.Equal("replaced\n", Assert.IsType<ShellScriptSyntax>(rewritten).ToFullString());
    }

    private sealed class CommandRenamer(string oldName, string newName) : ShellSyntaxRewriter
    {
        public override ShellSyntaxNode? VisitCommand(ShellCommandSyntax node)
        {
            if (node.NameValue != oldName || node.Name is null)
                return base.VisitCommand(node);

            var renamed = node.Name.WithText(newName);

            return node.WithChildNodes(node.ChildNodes.Select(child => ReferenceEquals(child, node.Name) ? renamed : child));
        }
    }

    private sealed class StatementReplacer : ShellSyntaxRewriter
    {
        public override ShellSyntaxNode? VisitIfStatement(PosixIfStatementSyntax node)
            => SyntaxFactory.Command(ShellDialect.Bash, "replaced");
    }

    [Fact]
    public void RewritingASubtreeReturnsTheReplacementForThatSubtree()
    {
        var tree = ShellSyntaxTree.ParseText("if true; then echo a; fi\necho b\n", ShellDialect.Bash);
        var ifStatement = tree.Root.Statements.Statements[0];

        var rewritten = new CommandRenamer("echo", "printf").Visit(ifStatement);

        Assert.NotNull(rewritten);
        Assert.Equal(ShellSyntaxKind.PosixIfStatement, rewritten.Kind);
        Assert.Equal("if true; then printf a; fi", rewritten.ToFullString());
    }

    [Fact]
    public void RewritingASubtreeLeavesItsSiblingsAlone()
    {
        var tree = ShellSyntaxTree.ParseText("echo a\necho b\n", ShellDialect.Bash);
        var first = tree.Root.Statements.Statements[0];

        var rewritten = new CommandRenamer("echo", "printf").Visit(first);

        // Only the visited subtree is returned; the sibling is not part of it.
        Assert.Equal("printf a", rewritten!.ToFullString());
    }

    [Fact]
    public void RewritingASubtreeWithNoChangeReturnsTheSameInstance()
    {
        var tree = ShellSyntaxTree.ParseText("if true; then ls; fi\n", ShellDialect.Bash);
        var ifStatement = tree.Root.Statements.Statements[0];

        Assert.Same(ifStatement, new CommandRenamer("echo", "printf").Visit(ifStatement));
    }

    [Fact]
    public void RewritingADetachedNodeReturnsItUnchanged()
    {
        // A node built by the factory has no script to splice into.
        var command = SyntaxFactory.Command(ShellDialect.Bash, "echo", "a");

        Assert.Same(command, new CommandRenamer("nothing", "x").Visit(command));
    }
}
