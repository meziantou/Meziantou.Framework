namespace Meziantou.Framework.Language.Regex;

/// <summary>Visitor that produces a rewritten regular-expression syntax tree.</summary>
/// <remarks>
/// <para>
/// Override any <c>Visit</c> method and return a different node to replace it. Returning the node unchanged, which is
/// what the base implementations do, means "leave this alone and keep looking inside it", so every node type is
/// descended into regardless of flavor.
/// </para>
/// <para>
/// A replaced node is spliced into the source text and the pattern is reparsed once, the same mechanism
/// <see cref="RegexSyntaxNode.ReplaceNode"/> uses. <c>Visit(tree.Root)</c> returns a rewritten
/// <see cref="RegexPatternSyntax"/>; visiting a node further down rebuilds through its pattern and returns the node
/// that took its place, so a rewrite can be scoped to one subtree. A node with no pattern above it, one built by
/// <see cref="SyntaxFactory"/> rather than parsed, is returned unchanged because there is no text to splice into.
/// </para>
/// <para>
/// As with <see cref="RegexSyntaxNode.ReplaceNode"/>, a replacement that carries no leading trivia of its own keeps
/// the whitespace and comments in front of the node it replaces.
/// </para>
/// </remarks>
public class RegexSyntaxRewriter : RegexSyntaxVisitor<RegexSyntaxNode?>
{
    private bool _isWalking;

    public override RegexSyntaxNode? Visit(RegexSyntaxNode? node)
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

            if (node is RegexPatternSyntax pattern)
                return Rebuild(pattern, edits);

            // Below the root, rebuild the whole pattern and hand back the node that took this one's place. Every edit
            // sits inside this node, so its start offset is unchanged and identifies it in the new tree.
            var owner = node.AncestorsAndSelf().OfType<RegexPatternSyntax>().FirstOrDefault() ?? node.SyntaxTree?.Root;
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
    protected override RegexSyntaxNode? DefaultVisit(RegexSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node;
    }

    protected virtual RegexSyntaxNode? VisitCore(RegexSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Visit(node);
    }

    /// <summary>
    /// Asks the overrides about <paramref name="node"/>. A node that was replaced is recorded and not descended into,
    /// since its replacement stands for the whole subtree.
    /// </summary>
    private void CollectEdits(RegexSyntaxNode node, List<TextEdit> edits)
    {
        // Walked with a stack rather than by recursion, so a deeply nested tree cannot run the stack out.
        var pending = new Stack<RegexSyntaxNode>();
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

            // Pushed back to front so they pop front to back: an override that keeps state, such as one that
            // replaces only the first match, has to see the tree in the order the traversal APIs report it.
            var children = current.ChildNodes;
            for (var index = children.Count - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }
    }

    private static RegexPatternSyntax Rebuild(RegexPatternSyntax pattern, List<TextEdit> edits)
    {
        var source = pattern.ToFullString();
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

        if (pattern.SyntaxTree is { } tree)
            return tree.Reparse(builder.ToString()).Root;

        return RegexSyntaxTree.ParseText(builder.ToString(), new RegexParseOptions(pattern.Flavor ?? RegexFlavor.Net)).Root;
    }

    /// <summary>Finds the node that replaced <paramref name="original"/> in the reparsed pattern.</summary>
    private static RegexSyntaxNode? FindCounterpart(RegexPatternSyntax rebuilt, RegexSyntaxNode original)
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
