using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>Anything that can appear where JSON expects a value.</summary>
public abstract class JsonValueSyntax : JsonSyntaxNode
{
    private protected JsonValueSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
