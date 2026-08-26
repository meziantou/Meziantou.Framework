namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a POSIX bracket expression such as <c>[:alpha:]</c>, which is only meaningful inside a character class.</summary>
public sealed class RegexPosixCharacterClassSyntax : RegexSyntaxNode
{
    public RegexPosixCharacterClassSyntax(RegexSyntaxToken startToken, RegexSyntaxToken? nameToken, RegexSyntaxToken? endToken)
        : base(RegexSyntaxKind.PosixCharacterClass, [startToken, nameToken, endToken], Part(startToken), Part(nameToken), Part(endToken))
    {
        StartToken = startToken;
        NameToken = nameToken;
        EndToken = endToken;
    }

    public RegexSyntaxToken StartToken { get; }

    public RegexSyntaxToken? NameToken { get; }

    public RegexSyntaxToken? EndToken { get; }

    /// <summary>Returns <see langword="true"/> for the negated form, <c>[:^alpha:]</c>.</summary>
    public bool IsNegated => NameToken?.Text is ['^', ..];

    /// <summary>The class name, without the negation marker.</summary>
    public string Name => NameToken?.Text.TrimStart('^') ?? string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitPosixCharacterClass(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitPosixCharacterClass(this);
}
