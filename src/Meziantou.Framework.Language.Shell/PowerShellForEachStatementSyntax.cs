namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>foreach ($x in $y) { }</c> loop.</summary>
public sealed class PowerShellForEachStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellForEachStatementSyntax(
        ShellSyntaxToken forEachKeyword,
        ShellSyntaxToken openParenToken,
        ShellExpressionSyntax variable,
        ShellSyntaxToken inKeyword,
        ShellSyntaxNode collection,
        ShellSyntaxToken closeParenToken,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellForEachStatement,
            forEachKeyword.ToFullString() + openParenToken.ToFullString() + variable.ToFullString() + inKeyword.ToFullString() + collection.ToFullString() + closeParenToken.ToFullString() + body.ToFullString(),
            forEachKeyword.FullSpan.Start,
            [forEachKeyword, openParenToken, inKeyword, closeParenToken])
    {
        ForEachKeyword = forEachKeyword;
        OpenParenToken = openParenToken;
        Variable = variable;
        InKeyword = inKeyword;
        Collection = collection;
        CloseParenToken = closeParenToken;
        Body = body;
        _childNodes = [.. SingleNode(Variable), .. SingleNode(Collection), .. SingleNode(Body)];
    }

    /// <summary>The <c>foreach</c> keyword.</summary>
    public ShellSyntaxToken ForEachKeyword { get; }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The loop variable.</summary>
    public ShellExpressionSyntax Variable { get; }

    /// <summary>The <c>in</c> keyword.</summary>
    public ShellSyntaxToken InKeyword { get; }

    /// <summary>The collection being enumerated.</summary>
    public ShellSyntaxNode Collection { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    /// <summary>The loop body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitForEachStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitForEachStatement(this);
}
