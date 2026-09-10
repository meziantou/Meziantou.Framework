namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a character class, <c>[abc]</c> or <c>[^a-z]</c>.</summary>
public sealed partial class RegexCharacterClassSyntax : RegexAtomSyntax
{
    public bool IsNegated => !CaretToken.IsKind(SyntaxKind.None);
}
