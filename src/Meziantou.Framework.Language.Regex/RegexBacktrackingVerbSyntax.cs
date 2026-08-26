namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a PCRE backtracking control verb such as <c>(*SKIP)</c>.</summary>
public sealed class RegexBacktrackingVerbSyntax : RegexAtomSyntax
{
    public RegexBacktrackingVerbSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken verbToken, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.BacktrackingVerb, [openParenToken, verbToken, closeParenToken], Part(openParenToken), Part(verbToken), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        VerbToken = verbToken;
        CloseParenToken = closeParenToken;
    }

    public RegexSyntaxToken OpenParenToken { get; }

    public RegexSyntaxToken VerbToken { get; }

    public RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The verb name, without the leading <c>*</c>.</summary>
    public string Name => VerbToken.Text.TrimStart('*');

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitBacktrackingVerb(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitBacktrackingVerb(this);
}
