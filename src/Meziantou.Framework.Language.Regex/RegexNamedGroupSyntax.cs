namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a named capturing group, <c>(?&lt;name&gt;…)</c>, <c>(?'name'…)</c>, or <c>(?P&lt;name&gt;…)</c>.</summary>
public sealed class RegexNamedGroupSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexNamedGroupSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken groupKindToken, RegexSyntaxToken? nameToken, RegexSyntaxToken? closeNameToken, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken, int number)
        : base(RegexSyntaxKind.NamedGroup, [openParenToken, groupKindToken, nameToken, closeNameToken, closeParenToken], Part(openParenToken), Part(groupKindToken), Part(nameToken), Part(closeNameToken), Part(alternation), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        GroupKindToken = groupKindToken;
        NameToken = nameToken;
        CloseNameToken = closeNameToken;
        Alternation = alternation;
        CloseParenToken = closeParenToken;
        Number = number;
        _childNodes = Children(alternation);
    }

    /// <summary>The <c>?&lt;</c>, <c>?'</c>, or <c>?P&lt;</c> that introduces the name.</summary>
    public RegexSyntaxToken GroupKindToken { get; }

    public RegexSyntaxToken? NameToken { get; }

    public RegexSyntaxToken? CloseNameToken { get; }

    public RegexAlternationSyntax Alternation { get; }

    public override RegexSyntaxToken OpenParenToken { get; }

    public override RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The group name.</summary>
    public string Name => NameToken?.Text ?? string.Empty;

    /// <summary>The group number the engine assigns.</summary>
    public int Number { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitNamedGroup(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitNamedGroup(this);
}
