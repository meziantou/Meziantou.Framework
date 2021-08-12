using System;

namespace Meziantou.Framework.Globbing.Internals;

internal sealed class RecursiveMatchAllSegment : Segment
{
    private RecursiveMatchAllSegment()
    {
    }

    public static RecursiveMatchAllSegment Instance { get; } = new RecursiveMatchAllSegment();

    public override bool IsMatch(ref PathReader pathReader) => throw new NotSupportedException();

    public override bool IsRecursiveMatchAll => true;

    public override string ToString()
    {
        return "**";
    }
}
