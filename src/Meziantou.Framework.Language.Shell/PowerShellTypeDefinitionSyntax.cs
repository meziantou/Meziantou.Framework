namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>class</c> or <c>enum</c> definition.</summary>
public sealed class PowerShellTypeDefinitionSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellTypeDefinitionSyntax(
        ShellSyntaxKind kind,
        IReadOnlyList<PowerShellAttributeSyntax>? attributes,
        ShellSyntaxToken keyword,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? colonToken,
        IReadOnlyList<PowerShellTypeLiteralSyntax>? baseTypes,
        IReadOnlyList<ShellSyntaxToken>? baseTypeSeparatorTokens,
        ShellSyntaxToken openBraceToken,
        ShellStatementListSyntax members,
        ShellSyntaxToken closeBraceToken)
        : base(
            kind,
            BuildFullText(attributes ?? []) + keyword.ToFullString() + nameToken.ToFullString() + (colonToken?.ToFullString() ?? string.Empty) + SeparatedNodes.BuildText(baseTypes, baseTypeSeparatorTokens) + openBraceToken.ToFullString() + members.ToFullString() + closeBraceToken.ToFullString(),
            keyword.FullSpan.Start,
            BuildTokens(keyword, nameToken, colonToken, baseTypeSeparatorTokens, openBraceToken, closeBraceToken))
    {
        Attributes = attributes ?? [];
        Keyword = keyword;
        NameToken = nameToken;
        ColonToken = colonToken;
        BaseTypes = baseTypes ?? [];
        BaseTypeSeparatorTokens = baseTypeSeparatorTokens ?? [];
        OpenBraceToken = openBraceToken;
        Members = members;
        CloseBraceToken = closeBraceToken;
        _childNodes = [.. (Attributes as IEnumerable<ShellSyntaxNode>), .. (BaseTypes as IEnumerable<ShellSyntaxNode>), .. SingleNode(Members)];
    }

    /// <summary>Attributes applied to the definition.</summary>
    public IReadOnlyList<PowerShellAttributeSyntax> Attributes { get; }

    /// <summary>The <c>class</c> or <c>enum</c> keyword.</summary>
    public ShellSyntaxToken Keyword { get; }

    /// <summary>The type name.</summary>
    public ShellSyntaxToken NameToken { get; }

    /// <summary>The <c>:</c> that introduces a base type list.</summary>
    public ShellSyntaxToken? ColonToken { get; }

    /// <summary>The base types and implemented interfaces.</summary>
    public IReadOnlyList<PowerShellTypeLiteralSyntax> BaseTypes { get; }

    /// <summary>The separator that follows each entry of <see cref="BaseTypes"/>.</summary>
    public IReadOnlyList<ShellSyntaxToken> BaseTypeSeparatorTokens { get; }

    /// <summary>The opening brace.</summary>
    public ShellSyntaxToken OpenBraceToken { get; }

    /// <summary>The member declarations, kept as statements.</summary>
    public ShellStatementListSyntax Members { get; }

    /// <summary>The closing brace.</summary>
    public ShellSyntaxToken CloseBraceToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>The type name.</summary>
    public string Name => NameToken.ValueText;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitTypeDefinition(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitTypeDefinition(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken keyword,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? colonToken,
        IReadOnlyList<ShellSyntaxToken>? baseTypeSeparatorTokens,
        ShellSyntaxToken openBraceToken,
        ShellSyntaxToken closeBraceToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(keyword);
        tokens.Add(nameToken);
        if (colonToken is not null)
        {
            tokens.Add(colonToken);
        }

        tokens.AddRange(baseTypeSeparatorTokens ?? []);
        tokens.Add(openBraceToken);
        tokens.Add(closeBraceToken);

        return tokens;
    }
}
