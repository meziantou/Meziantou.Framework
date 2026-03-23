namespace Meziantou.Framework.Json.Internals;

/// <summary>Base class for all selector types in a JSONPath segment.</summary>
internal abstract class Selector
{
    public abstract SelectorKind Kind { get; }
}
