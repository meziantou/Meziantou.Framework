namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a function definition, in either the <c>name() { }</c> or <c>function name { }</c> form.</summary>
public sealed class PosixFunctionDefinitionSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixFunctionDefinitionSyntax(
        ShellSyntaxToken? functionKeyword,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? openParenToken,
        ShellSyntaxToken? closeParenToken,
        ShellStatementSyntax body)
        : base(
            ShellSyntaxKind.PosixFunctionDefinition,
            (functionKeyword?.ToFullString() ?? string.Empty) + nameToken?.ToFullString()
                + (openParenToken?.ToFullString() ?? string.Empty) + (closeParenToken?.ToFullString() ?? string.Empty)
                + body?.ToFullString(),
            (functionKeyword ?? nameToken)?.FullSpan.Start ?? 0,
            BuildTokens(functionKeyword, nameToken!, openParenToken, closeParenToken))
    {
        FunctionKeyword = functionKeyword;
        NameToken = nameToken!;
        OpenParenToken = openParenToken;
        CloseParenToken = closeParenToken;
        Body = body!;
        _childNodes = [body!];
    }

    /// <summary>The <c>function</c> keyword, present only in the bash and zsh form.</summary>
    public ShellSyntaxToken? FunctionKeyword { get; }

    public ShellSyntaxToken NameToken { get; }
    public string Name => NameToken.ValueText;
    public ShellSyntaxToken? OpenParenToken { get; }
    public ShellSyntaxToken? CloseParenToken { get; }

    /// <summary>The function body, normally a <see cref="PosixCompoundStatementSyntax"/>.</summary>
    public ShellStatementSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitFunctionDefinition(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitFunctionDefinition(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken? functionKeyword,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? openParenToken,
        ShellSyntaxToken? closeParenToken)
    {
        var tokens = new List<ShellSyntaxToken>(4);
        if (functionKeyword is not null)
        {
            tokens.Add(functionKeyword);
        }

        tokens.Add(nameToken);
        if (openParenToken is not null)
        {
            tokens.Add(openParenToken);
        }

        if (closeParenToken is not null)
        {
            tokens.Add(closeParenToken);
        }

        return tokens;
    }
}
