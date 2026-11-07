using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>Anything that can appear as a top-level TOML document entry.</summary>
public abstract class TomlEntrySyntax : TomlSyntaxNode
{
    private protected TomlEntrySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
