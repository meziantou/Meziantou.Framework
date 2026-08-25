namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a parameter expansion such as <c>$name</c>, <c>${name}</c>, or <c>%NAME%</c>.</summary>
public sealed class ShellVariableReferenceSyntax : ShellWordPartSyntax
{
    public ShellVariableReferenceSyntax(
        ShellSyntaxToken introducerToken,
        ShellSyntaxToken? openBraceToken,
        ShellSyntaxToken nameToken,
        ShellSyntaxToken? closeBraceToken)
        : base(
            ShellSyntaxKind.VariableReference,
            BuildText(introducerToken, openBraceToken, nameToken, closeBraceToken),
            introducerToken?.FullSpan.Start ?? 0,
            BuildTokens(introducerToken!, openBraceToken, nameToken!, closeBraceToken))
    {
        IntroducerToken = introducerToken!;
        OpenBraceToken = openBraceToken;
        NameToken = nameToken!;
        CloseBraceToken = closeBraceToken;
    }

    /// <summary>The token that introduces the expansion, such as <c>$</c>.</summary>
    public ShellSyntaxToken IntroducerToken { get; }

    public ShellSyntaxToken? OpenBraceToken { get; }
    public ShellSyntaxToken NameToken { get; }
    public ShellSyntaxToken? CloseBraceToken { get; }

    /// <summary>The name of the referenced variable, without the introducer or braces.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> when the reference uses the braced form.</summary>
    public bool IsBraced => OpenBraceToken is not null;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitVariableReference(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitVariableReference(this);

    private static string BuildText(ShellSyntaxToken introducerToken, ShellSyntaxToken? openBraceToken, ShellSyntaxToken nameToken, ShellSyntaxToken? closeBraceToken)
    {
        ArgumentNullException.ThrowIfNull(introducerToken);
        ArgumentNullException.ThrowIfNull(nameToken);

        return introducerToken.ToFullString()
            + (openBraceToken?.ToFullString() ?? string.Empty)
            + nameToken.ToFullString()
            + (closeBraceToken?.ToFullString() ?? string.Empty);
    }

    private static List<ShellSyntaxToken> BuildTokens(ShellSyntaxToken introducerToken, ShellSyntaxToken? openBraceToken, ShellSyntaxToken nameToken, ShellSyntaxToken? closeBraceToken)
    {
        var tokens = new List<ShellSyntaxToken>(4) { introducerToken };
        if (openBraceToken is not null)
        {
            tokens.Add(openBraceToken);
        }

        tokens.Add(nameToken);
        if (closeBraceToken is not null)
        {
            tokens.Add(closeBraceToken);
        }

        return tokens;
    }
}
