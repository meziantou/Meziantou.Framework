using Meziantou.Framework.Json;

namespace Meziantou.Framework.ExternalJsonNavigatorTests;

public sealed class ExternalJsonNode
{
    private ExternalJsonNode(JsonPathNodeKind kind)
    {
        Kind = kind;
    }

    public JsonPathNodeKind Kind { get; }

    public Dictionary<string, ExternalJsonNode?> Properties { get; } = new(StringComparer.Ordinal);

    public LinkedList<ExternalJsonNode?> Items { get; } = new();

    public string? StringValue { get; private init; }

    public double NumberValue { get; private init; }

    public bool BooleanValue { get; private init; }

    public static ExternalJsonNode CreateObject(params (string Name, ExternalJsonNode? Value)[] properties)
    {
        var result = new ExternalJsonNode(JsonPathNodeKind.Object);
        foreach (var (name, value) in properties)
        {
            result.Properties.Add(name, value);
        }

        return result;
    }

    public static ExternalJsonNode CreateArray(params ExternalJsonNode?[] items)
    {
        var result = new ExternalJsonNode(JsonPathNodeKind.Array);
        foreach (var item in items)
        {
            result.Items.AddLast(item);
        }

        return result;
    }

    public static ExternalJsonNode CreateString(string value) => new(JsonPathNodeKind.String) { StringValue = value };

    public static ExternalJsonNode CreateNumber(double value) => new(JsonPathNodeKind.Number) { NumberValue = value };

    public static ExternalJsonNode CreateBoolean(bool value) => new(JsonPathNodeKind.Boolean) { BooleanValue = value };
}
