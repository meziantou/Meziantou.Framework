using System.Linq;

namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotSerializerCollectionIcoSerializerExtensions
{
    extension(SnapshotSerializerCollection serializers)
    {
        public void AddIcoSerializer()
        {
            if (serializers.Any(static serializer => serializer is IcoSnapshotSerializer))
                return;

            serializers.Add(IcoSnapshotSerializer.Instance);
        }
    }
}
