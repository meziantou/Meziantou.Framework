namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>for</c> loop, including the <c>/d</c>, <c>/r</c>, <c>/l</c>, and <c>/f</c> forms.</summary>
public sealed class CmdForStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public CmdForStatementSyntax(
        ShellSyntaxToken forKeyword,
        ShellSyntaxToken? switchToken,
        IReadOnlyList<ShellWordSyntax>? switchArguments,
        ShellSyntaxToken variableToken,
        ShellSyntaxToken inKeyword,
        ShellSyntaxToken openParenToken,
        IReadOnlyList<ShellWordSyntax>? items,
        ShellSyntaxToken closeParenToken,
        ShellSyntaxToken doKeyword,
        ShellStatementSyntax body)
        : base(
            ShellSyntaxKind.CmdForStatement,
            forKeyword.ToFullString() + (switchToken?.ToFullString() ?? string.Empty) + BuildFullText(switchArguments ?? []) + variableToken.ToFullString() + inKeyword.ToFullString() + openParenToken.ToFullString() + BuildFullText(items ?? []) + closeParenToken.ToFullString() + doKeyword.ToFullString() + body.ToFullString(),
            forKeyword.FullSpan.Start,
            BuildTokens(forKeyword, switchToken, variableToken, inKeyword, openParenToken, closeParenToken, doKeyword))
    {
        ForKeyword = forKeyword;
        SwitchToken = switchToken;
        SwitchArguments = switchArguments ?? [];
        VariableToken = variableToken;
        InKeyword = inKeyword;
        OpenParenToken = openParenToken;
        Items = items ?? [];
        CloseParenToken = closeParenToken;
        DoKeyword = doKeyword;
        Body = body;
        _childNodes = [.. (SwitchArguments as IEnumerable<ShellSyntaxNode>), .. (Items as IEnumerable<ShellSyntaxNode>), .. SingleNode(Body)];
    }

    /// <summary>The <c>for</c> keyword.</summary>
    public ShellSyntaxToken ForKeyword { get; }

    /// <summary>The <c>/d</c>, <c>/r</c>, <c>/l</c>, or <c>/f</c> switch, when present.</summary>
    public ShellSyntaxToken? SwitchToken { get; }

    /// <summary>Words between the switch and the loop variable, such as an <c>/f</c> option string.</summary>
    public IReadOnlyList<ShellWordSyntax> SwitchArguments { get; }

    /// <summary>The loop variable, written <c>%%i</c> in a script and <c>%i</c> at the prompt.</summary>
    public ShellSyntaxToken VariableToken { get; }

    /// <summary>The <c>in</c> keyword.</summary>
    public ShellSyntaxToken InKeyword { get; }

    /// <summary>The opening parenthesis of the item set.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The words of the item set.</summary>
    public IReadOnlyList<ShellWordSyntax> Items { get; }

    /// <summary>The closing parenthesis of the item set.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    /// <summary>The <c>do</c> keyword.</summary>
    public ShellSyntaxToken DoKeyword { get; }

    /// <summary>The loop body.</summary>
    public ShellStatementSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>The loop variable name without its leading percent signs.</summary>
    public string VariableName => VariableToken.Text.TrimStart('%');

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdFor(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdFor(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken forKeyword,
        ShellSyntaxToken? switchToken,
        ShellSyntaxToken variableToken,
        ShellSyntaxToken inKeyword,
        ShellSyntaxToken openParenToken,
        ShellSyntaxToken closeParenToken,
        ShellSyntaxToken doKeyword)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(forKeyword);
        if (switchToken is not null)
        {
            tokens.Add(switchToken);
        }

        tokens.Add(variableToken);
        tokens.Add(inKeyword);
        tokens.Add(openParenToken);
        tokens.Add(closeParenToken);
        tokens.Add(doKeyword);

        return tokens;
    }
}
