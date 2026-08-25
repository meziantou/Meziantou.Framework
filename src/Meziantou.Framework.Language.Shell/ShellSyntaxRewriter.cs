namespace Meziantou.Framework.Language.Shell;

/// <summary>Visitor that produces a rewritten shell syntax tree.</summary>
/// <remarks>
/// <para>
/// Override any <c>Visit</c> method and return a different node to replace it. Returning the node unchanged, which is
/// what the base implementations do, means "leave this alone and keep looking inside it", so every node type is
/// descended into regardless of dialect.
/// </para>
/// <para>
/// A replaced node is spliced into the source text and the script is reparsed once at the end, the same mechanism
/// <see cref="ShellSyntaxNode.ReplaceNode"/> uses. That is also why the rewrite is driven from the script root:
/// <c>Visit(tree.Root)</c> returns a rewritten <see cref="ShellScriptSyntax"/>. Visiting a node further down returns
/// whatever the overrides produce for that node, without rebuilding, because a node cannot be reconstructed from
/// text outside the context it was parsed in.
/// </para>
/// <para>
/// As with <see cref="ShellSyntaxNode.ReplaceNode"/>, a replacement that carries no leading trivia of its own keeps
/// the whitespace and comments in front of the node it replaces.
/// </para>
/// </remarks>
public class ShellSyntaxRewriter : ShellSyntaxVisitor<ShellSyntaxNode?>
{
    private bool _isWalking;

    public override ShellSyntaxNode? Visit(ShellSyntaxNode? node)
    {
        if (node is null)
            return null;

        // Inner dispatch during a walk: just ask the overrides what to do with this node.
        if (_isWalking)
            return node.Accept(this);

        _isWalking = true;
        try
        {
            var replaced = node.Accept(this);
            if (replaced is not null && !ReferenceEquals(replaced, node))
                return replaced;

            if (node is not ShellScriptSyntax script)
                return node;

            var edits = new List<TextEdit>();
            foreach (var child in script.ChildNodes)
            {
                CollectEdits(child, edits);
            }

            return edits.Count == 0 ? script : Rebuild(script, edits);
        }
        finally
        {
            _isWalking = false;
        }
    }

    /// <summary>Returns <paramref name="node"/> unchanged, so the walk keeps descending into it.</summary>
    protected override ShellSyntaxNode? DefaultVisit(ShellSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node;
    }

    protected virtual ShellSyntaxNode? VisitCore(ShellSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Visit(node);
    }

    /// <summary>
    /// Asks the overrides about <paramref name="node"/>. A node that was replaced is recorded and not descended into,
    /// since its replacement stands for the whole subtree.
    /// </summary>
    private void CollectEdits(ShellSyntaxNode node, List<TextEdit> edits)
    {
        var updated = VisitCore(node);
        if (updated is not null && !ReferenceEquals(updated, node))
        {
            // Keep the trivia in front of the node when the replacement brings none of its own.
            var span = HasLeadingTrivia(updated) ? node.FullSpan : node.Span;
            edits.Add(new TextEdit(span, updated.ToFullString()));

            return;
        }

        foreach (var child in node.ChildNodes)
        {
            CollectEdits(child, edits);
        }
    }

    private static ShellScriptSyntax Rebuild(ShellScriptSyntax script, List<TextEdit> edits)
    {
        var source = script.ToFullString();
        var builder = new StringBuilder(source.Length);
        var position = 0;

        foreach (var edit in edits.OrderBy(edit => edit.Span.Start))
        {
            // The walk never descends into a replaced node, so overlaps only happen if an override rewrote a node
            // and one of its ancestors. Keeping the outer edit is the safe reading.
            if (edit.Span.Start < position || edit.Span.End > source.Length)
                continue;

            builder.Append(source, position, edit.Span.Start - position);
            builder.Append(edit.Text);
            position = edit.Span.End;
        }

        builder.Append(source, position, source.Length - position);

        var options = script.SyntaxTree?.Options ?? new ShellParseOptions(script.Dialect ?? ShellDialect.Bash);

        return ShellSyntaxTree.ParseText(builder.ToString(), options).Root;
    }

    private static bool HasLeadingTrivia(ShellSyntaxNode node)
    {
        foreach (var token in node.DescendantTokens())
        {
            return token.LeadingTrivia.Count > 0;
        }

        return false;
    }

    private readonly record struct TextEdit(TextSpan Span, string Text);
}
