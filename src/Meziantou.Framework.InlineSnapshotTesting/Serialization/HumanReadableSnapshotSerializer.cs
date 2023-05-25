using Meziantou.Framework.HumanReadable;

namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

public sealed class HumanReadableSnapshotSerializer : SnapshotSerializer
{
    private readonly HumanReadableSerializerOptions? _options;

    internal static HumanReadableSnapshotSerializer Instance { get; } = new(options: null);

    public HumanReadableSnapshotSerializer(HumanReadableSerializerOptions? options = null)
    {
        _options = options;
    }

    public override string Serialize(object? value)
    {
        return HumanReadableSerializer.Serialize(value, _options);
    }
}
