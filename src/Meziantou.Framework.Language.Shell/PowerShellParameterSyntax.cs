namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents one declared parameter, with its attributes, type, and default value.</summary>
public sealed class PowerShellParameterSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellParameterSyntax(
        IReadOnlyList<PowerShellAttributeSyntax>? attributes,
        ShellExpressionSyntax variable,
        ShellSyntaxToken? equalsToken,
        ShellSyntaxNode? defaultValue)
        : base(
            ShellSyntaxKind.PowerShellParameter,
            BuildFullText(attributes ?? []) + variable.ToFullString() + (equalsToken?.ToFullString() ?? string.Empty) + (defaultValue?.ToFullString() ?? string.Empty),
            variable.FullSpan.Start,
            BuildTokens(equalsToken))
    {
        Attributes = attributes ?? [];
        Variable = variable;
        EqualsToken = equalsToken;
        DefaultValue = defaultValue;
        _childNodes = [.. (Attributes as IEnumerable<ShellSyntaxNode>), .. SingleNode(Variable), .. OptionalNode(DefaultValue)];
    }

    /// <summary>The attributes and type constraints applied to the parameter.</summary>
    public IReadOnlyList<PowerShellAttributeSyntax> Attributes { get; }

    /// <summary>The parameter variable.</summary>
    public ShellExpressionSyntax Variable { get; }

    /// <summary>The <c>=</c> before a default value.</summary>
    public ShellSyntaxToken? EqualsToken { get; }

    /// <summary>The default value, when present.</summary>
    public ShellSyntaxNode? DefaultValue { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitParameter(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitParameter(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken? equalsToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        if (equalsToken is not null)
        {
            tokens.Add(equalsToken);
        }

        return tokens;
    }
}
