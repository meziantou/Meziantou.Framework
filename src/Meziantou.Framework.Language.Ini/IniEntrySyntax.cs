using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Ini;

/// <summary>Anything that can appear as a top-level INI document entry.</summary>
public abstract class IniEntrySyntax : IniSyntaxNode
{
    private protected IniEntrySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
