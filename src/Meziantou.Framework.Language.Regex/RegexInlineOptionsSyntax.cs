namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents an inline option setter such as <c>(?i)</c> or <c>(?-x)</c>, which has no body.</summary>
public sealed class RegexInlineOptionsSyntax : RegexAtomSyntax
{
    public RegexInlineOptionsSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken questionToken, RegexSyntaxToken? optionsToken, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.InlineOptions, [openParenToken, questionToken, optionsToken, closeParenToken], Part(openParenToken), Part(questionToken), Part(optionsToken), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        QuestionToken = questionToken;
        OptionsToken = optionsToken;
        CloseParenToken = closeParenToken;
    }

    public RegexSyntaxToken OpenParenToken { get; }

    public RegexSyntaxToken QuestionToken { get; }

    public RegexSyntaxToken? OptionsToken { get; }

    public RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The option letters, such as <c>imnsx-imnsx</c>.</summary>
    public string OptionsText => OptionsToken?.Text ?? string.Empty;

    /// <summary>The options in effect after this setter.</summary>
    public RegexPatternOptions AppliedOptions { get; internal set; }

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitInlineOptions(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitInlineOptions(this);
}
