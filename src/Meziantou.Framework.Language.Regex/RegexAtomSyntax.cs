using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex;

/// <summary>The base of a single unquantified unit of a pattern, such as a literal, a class, or a group.</summary>
public abstract class RegexAtomSyntax : RegexTermSyntax
{
    private protected RegexAtomSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
