namespace Meziantou.Framework.SnapshotTesting;

public interface ISnapshotComparer
{
    bool Equals(SnapshotData expected, SnapshotData actual);
}

