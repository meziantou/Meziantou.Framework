namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a JavaScript <c>v</c>-mode string disjunction inside a class, <c>\q{abc|def}</c>.</summary>
/// <remarks>
/// This is what lets a class match a sequence rather than a single character, which is why <c>v</c> mode calls its
/// members sets of strings rather than sets of characters.
/// </remarks>
public sealed class RegexClassStringLiteralSyntax : RegexSyntaxNode
{
    public RegexClassStringLiteralSyntax(RegexSyntaxToken startToken, RegexSyntaxToken? textToken, RegexSyntaxToken? closeBraceToken)
        : base(RegexSyntaxKind.ClassStringLiteral, [startToken, textToken, closeBraceToken], Part(startToken), Part(textToken), Part(closeBraceToken))
    {
        StartToken = startToken;
        TextToken = textToken;
        CloseBraceToken = closeBraceToken;
    }

    /// <summary>The <c>\q{</c> that opens the disjunction.</summary>
    public RegexSyntaxToken StartToken { get; }

    public RegexSyntaxToken? TextToken { get; }

    public RegexSyntaxToken? CloseBraceToken { get; }

    /// <summary>The text between the braces, alternatives and all.</summary>
    public string Value => TextToken?.Text ?? string.Empty;

    /// <summary>The alternatives the disjunction lists.</summary>
    public IReadOnlyList<string> Alternatives => Value.Length == 0 ? [] : Value.Split('|');

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitClassStringLiteral(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitClassStringLiteral(this);
}
