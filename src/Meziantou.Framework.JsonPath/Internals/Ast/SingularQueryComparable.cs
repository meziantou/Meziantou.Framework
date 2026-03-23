namespace Meziantou.Framework.Json.Internals;

/// <summary>A singular query used as a comparable in a filter expression.</summary>
internal sealed class SingularQueryComparable : Comparable
{
    public SingularQueryComparable(SingularQuery query)
    {
        Query = query;
    }

    public override ComparableKind Kind => ComparableKind.SingularQuery;

    public SingularQuery Query { get; }
}
