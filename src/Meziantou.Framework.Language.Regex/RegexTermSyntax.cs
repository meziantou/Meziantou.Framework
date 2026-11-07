using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex;

/// <summary>The base of anything that can appear as one element of a <see cref="RegexSequenceSyntax"/>.</summary>
public abstract class RegexTermSyntax : RegexSyntaxNode
{
    private protected RegexTermSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
