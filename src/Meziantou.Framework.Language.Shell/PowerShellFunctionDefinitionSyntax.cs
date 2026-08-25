namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>function</c>, <c>filter</c>, or <c>workflow</c> definition.</summary>
public sealed class PowerShellFunctionDefinitionSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellFunctionDefinitionSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken keyword,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? openParenToken,
        IReadOnlyList<PowerShellParameterSyntax>? parameters,
        IReadOnlyList<ShellSyntaxToken>? parameterSeparatorTokens,
        ShellSyntaxToken? closeParenToken,
        PowerShellScriptBlockSyntax body)
        : base(
            kind,
            keyword.ToFullString() + nameToken.ToFullString() + (openParenToken?.ToFullString() ?? string.Empty) + SeparatedNodes.BuildText(parameters, parameterSeparatorTokens) + (closeParenToken?.ToFullString() ?? string.Empty) + body.ToFullString(),
            keyword.FullSpan.Start,
            BuildTokens(keyword, nameToken, openParenToken, parameterSeparatorTokens, closeParenToken))
    {
        Keyword = keyword;
        NameToken = nameToken;
        OpenParenToken = openParenToken;
        Parameters = parameters ?? [];
        ParameterSeparatorTokens = parameterSeparatorTokens ?? [];
        CloseParenToken = closeParenToken;
        Body = body;
        _childNodes = [.. (Parameters as IEnumerable<ShellSyntaxNode>), .. SingleNode(Body)];
    }

    /// <summary>The <c>function</c>, <c>filter</c>, or <c>workflow</c> keyword.</summary>
    public ShellSyntaxToken Keyword { get; }

    /// <summary>The definition name.</summary>
    public ShellSyntaxToken NameToken { get; }

    /// <summary>The opening parenthesis of an inline parameter list.</summary>
    public ShellSyntaxToken? OpenParenToken { get; }

    /// <summary>The inline parameters.</summary>
    public IReadOnlyList<PowerShellParameterSyntax> Parameters { get; }

    /// <summary>The separator that follows each entry of <see cref="Parameters"/>.</summary>
    public IReadOnlyList<ShellSyntaxToken> ParameterSeparatorTokens { get; }

    /// <summary>The closing parenthesis of an inline parameter list.</summary>
    public ShellSyntaxToken? CloseParenToken { get; }

    /// <summary>The body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>The definition name.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for a <c>filter</c>, which runs its body once per pipeline item.</summary>
    public bool IsFilter => Kind == ShellSyntaxKind.PowerShellFilterDefinition;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPowerShellFunctionDefinition(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPowerShellFunctionDefinition(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken keyword,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? openParenToken,
        IReadOnlyList<ShellSyntaxToken>? parameterSeparatorTokens,
        ShellSyntaxToken? closeParenToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(keyword);
        tokens.Add(nameToken);
        if (openParenToken is not null)
        {
            tokens.Add(openParenToken);
        }

        tokens.AddRange(parameterSeparatorTokens ?? []);
        if (closeParenToken is not null)
        {
            tokens.Add(closeParenToken);
        }

        return tokens;
    }
}
