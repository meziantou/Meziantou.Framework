namespace Meziantou.Framework.Language.Shell;

/// <summary>Visitor that can produce a rewritten shell syntax tree.</summary>
public class ShellSyntaxRewriter : ShellSyntaxVisitor<ShellSyntaxNode?>
{
    public override ShellSyntaxNode? VisitScript(ShellScriptSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (VisitCore(node.Statements) is not ShellStatementListSyntax statements || ReferenceEquals(statements, node.Statements))
            return node;

        return node.WithStatements(statements);
    }

    public override ShellSyntaxNode? VisitStatementList(ShellStatementListSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewritten = RewriteNodes<ShellStatementSyntax, ShellStatementSyntax>(node.Statements);

        return rewritten is null ? node : node.WithStatements(rewritten);
    }

    public override ShellSyntaxNode? VisitCommand(ShellCommandSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewritten = RewriteNodes<ShellSyntaxNode, ShellSyntaxNode>(node.ChildNodes);

        return rewritten is null ? node : node.WithChildNodes(rewritten);
    }

    public override ShellSyntaxNode? VisitPipeline(ShellPipelineSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewritten = RewriteNodes<ShellStatementSyntax, ShellStatementSyntax>(node.Commands);

        return rewritten is null ? node : node.WithCommands(rewritten);
    }

    public override ShellSyntaxNode? VisitCommandList(ShellCommandListSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewritten = RewriteNodes<ShellStatementSyntax, ShellStatementSyntax>(node.Pipelines);

        return rewritten is null ? node : node.WithPipelines(rewritten);
    }

    public override ShellSyntaxNode? VisitRedirection(ShellRedirectionSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Target is null)
            return node;

        if (VisitCore(node.Target) is not ShellWordSyntax target || ReferenceEquals(target, node.Target))
            return node;

        return node.WithTarget(target);
    }

    public override ShellSyntaxNode? VisitAssignment(ShellAssignmentSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Value is null)
            return node;

        if (VisitCore(node.Value) is not ShellWordSyntax value || ReferenceEquals(value, node.Value))
            return node;

        return node.WithValue(value);
    }

    public override ShellSyntaxNode? VisitWord(ShellWordSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewritten = RewriteNodes<ShellWordPartSyntax, ShellWordPartSyntax>(node.Parts);

        return rewritten is null ? node : node.WithParts(rewritten);
    }

    public override ShellSyntaxNode? VisitQuotedString(ShellQuotedStringSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewritten = RewriteNodes<ShellWordPartSyntax, ShellWordPartSyntax>(node.Parts);

        return rewritten is null ? node : node.WithParts(rewritten);
    }

    public override ShellSyntaxNode? VisitCommandSubstitution(ShellCommandSubstitutionSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (VisitCore(node.Statements) is not ShellStatementListSyntax statements || ReferenceEquals(statements, node.Statements))
            return node;

        return node.WithStatements(statements);
    }

    public override ShellSyntaxNode? VisitLiteralWordPart(ShellLiteralWordPartSyntax node) => node;
    public override ShellSyntaxNode? VisitVariableReference(ShellVariableReferenceSyntax node) => node;
    public override ShellSyntaxNode? VisitEscapeSequence(ShellEscapeSequenceSyntax node) => node;
    public override ShellSyntaxNode? VisitArithmeticExpansion(PosixArithmeticExpansionSyntax node) => node;
    public override ShellSyntaxNode? VisitGlob(ShellGlobSyntax node) => node;
    public override ShellSyntaxNode? VisitRawExpression(ShellRawExpressionSyntax node) => node;
    public override ShellSyntaxNode? VisitCompoundStatement(PosixCompoundStatementSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (VisitCore(node.Statements) is not ShellStatementListSyntax statements || ReferenceEquals(statements, node.Statements))
            return node;

        return node.WithStatements(statements);
    }

    public override ShellSyntaxNode? VisitEmptyStatement(ShellEmptyStatementSyntax node) => node;
    public override ShellSyntaxNode? VisitSkippedText(ShellSkippedTextSyntax node) => node;

    protected virtual ShellSyntaxNode? VisitCore(ShellSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Visit(node);
    }

    /// <summary>
    /// Rewrites each node in <paramref name="nodes"/>, returning <see langword="null"/> when nothing changed so the
    /// caller can keep the original instance.
    /// </summary>
    private List<TResult>? RewriteNodes<TNode, TResult>(IReadOnlyList<TNode> nodes)
        where TNode : ShellSyntaxNode
        where TResult : ShellSyntaxNode
    {
        List<TResult>? rewritten = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var current = nodes[index];
            var updated = VisitCore(current) ?? current;
            if (updated is not TResult typed)
            {
                typed = (TResult)(ShellSyntaxNode)current;
            }

            if (rewritten is null)
            {
                if (!ReferenceEquals(current, typed))
                {
                    rewritten = [];
                    for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    {
                        rewritten.Add((TResult)(ShellSyntaxNode)nodes[copyIndex]);
                    }

                    rewritten.Add(typed);
                }
            }
            else
            {
                rewritten.Add(typed);
            }
        }

        return rewritten;
    }
}
