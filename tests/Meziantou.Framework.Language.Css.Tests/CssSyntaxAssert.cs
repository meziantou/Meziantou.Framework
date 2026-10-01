using System.Text;

namespace Meziantou.Framework.Language.Css.Tests;

/// <summary>Checks that a parsed tree accounts for every character of its source, and dumps trees for comparison.</summary>
/// <remarks>
/// The root node keeps the original text, so <c>Root.ToFullString()</c> round-trips even when a child node dropped a
/// character or claims the wrong span. Rebuilding the text from the children, and comparing every node and token
/// against the slice of source its span points at, is what actually catches that.
/// </remarks>
internal static class CssSyntaxAssert
{
    public static CssSyntaxTree TextIsFaithful(string text, CssParseOptions? options = null)
    {
        var tree = CssSyntaxTree.ParseText(text, options);
        TextIsFaithful(text, tree);

        return tree;
    }

    public static void TextIsFaithful(string text, CssSyntaxTree tree)
    {
        var root = tree.GetRoot();
        Assert.Equal(text, root.ToFullString());
        Assert.Equal(text, root.Statements.ToFullString() + root.EndOfFileToken.ToFullString());
        Assert.Equal(text, string.Concat(root.DescendantTokens().Select(token => token.ToFullString())));

        foreach (var node in root.DescendantNodes())
        {
            var span = node.FullSpan;
            Assert.True(span.Start >= 0 && span.End <= text.Length, $"{node.Kind()} has span {span} outside a source of length {text.Length}.");
            Assert.Equal(text[span.Start..span.End], node.ToFullString());
        }

        foreach (var token in root.DescendantTokens())
        {
            var span = token.FullSpan;
            Assert.True(span.Start >= 0 && span.End <= text.Length, $"{token.Kind()} has span {span} outside a source of length {text.Length}.");
            Assert.Equal(text[span.Start..span.End], token.ToFullString());
        }

        foreach (var diagnostic in tree.GetDiagnostics())
        {
            var span = diagnostic.Location.SourceSpan;
            Assert.True(span.Start >= 0 && span.End <= text.Length, $"{diagnostic.Id} has span {span} outside a source of length {text.Length}.");
        }
    }

    /// <summary>Writes the structure of <paramref name="node"/> as <c>Kind[children]</c>, with each token as its text.</summary>
    public static string Dump(SyntaxNode? node)
    {
        if (node is null)
            return "null";

        var builder = new StringBuilder();
        Write(builder, node);
        return builder.ToString();

        static void Write(StringBuilder builder, SyntaxNode node)
        {
            builder.Append(node.Kind()).Append('[');
            var first = true;
            foreach (var child in node.ChildNodesAndTokens())
            {
                if (!first)
                {
                    builder.Append(' ');
                }

                first = false;
                if (child.AsNode(out var childNode))
                {
                    Write(builder, childNode);
                }
                else
                {
                    var token = child.AsToken();
                    builder.Append(token.IsMissing ? "<missing>" : token.Text);
                }
            }

            builder.Append(']');
        }
    }

    /// <summary>Writes the diagnostics of a tree as <c>ID@start:length</c>, in source order.</summary>
    public static string Diagnostics(CssSyntaxTree tree)
        => string.Join(' ', tree.GetDiagnostics().Select(diagnostic => $"{diagnostic.Id}@{diagnostic.Location.SourceSpan.Start}:{diagnostic.Location.SourceSpan.Length}"));
}
