namespace Meziantou.Framework.InlineSnapshotTesting.Serialization;

internal sealed class ReadableSnapshotSerializer : SnapshotSerializer
{
    internal static ReadableSnapshotSerializer Instance { get; } = new();

    private ReadableSnapshotSerializer()
    {
    }

    public override string Serialize(object? value)
    {
        if(value is null)
            return "<null>";

        if(value is string s)
            return s;

        return YamlSnapshotSerializer.Instance.Serialize(value);
    }
}
