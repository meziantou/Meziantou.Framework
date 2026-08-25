namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>data</c> statement.</summary>
public sealed class PowerShellDataStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellDataStatementSyntax(
        ShellSyntaxToken dataKeyword,
        ShellSyntaxToken? nameToken,
        IReadOnlyList<ShellSyntaxToken>? parameterTokens,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellDataStatement,
            dataKeyword.ToFullString() + (nameToken?.ToFullString() ?? string.Empty) + BuildFullText(parameterTokens ?? []) + body.ToFullString(),
            dataKeyword.FullSpan.Start,
            BuildTokens(dataKeyword, nameToken, parameterTokens))
    {
        DataKeyword = dataKeyword;
        NameToken = nameToken;
        ParameterTokens = parameterTokens ?? [];
        Body = body;
        _childNodes = [.. SingleNode(Body)];
    }

    /// <summary>The <c>data</c> keyword.</summary>
    public ShellSyntaxToken DataKeyword { get; }

    /// <summary>The variable the data is stored in, when present.</summary>
    public ShellSyntaxToken? NameToken { get; }

    /// <summary>Parameters such as <c>-SupportedCommand</c>.</summary>
    public IReadOnlyList<ShellSyntaxToken> ParameterTokens { get; }

    /// <summary>The data body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitDataStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitDataStatement(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken dataKeyword,
        ShellSyntaxToken? nameToken,
        IReadOnlyList<ShellSyntaxToken>? parameterTokens)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(dataKeyword);
        if (nameToken is not null)
        {
            tokens.Add(nameToken);
        }

        tokens.AddRange(parameterTokens ?? []);

        return tokens;
    }
}
