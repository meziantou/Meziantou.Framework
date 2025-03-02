namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

public abstract class SnapshotSerializer
{
    public abstract string? Serialize(object? value);
}
