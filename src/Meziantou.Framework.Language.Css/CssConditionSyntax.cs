using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Css;

/// <summary>A condition of a media query, a supports query, or a container query.</summary>
public abstract class CssConditionSyntax : CssSyntaxNode
{
    private protected CssConditionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
