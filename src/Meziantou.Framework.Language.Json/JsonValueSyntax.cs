namespace Meziantou.Framework.Language.Json;

/// <summary>Base type for all JSON value syntax nodes.</summary>
public abstract class JsonValueSyntax : JsonSyntaxNode
{
    protected JsonValueSyntax(JsonSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<JsonSyntaxToken>? tokens = null)
        : base(kind, fullText, fullStart, tokens)
    {
    }
}
