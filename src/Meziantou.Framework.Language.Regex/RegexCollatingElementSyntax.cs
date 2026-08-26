namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a POSIX collating element, <c>[.ch.]</c>, or an equivalence class, <c>[=a=]</c>.</summary>
public sealed class RegexCollatingElementSyntax : RegexSyntaxNode
{
    public RegexCollatingElementSyntax(RegexSyntaxToken startToken, RegexSyntaxToken? textToken, RegexSyntaxToken? endToken)
        : base(RegexSyntaxKind.CollatingElement, [startToken, textToken, endToken], Part(startToken), Part(textToken), Part(endToken))
    {
        StartToken = startToken;
        TextToken = textToken;
        EndToken = endToken;
    }

    public RegexSyntaxToken StartToken { get; }

    public RegexSyntaxToken? TextToken { get; }

    public RegexSyntaxToken? EndToken { get; }

    /// <summary>The text between the delimiters.</summary>
    public string Value => TextToken?.Text ?? string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitCollatingElement(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitCollatingElement(this);
}
