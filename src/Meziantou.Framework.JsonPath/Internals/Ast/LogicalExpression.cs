namespace Meziantou.Framework.Json.Internals;

/// <summary>Base class for logical expressions used in filter selectors.</summary>
internal abstract class LogicalExpression
{
    public abstract LogicalExpressionKind Kind { get; }
}
