using Meziantou.Framework.HumanReadable;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

public sealed class HumanReadableSnapshotSerializer : SnapshotSerializer
{
    internal static HumanReadableSnapshotSerializer Instance { get; } = new();

    private HumanReadableSnapshotSerializer()
    {
    }

    public override string Serialize(object? value)
    {
        return HumanReadableSerializer.Serialize(value);
    }
}
