namespace Meziantou.Framework.Json.Internals;

/// <summary>Root AST node for a parsed JSONPath query: $ followed by zero or more segments.</summary>
internal sealed class JsonPathExpression
{
    public JsonPathExpression(Segment[] segments)
    {
        Segments = segments;
    }

    public Segment[] Segments { get; }
}
