namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a group that scopes inline options to its body, <c>(?i:…)</c>.</summary>
public sealed class RegexOptionsGroupSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexOptionsGroupSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken questionToken, RegexSyntaxToken? optionsToken, RegexSyntaxToken? colonToken, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.OptionsGroup, [openParenToken, questionToken, optionsToken, colonToken, closeParenToken], Part(openParenToken), Part(questionToken), Part(optionsToken), Part(colonToken), Part(alternation), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        QuestionToken = questionToken;
        OptionsToken = optionsToken;
        ColonToken = colonToken;
        Alternation = alternation;
        CloseParenToken = closeParenToken;
        _childNodes = Children(alternation);
    }

    public RegexSyntaxToken QuestionToken { get; }

    public RegexSyntaxToken? OptionsToken { get; }

    /// <summary>The <c>:</c> that separates the options from the body, absent when the header is malformed.</summary>
    public RegexSyntaxToken? ColonToken { get; }

    public RegexAlternationSyntax Alternation { get; }

    public override RegexSyntaxToken OpenParenToken { get; }

    public override RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The option letters, such as <c>ix-ms</c>.</summary>
    public string OptionsText => OptionsToken?.Text ?? string.Empty;

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitOptionsGroup(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitOptionsGroup(this);
}
