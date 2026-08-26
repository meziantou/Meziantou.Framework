namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a <c>\Q…\E</c> run, in which every character is literal.</summary>
public sealed class RegexQuotedLiteralSyntax : RegexAtomSyntax
{
    public RegexQuotedLiteralSyntax(RegexSyntaxToken startToken, RegexSyntaxToken? textToken, RegexSyntaxToken? endToken)
        : base(RegexSyntaxKind.QuotedLiteral, [startToken, textToken, endToken], Part(startToken), Part(textToken), Part(endToken))
    {
        StartToken = startToken;
        TextToken = textToken;
        EndToken = endToken;
    }

    /// <summary>The <c>\Q</c> that opens the run.</summary>
    public RegexSyntaxToken StartToken { get; }

    public RegexSyntaxToken? TextToken { get; }

    /// <summary>The <c>\E</c> that closes the run, absent when the run reaches the end of the pattern.</summary>
    public RegexSyntaxToken? EndToken { get; }

    /// <summary>The literal text between the delimiters.</summary>
    public string Value => TextToken?.Text ?? string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitQuotedLiteral(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitQuotedLiteral(this);
}
