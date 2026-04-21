namespace Meziantou.Framework.SnapshotTesting;

public interface ISnapshotSerializer
{
    bool CanSerialize(SnapshotType type, object? value);

    SerializedSnapshot Serialize(SnapshotType type, object? value);
}
