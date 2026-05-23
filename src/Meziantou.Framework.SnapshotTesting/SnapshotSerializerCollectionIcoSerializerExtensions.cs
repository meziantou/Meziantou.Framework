namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotSerializerCollectionIcoSerializerExtensions
{
    public static void AddIcoSerializer(this SnapshotSerializerCollection serializers)
    {
        serializers.Add(IcoSnapshotSerializer.Instance);
    }
}
