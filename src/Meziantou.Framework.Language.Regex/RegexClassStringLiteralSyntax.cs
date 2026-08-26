namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a JavaScript <c>v</c>-mode string literal inside a class, <c>\q{abc}</c>.</summary>
public sealed class RegexClassStringLiteralSyntax : RegexSyntaxNode
{
    public RegexClassStringLiteralSyntax(RegexSyntaxToken startToken, RegexSyntaxToken? textToken, RegexSyntaxToken? closeBraceToken)
        : base(RegexSyntaxKind.ClassStringLiteral, [startToken, textToken, closeBraceToken], Part(startToken), Part(textToken), Part(closeBraceToken))
    {
        StartToken = startToken;
        TextToken = textToken;
        CloseBraceToken = closeBraceToken;
    }

    public RegexSyntaxToken StartToken { get; }

    public RegexSyntaxToken? TextToken { get; }

    public RegexSyntaxToken? CloseBraceToken { get; }

    /// <summary>The literal text between the braces.</summary>
    public string Value => TextToken?.Text ?? string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitClassStringLiteral(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitClassStringLiteral(this);
}
