namespace Meziantou.Framework.SnapshotTesting;

public interface ISnapshotSerializer
{
    IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value);
}

