namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a PCRE callout, <c>(?C)</c>, <c>(?C1)</c>, or <c>(?C"text")</c>.</summary>
/// <remarks>A callout hands control to the host at that point in the match. It matches nothing itself.</remarks>
public sealed class RegexCalloutSyntax : RegexAtomSyntax
{
    public RegexCalloutSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken questionToken, RegexSyntaxToken? bodyToken, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.Callout, [openParenToken, questionToken, bodyToken, closeParenToken], Part(openParenToken), Part(questionToken), Part(bodyToken), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        QuestionToken = questionToken;
        BodyToken = bodyToken;
        CloseParenToken = closeParenToken;
    }

    public RegexSyntaxToken OpenParenToken { get; }

    /// <summary>The <c>?C</c> that introduces the callout.</summary>
    public RegexSyntaxToken QuestionToken { get; }

    /// <summary>The number or quoted string that identifies the callout, absent for a bare <c>(?C)</c>.</summary>
    public RegexSyntaxToken? BodyToken { get; }

    public RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The callout's identifier, or an empty string when it has none.</summary>
    public string Value => BodyToken?.Text ?? string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitCallout(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitCallout(this);
}
