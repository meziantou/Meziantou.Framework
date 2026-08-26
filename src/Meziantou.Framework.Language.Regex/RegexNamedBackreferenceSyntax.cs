namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a named backreference such as <c>\k&lt;name&gt;</c>, <c>\k'name'</c>, or <c>\&lt;name&gt;</c>.</summary>
public sealed class RegexNamedBackreferenceSyntax : RegexAtomSyntax
{
    public RegexNamedBackreferenceSyntax(RegexSyntaxToken startToken, RegexSyntaxToken? openNameToken, RegexSyntaxToken? nameToken, RegexSyntaxToken? closeNameToken)
        : base(RegexSyntaxKind.NamedBackreference, [startToken, openNameToken, nameToken, closeNameToken], Part(startToken), Part(openNameToken), Part(nameToken), Part(closeNameToken))
    {
        StartToken = startToken;
        OpenNameToken = openNameToken;
        NameToken = nameToken;
        CloseNameToken = closeNameToken;
    }

    /// <summary>The <c>\k</c> that introduces the reference, or the <c>\</c> of the <c>\&lt;name&gt;</c> spelling.</summary>
    public RegexSyntaxToken StartToken { get; }

    public RegexSyntaxToken? OpenNameToken { get; }

    public RegexSyntaxToken? NameToken { get; }

    public RegexSyntaxToken? CloseNameToken { get; }

    /// <summary>The group name the reference names, or an empty string when the construct is incomplete.</summary>
    public string Name => NameToken?.Text ?? string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitNamedBackreference(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitNamedBackreference(this);
}
