namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a .NET balancing group, <c>(?&lt;current-previous&gt;…)</c>.</summary>
public sealed class RegexBalancingGroupSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexBalancingGroupSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken groupKindToken, RegexSyntaxToken? nameToken, RegexSyntaxToken hyphenToken, RegexSyntaxToken? previousNameToken, RegexSyntaxToken? closeNameToken, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken, int number)
        : base(RegexSyntaxKind.BalancingGroup, [openParenToken, groupKindToken, nameToken, hyphenToken, previousNameToken, closeNameToken, closeParenToken], Part(openParenToken), Part(groupKindToken), Part(nameToken), Part(hyphenToken), Part(previousNameToken), Part(closeNameToken), Part(alternation), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        GroupKindToken = groupKindToken;
        NameToken = nameToken;
        HyphenToken = hyphenToken;
        PreviousNameToken = previousNameToken;
        CloseNameToken = closeNameToken;
        Alternation = alternation;
        CloseParenToken = closeParenToken;
        Number = number;
        _childNodes = Children(alternation);
    }

    public RegexSyntaxToken GroupKindToken { get; }

    public RegexSyntaxToken? NameToken { get; }

    public RegexSyntaxToken HyphenToken { get; }

    public RegexSyntaxToken? PreviousNameToken { get; }

    public RegexSyntaxToken? CloseNameToken { get; }

    public RegexAlternationSyntax Alternation { get; }

    public override RegexSyntaxToken OpenParenToken { get; }

    public override RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The name of the group being pushed, or an empty string when the group only pops.</summary>
    public string Name => NameToken?.Text ?? string.Empty;

    /// <summary>The name or number of the group being popped.</summary>
    public string PreviousName => PreviousNameToken?.Text ?? string.Empty;

    /// <summary>The number assigned to <see cref="Name"/>, or 0 when the group only pops.</summary>
    public int Number { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitBalancingGroup(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitBalancingGroup(this);
}
