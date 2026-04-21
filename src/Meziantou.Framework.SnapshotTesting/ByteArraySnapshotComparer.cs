namespace Meziantou.Framework.SnapshotTesting;

internal sealed class ByteArraySnapshotComparer : ISnapshotComparer
{
    public static ByteArraySnapshotComparer Instance { get; } = new();

    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        return expected.Data.AsSpan().SequenceEqual(actual.Data);
    }
}

