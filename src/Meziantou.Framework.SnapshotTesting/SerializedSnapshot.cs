namespace Meziantou.Framework.SnapshotTesting;

public sealed class SerializedSnapshot(IReadOnlyList<SnapshotData> data)
{
    public IReadOnlyList<SnapshotData> Data { get; } = data ?? throw new ArgumentNullException(nameof(data));
}
