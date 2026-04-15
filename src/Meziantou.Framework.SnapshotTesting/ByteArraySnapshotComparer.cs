namespace Meziantou.Framework.SnapshotTesting;

public sealed class ByteArraySnapshotComparer : ISnapshotComparer
{
    public static ByteArraySnapshotComparer Default { get; } = new();

    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return expected.Data.AsSpan().SequenceEqual(actual.Data);
    }
}

