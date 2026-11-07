using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>One of the simple selectors a compound selector is made of, such as a type, a class, or a pseudo-class.</summary>
public abstract class CssSimpleSelectorSyntax : CssSyntaxNode
{
    private protected CssSimpleSelectorSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
