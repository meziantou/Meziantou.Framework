namespace Meziantou.Framework.SnapshotTesting;

public readonly record struct SnapshotType(string Type)
{
    public static SnapshotType Default { get; } = new("");
    public static SnapshotType Png { get; } = new("png");
}

