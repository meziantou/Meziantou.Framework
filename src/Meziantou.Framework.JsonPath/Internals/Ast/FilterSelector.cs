namespace Meziantou.Framework.Json.Internals;

/// <summary>Filter selector: selects children matching a logical expression.</summary>
internal sealed class FilterSelector : Selector
{
    public FilterSelector(LogicalExpression expression)
    {
        Expression = expression;
    }

    public override SelectorKind Kind => SelectorKind.Filter;

    public LogicalExpression Expression { get; }
}
