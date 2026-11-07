using Meziantou.Framework.Language.InternalSyntax;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

internal sealed partial class CssConditionChainSyntax
{
    /// <summary>The operands of a chain are separated by <c>and</c> or <c>or</c>, whichever the chain is made of.</summary>
    /// <remarks>
    /// The keyword has a space on each side: without one before it, <c>(a)and</c> would still read, but without one
    /// after it, <c>and(b)</c> would read as a function.
    /// </remarks>
    internal override GreenNode? CreateSeparator(int index)
    {
        if (index != 0)
            return base.CreateSeparator(index);

        var space = SyntaxFactory.Trivia(SyntaxKind.WhitespaceTrivia, " ");
        return new GreenToken((int)SyntaxKind.IdentToken, Kind == SyntaxKind.AndCondition ? "and" : "or", space, space, isMissing: false);
    }
}
