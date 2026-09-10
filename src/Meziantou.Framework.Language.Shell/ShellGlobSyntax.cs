namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a pathname-expansion metacharacter inside a word, such as <c>*</c> or <c>?</c>.</summary>
public sealed partial class ShellGlobSyntax
{
    /// <summary>Returns <see langword="true"/> for <c>**</c>, which matches across directory separators.</summary>
    public bool IsRecursive => GlobToken.Kind() == SyntaxKind.AsteriskAsteriskToken;

    /// <summary>Returns <see langword="true"/> for a bracket expression such as <c>[abc]</c> or <c>[!a-z]</c>.</summary>
    public bool IsBracketExpression => GlobToken.Kind() == SyntaxKind.BracketExpressionToken;

    /// <summary>Returns <see langword="true"/> when a zsh glob qualifier follows the pattern, as in <c>*(.)</c>.</summary>
    public bool HasQualifier => GlobToken.Text is [.., ')'];
}
