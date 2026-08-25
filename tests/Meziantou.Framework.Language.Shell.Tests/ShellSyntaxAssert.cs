namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// Checks that a parsed tree accounts for every character of its source.
/// </summary>
/// <remarks>
/// The root node keeps the original text, so <c>Root.ToFullString()</c> round-trips even when a child node dropped a
/// character or claims the wrong span. Rebuilding the text from the children, and comparing every node and token
/// against the slice of source its span points at, is what actually catches that.
/// </remarks>
internal static class ShellSyntaxAssert
{
    public static ShellSyntaxTree TextIsFaithful(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxTree.ParseText(text, dialect);
        TextIsFaithful(text, tree);

        return tree;
    }

    public static void TextIsFaithful(string text, ShellSyntaxTree tree)
    {
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Equal(text, tree.Root.Statements.ToFullString() + tree.Root.EndOfFileToken.ToFullString());

        foreach (var node in tree.Root.DescendantNodes())
        {
            var span = node.FullSpan;
            Assert.True(span.Start >= 0 && span.End <= text.Length, $"{node.Kind} has span {span} outside a source of length {text.Length}.");
            Assert.Equal(text[span.Start..span.End], node.ToFullString());
        }

        foreach (var token in tree.Root.DescendantTokens())
        {
            var span = token.FullSpan;
            Assert.True(span.Start >= 0 && span.End <= text.Length, $"{token.Kind} has span {span} outside a source of length {text.Length}.");
            Assert.Equal(text[span.Start..span.End], token.ToFullString());
        }
    }
}
