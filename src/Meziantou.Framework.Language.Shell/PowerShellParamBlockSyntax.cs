namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>param( ... )</c> block.</summary>
public sealed class PowerShellParamBlockSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellParamBlockSyntax(
        IReadOnlyList<PowerShellAttributeSyntax>? attributes,
        ShellSyntaxToken paramKeyword,
        ShellSyntaxToken openParenToken,
        IReadOnlyList<PowerShellParameterSyntax>? parameters,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens,
        ShellSyntaxToken closeParenToken)
        : base(
            ShellSyntaxKind.PowerShellParamBlock,
            BuildFullText(attributes ?? []) + paramKeyword.ToFullString() + openParenToken.ToFullString() + SeparatedNodes.BuildText(parameters, separatorTokens) + closeParenToken.ToFullString(),
            // The text starts at the first attribute when there is one, so the span has to start there too.
            attributes is { Count: > 0 } ? attributes[0].FullSpan.Start : paramKeyword.FullSpan.Start,
            BuildTokens(paramKeyword, openParenToken, separatorTokens, closeParenToken))
    {
        Attributes = attributes ?? [];
        ParamKeyword = paramKeyword;
        OpenParenToken = openParenToken;
        Parameters = parameters ?? [];
        SeparatorTokens = separatorTokens ?? [];
        CloseParenToken = closeParenToken;
        _childNodes = [.. (Attributes as IEnumerable<ShellSyntaxNode>), .. (Parameters as IEnumerable<ShellSyntaxNode>)];
    }

    /// <summary>Attributes applied to the block.</summary>
    public IReadOnlyList<PowerShellAttributeSyntax> Attributes { get; }

    /// <summary>The <c>param</c> keyword.</summary>
    public ShellSyntaxToken ParamKeyword { get; }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The declared parameters.</summary>
    public IReadOnlyList<PowerShellParameterSyntax> Parameters { get; }

    /// <summary>The separator that follows each entry of <see cref="Parameters"/>.</summary>
    public IReadOnlyList<ShellSyntaxToken> SeparatorTokens { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitParamBlock(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitParamBlock(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken paramKeyword,
        ShellSyntaxToken openParenToken,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens,
        ShellSyntaxToken closeParenToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(paramKeyword);
        tokens.Add(openParenToken);
        tokens.AddRange(separatorTokens ?? []);
        tokens.Add(closeParenToken);

        return tokens;
    }
}
