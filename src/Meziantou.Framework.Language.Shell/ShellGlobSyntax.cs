namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a pathname-expansion metacharacter inside a word, such as <c>*</c> or <c>?</c>.</summary>
public sealed class ShellGlobSyntax : ShellWordPartSyntax
{
    public ShellGlobSyntax(ShellSyntaxToken globToken)
        : base(ShellSyntaxKind.Glob, GetFullText(globToken), globToken?.FullSpan.Start ?? 0, [globToken!])
    {
        GlobToken = globToken!;
    }

    public ShellSyntaxToken GlobToken { get; }

    /// <summary>Returns <see langword="true"/> for <c>**</c>, which matches across directory separators.</summary>
    public bool IsRecursive => GlobToken.Kind == ShellSyntaxKind.AsteriskAsteriskToken;

    /// <summary>Returns <see langword="true"/> for a bracket expression such as <c>[abc]</c> or <c>[!a-z]</c>.</summary>
    public bool IsBracketExpression => GlobToken.Kind == ShellSyntaxKind.BracketExpressionToken;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitGlob(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitGlob(this);

    private static string GetFullText(ShellSyntaxToken globToken)
    {
        ArgumentNullException.ThrowIfNull(globToken);

        return globToken.ToFullString();
    }
}
