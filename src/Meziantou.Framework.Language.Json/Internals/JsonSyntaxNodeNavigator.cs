using Meziantou.Framework.Json;

namespace Meziantou.Framework.Language.Json.Internals;

internal sealed class JsonSyntaxNodeNavigator : JsonPathNavigator<JsonSyntaxNode>
{
    public static JsonSyntaxNodeNavigator Instance { get; } = new();

    private JsonSyntaxNodeNavigator()
    {
    }

    public override JsonPathNodeKind GetKind(JsonSyntaxNode? value)
    {
        return value switch
        {
            JsonDocumentSyntax document => GetKind(document.Value),
            JsonObjectSyntax => JsonPathNodeKind.Object,
            JsonArraySyntax => JsonPathNodeKind.Array,
            JsonStringSyntax => JsonPathNodeKind.String,
            JsonNumberSyntax => JsonPathNodeKind.Number,
            JsonLiteralSyntax literal => literal.Kind is JsonSyntaxKind.JsonTrueLiteral or JsonSyntaxKind.JsonFalseLiteral ? JsonPathNodeKind.Boolean : JsonPathNodeKind.Null,
            _ => JsonPathNodeKind.Null,
        };
    }

    public override bool TryGetPropertyValue(JsonSyntaxNode? value, string name, out JsonSyntaxNode? result)
    {
        if (value is JsonDocumentSyntax document)
            return TryGetPropertyValue(document.Value, name, out result);

        if (value is JsonObjectSyntax obj)
        {
            foreach (var member in obj.Members)
            {
                if (string.Equals(member.Name, name, StringComparison.Ordinal))
                {
                    result = member.Value;
                    return true;
                }
            }
        }

        result = null;
        return false;
    }

    public override IEnumerable<JsonPathProperty<JsonSyntaxNode>> GetProperties(JsonSyntaxNode? value)
    {
        if (value is JsonDocumentSyntax document)
        {
            value = document.Value;
        }

        if (value is not JsonObjectSyntax obj)
            yield break;

        foreach (var member in obj.Members)
        {
            yield return new JsonPathProperty<JsonSyntaxNode>(member.Name, member.Value);
        }
    }

    public override int GetArrayLength(JsonSyntaxNode? value)
    {
        if (value is JsonDocumentSyntax document)
            return GetArrayLength(document.Value);

        return value is JsonArraySyntax array ? array.Elements.Count : 0;
    }

    public override bool TryGetElement(JsonSyntaxNode? value, int index, out JsonSyntaxNode? result)
    {
        if (value is JsonDocumentSyntax document)
            return TryGetElement(document.Value, index, out result);

        if (value is JsonArraySyntax array && index >= 0 && index < array.Elements.Count)
        {
            result = array.Elements[index].Value;
            return true;
        }

        result = null;
        return false;
    }

    public override bool TryGetString(JsonSyntaxNode? value, out string? result)
    {
        if (value is JsonStringSyntax syntax)
        {
            result = syntax.Value;
            return true;
        }

        result = null;
        return false;
    }

    public override bool TryGetNumber(JsonSyntaxNode? value, out double result)
    {
        if (value is JsonNumberSyntax syntax && double.TryParse(syntax.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            return true;

        result = 0;
        return false;
    }

    public override bool TryGetBoolean(JsonSyntaxNode? value, out bool result)
    {
        if (value is JsonLiteralSyntax literal)
        {
            switch (literal.Kind)
            {
                case JsonSyntaxKind.JsonTrueLiteral:
                    result = true;
                    return true;
                case JsonSyntaxKind.JsonFalseLiteral:
                    result = false;
                    return true;
            }
        }

        result = false;
        return false;
    }
}
