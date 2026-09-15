namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotSerializerCollectionIcoSerializerExtensions
{
    public static void AddIcoSerializer(this SnapshotSerializerCollection serializers)
    {
        if (serializers.Any(static serializer => serializer is IcoSnapshotSerializer))
            return;

        serializers.Add(IcoSnapshotSerializer.Instance);
    }
}
