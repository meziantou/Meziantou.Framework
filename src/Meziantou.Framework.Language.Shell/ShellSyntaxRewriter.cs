namespace Meziantou.Framework.Language.Shell;

/// <summary>Visitor that produces a rewritten shell syntax tree.</summary>
/// <remarks>
/// <para>
/// Override any <c>Visit</c> method and return a different node to replace it. Returning the node unchanged, which is
/// what the base implementations do, means "leave this alone and keep looking inside it", so every node type is
/// descended into regardless of dialect.
/// </para>
/// <para>
/// A replaced node is spliced into the source text and the script is reparsed once, the same mechanism
/// <see cref="ShellSyntaxNode.ReplaceNode"/> uses. <c>Visit(tree.Root)</c> returns a rewritten
/// <see cref="ShellScriptSyntax"/>; visiting a node further down rebuilds through its script and returns the node
/// that took its place, so a rewrite can be scoped to one subtree. A node with no script above it, one built by
/// <see cref="SyntaxFactory"/> rather than parsed, is returned unchanged because there is no text to splice into.
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

            var edits = new List<TextEdit>();
            foreach (var child in node.ChildNodes)
            {
                CollectEdits(child, edits);
            }

            if (edits.Count == 0)
                return node;

            if (node is ShellScriptSyntax script)
                return Rebuild(script, edits);

            // Below the root, rebuild the whole script and hand back the node that took this one's place. Every edit
            // sits inside this node, so its start offset is unchanged and identifies it in the new tree.
            var owner = node.AncestorsAndSelf().OfType<ShellScriptSyntax>().FirstOrDefault() ?? node.SyntaxTree?.Root;
            if (owner is null)
                return node;

            var rebuilt = Rebuild(owner, edits);

            return FindCounterpart(rebuilt, node) ?? node;
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
        // Walked with a stack rather than by recursion, so a deeply nested tree cannot run the stack out.
        var pending = new Stack<ShellSyntaxNode>();
        pending.Push(node);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            var updated = VisitCore(current);
            if (updated is not null && !ReferenceEquals(updated, current))
            {
                // Keep the trivia in front of the node when the replacement brings none of its own.
                var span = updated.StartsWithTrivia ? current.FullSpan : current.SpanWithoutLeadingTrivia;
                edits.Add(new TextEdit(span, updated.ToFullString()));

                continue;
            }

            foreach (var child in current.ChildNodes)
            {
                pending.Push(child);
            }
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

    /// <summary>Finds the node that replaced <paramref name="original"/> in the reparsed script.</summary>
    private static ShellSyntaxNode? FindCounterpart(ShellScriptSyntax rebuilt, ShellSyntaxNode original)
    {
        foreach (var candidate in rebuilt.DescendantNodesAndSelf())
        {
            if (candidate.Kind == original.Kind && candidate.FullSpan.Start == original.FullSpan.Start)
                return candidate;
        }

        return null;
    }

    private readonly record struct TextEdit(TextSpan Span, string Text);
}
