namespace Meziantou.Framework.Json.Internals;

internal sealed class SingularQuerySegment
{
    public SingularQuerySegment(string name)
    {
        Kind = SingularQuerySegmentKind.Name;
        Name = name;
    }

    public SingularQuerySegment(long index)
    {
        Kind = SingularQuerySegmentKind.Index;
        Index = index;
    }

    public SingularQuerySegmentKind Kind { get; }

    public string? Name { get; }

    public long Index { get; }
}
