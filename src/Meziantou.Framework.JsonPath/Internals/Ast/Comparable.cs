namespace Meziantou.Framework.Json.Internals;

/// <summary>Base class for comparable values used in filter comparison expressions.</summary>
internal abstract class Comparable
{
    public abstract ComparableKind Kind { get; }
}
