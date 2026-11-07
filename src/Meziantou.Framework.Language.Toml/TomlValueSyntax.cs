using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>Anything that can appear where TOML expects a value.</summary>
public abstract class TomlValueSyntax : TomlSyntaxNode
{
    private protected TomlValueSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
