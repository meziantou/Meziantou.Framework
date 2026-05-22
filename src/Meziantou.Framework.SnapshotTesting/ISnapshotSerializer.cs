using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.SnapshotTesting;

public interface ISnapshotSerializer
{
    bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result);
}
