using System.Linq;

namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotSerializerCollectionGifSerializerExtensions
{
    extension(SnapshotSerializerCollection serializers)
    {
        public void AddGifSerializer()
        {
            if (serializers.Any(static serializer => serializer is GifSnapshotSerializer))
                return;

            serializers.Add(GifSnapshotSerializer.Instance);
        }
    }
}
