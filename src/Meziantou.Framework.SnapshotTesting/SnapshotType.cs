namespace Meziantou.Framework.SnapshotTesting;

public sealed record class SnapshotType(string Type)
{
    public static SnapshotType Default { get; } = new("default");
}

