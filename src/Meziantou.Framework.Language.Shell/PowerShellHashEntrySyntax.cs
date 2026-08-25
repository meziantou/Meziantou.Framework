namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents one <c>key = value</c> entry of a hashtable literal.</summary>
public sealed class PowerShellHashEntrySyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellHashEntrySyntax(
        ShellSyntaxNode key,
        ShellSyntaxToken equalsToken,
        ShellSyntaxNode value,
        ShellSyntaxToken? separatorToken)
        : base(
            ShellSyntaxKind.PowerShellHashEntry,
            key.ToFullString() + equalsToken.ToFullString() + value.ToFullString() + (separatorToken?.ToFullString() ?? string.Empty),
            key.FullSpan.Start,
            BuildTokens(equalsToken, separatorToken))
    {
        Key = key;
        EqualsToken = equalsToken;
        Value = value;
        SeparatorToken = separatorToken;
        _childNodes = [.. SingleNode(Key), .. SingleNode(Value)];
    }

    /// <summary>The entry key.</summary>
    public ShellSyntaxNode Key { get; }

    /// <summary>The <c>=</c> token.</summary>
    public ShellSyntaxToken EqualsToken { get; }

    /// <summary>The entry value.</summary>
    public ShellSyntaxNode Value { get; }

    /// <summary>The <c>;</c> that follows the entry, when present.</summary>
    public ShellSyntaxToken? SeparatorToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitHashEntry(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitHashEntry(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken equalsToken,
        ShellSyntaxToken? separatorToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(equalsToken);
        if (separatorToken is not null)
        {
            tokens.Add(separatorToken);
        }

        return tokens;
    }
}
