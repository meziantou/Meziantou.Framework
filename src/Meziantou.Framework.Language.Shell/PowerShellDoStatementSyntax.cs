namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>do ... while</c> or <c>do ... until</c> loop.</summary>
public sealed class PowerShellDoStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellDoStatementSyntax(
        ShellSyntaxToken doKeyword,
        PowerShellScriptBlockSyntax body,
        ShellSyntaxToken conditionKeyword,
        ShellSyntaxToken openParenToken,
        ShellStatementListSyntax condition,
        ShellSyntaxToken closeParenToken)
        : base(
            ShellSyntaxKind.PowerShellDoStatement,
            doKeyword.ToFullString() + body.ToFullString() + conditionKeyword.ToFullString() + openParenToken.ToFullString() + condition.ToFullString() + closeParenToken.ToFullString(),
            doKeyword.FullSpan.Start,
            [doKeyword, conditionKeyword, openParenToken, closeParenToken])
    {
        DoKeyword = doKeyword;
        Body = body;
        ConditionKeyword = conditionKeyword;
        OpenParenToken = openParenToken;
        Condition = condition;
        CloseParenToken = closeParenToken;
        _childNodes = [.. SingleNode(Body), .. SingleNode(Condition)];
    }

    /// <summary>The <c lang="powershell">do</c> keyword.</summary>
    public ShellSyntaxToken DoKeyword { get; }

    /// <summary>The loop body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    /// <summary>The trailing <c lang="powershell">while</c> or <c lang="powershell">until</c> keyword.</summary>
    public ShellSyntaxToken ConditionKeyword { get; }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The loop condition.</summary>
    public ShellStatementListSyntax Condition { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> when the loop repeats until the condition becomes true.</summary>
    public bool IsUntil => string.Equals(ConditionKeyword.Text, "until", StringComparison.OrdinalIgnoreCase);

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitDoStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitDoStatement(this);
}
