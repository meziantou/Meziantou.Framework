namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the zsh <c>repeat</c> loop, which runs its body a fixed number of times.</summary>
public sealed class ZshRepeatStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ZshRepeatStatementSyntax(
        ShellSyntaxToken repeatKeyword,
        ShellWordSyntax count,
        ShellSyntaxToken? listTerminatorToken,
        ShellSyntaxToken? doKeyword,
        ShellStatementListSyntax body,
        ShellSyntaxToken? doneKeyword)
        : base(
            ShellSyntaxKind.ZshRepeatStatement,
            repeatKeyword.ToFullString() + count.ToFullString() + (listTerminatorToken?.ToFullString() ?? string.Empty)
                + (doKeyword?.ToFullString() ?? string.Empty) + body.ToFullString() + (doneKeyword?.ToFullString() ?? string.Empty),
            repeatKeyword.FullSpan.Start,
            BuildTokens(repeatKeyword, listTerminatorToken, doKeyword, doneKeyword))
    {
        RepeatKeyword = repeatKeyword;
        Count = count;
        ListTerminatorToken = listTerminatorToken;
        DoKeyword = doKeyword;
        Body = body;
        DoneKeyword = doneKeyword;
        _childNodes = [count, body];
    }

    public ShellSyntaxToken RepeatKeyword { get; }

    /// <summary>The iteration count.</summary>
    public ShellWordSyntax Count { get; }

    /// <summary>The <c>;</c> that may follow the count.</summary>
    public ShellSyntaxToken? ListTerminatorToken { get; }

    /// <summary>The <c>do</c> keyword, absent when the body is a single command or a brace group.</summary>
    public ShellSyntaxToken? DoKeyword { get; }

    public ShellStatementListSyntax Body { get; }

    /// <summary>The <c>done</c> keyword, present only with the <c>do ... done</c> form.</summary>
    public ShellSyntaxToken? DoneKeyword { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitZshRepeatStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitZshRepeatStatement(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken repeatKeyword,
        ShellSyntaxToken? listTerminatorToken,
        ShellSyntaxToken? doKeyword,
        ShellSyntaxToken? doneKeyword)
    {
        var tokens = new List<ShellSyntaxToken>(4) { repeatKeyword };
        foreach (var token in new[] { listTerminatorToken, doKeyword, doneKeyword })
        {
            if (token is not null)
            {
                tokens.Add(token);
            }
        }

        return tokens;
    }
}
