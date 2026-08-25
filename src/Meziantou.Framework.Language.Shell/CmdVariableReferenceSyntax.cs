namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents <c>%VAR%</c>, delayed expansion <c>!VAR!</c>, an argument such as <c>%1</c> or <c>%~dp0</c>, or a loop variable <c>%%i</c>.</summary>
public sealed class CmdVariableReferenceSyntax : ShellWordPartSyntax
{
    public CmdVariableReferenceSyntax(
        ShellSyntaxToken openToken,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? closeToken)
        : base(
            ShellSyntaxKind.CmdVariableReference,
            openToken.ToFullString() + nameToken.ToFullString() + (closeToken?.ToFullString() ?? string.Empty),
            openToken.FullSpan.Start,
            BuildTokens(openToken, nameToken, closeToken))
    {
        OpenToken = openToken;
        NameToken = nameToken;
        CloseToken = closeToken;
    }

    /// <summary>The introducing <c>%</c>, <c>%%</c>, or <c>!</c>.</summary>
    public ShellSyntaxToken OpenToken { get; }

    /// <summary>The variable name or argument selector.</summary>
    public ShellSyntaxToken NameToken { get; }

    /// <summary>The closing <c>%</c> or <c>!</c>, absent for argument and loop-variable references.</summary>
    public ShellSyntaxToken? CloseToken { get; }

    /// <summary>The referenced name.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for <c>!VAR!</c>, which is resolved at execution time.</summary>
    public bool IsDelayed => OpenToken.Text == "!";

    /// <summary>Returns <see langword="true"/> for a <c>for</c> loop variable, <c>%%i</c>.</summary>
    public bool IsLoopVariable => OpenToken.Text == "%%";

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdVariableReference(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdVariableReference(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken openToken,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? closeToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(openToken);
        tokens.Add(nameToken);
        if (closeToken is not null)
        {
            tokens.Add(closeToken);
        }

        return tokens;
    }
}
