namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>for</c> or <c>select</c> loop over a word list.</summary>
public sealed class PosixForStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixForStatementSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken keyword,
        ShellSyntaxToken variableToken,
        ShellSyntaxToken? inKeyword,
        IReadOnlyList<ShellWordSyntax>? items,
        ShellSyntaxToken? listTerminatorToken,
        ShellSyntaxToken doKeyword,
        ShellStatementListSyntax body,
        ShellSyntaxToken doneKeyword)
        : base(
            kind,
            keyword?.ToFullString() + variableToken?.ToFullString() + (inKeyword?.ToFullString() ?? string.Empty)
                + BuildFullText(items ?? []) + (listTerminatorToken?.ToFullString() ?? string.Empty)
                + doKeyword?.ToFullString() + body?.ToFullString() + doneKeyword?.ToFullString(),
            keyword?.FullSpan.Start ?? 0,
            BuildTokens(keyword!, variableToken!, inKeyword, listTerminatorToken, doKeyword!, doneKeyword!))
    {
        Keyword = keyword!;
        VariableToken = variableToken!;
        InKeyword = inKeyword;
        Items = items ?? [];
        ListTerminatorToken = listTerminatorToken;
        DoKeyword = doKeyword!;
        Body = body!;
        DoneKeyword = doneKeyword!;
        _childNodes = [.. Items, body!];
    }

    /// <summary>The <c>for</c> or <c>select</c> keyword.</summary>
    public ShellSyntaxToken Keyword { get; }

    public ShellSyntaxToken VariableToken { get; }
    public string VariableName => VariableToken.ValueText;

    /// <summary>The <c>in</c> keyword, absent when the loop iterates the positional parameters.</summary>
    public ShellSyntaxToken? InKeyword { get; }

    public IReadOnlyList<ShellWordSyntax> Items { get; }

    /// <summary>The <c>;</c> or newline-equivalent token that closes the item list, when present.</summary>
    public ShellSyntaxToken? ListTerminatorToken { get; }

    public ShellSyntaxToken DoKeyword { get; }
    public ShellStatementListSyntax Body { get; }
    public ShellSyntaxToken DoneKeyword { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public bool IsSelect => Kind == ShellSyntaxKind.PosixSelectStatement;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitForStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitForStatement(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken keyword,
        ShellSyntaxToken variableToken,
        ShellSyntaxToken? inKeyword,
        ShellSyntaxToken? listTerminatorToken,
        ShellSyntaxToken doKeyword,
        ShellSyntaxToken doneKeyword)
    {
        var tokens = new List<ShellSyntaxToken>(6) { keyword, variableToken };
        if (inKeyword is not null)
        {
            tokens.Add(inKeyword);
        }

        if (listTerminatorToken is not null)
        {
            tokens.Add(listTerminatorToken);
        }

        tokens.Add(doKeyword);
        tokens.Add(doneKeyword);

        return tokens;
    }
}
