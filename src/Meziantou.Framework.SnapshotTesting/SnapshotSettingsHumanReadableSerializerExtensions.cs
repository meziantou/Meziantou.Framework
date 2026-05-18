using Meziantou.Framework.HumanReadable;

namespace Meziantou.Framework.SnapshotTesting;

public static class SnapshotSettingsHumanReadableSerializerExtensions
{
    extension(SnapshotSettings settings)
    {
        public void ConfigureHumanReadableSerializer(Action<HumanReadableSerializerOptions>? options)
        {
            if (options is null)
                return;

            var serializers = settings.Serializers.ToList();
            var index = serializers.FindIndex(serializer => serializer is HumanReadableSnapshotSerializer);
            if (index < 0)
                return;

            var serializer = (HumanReadableSnapshotSerializer)serializers[index];
            var clone = new HumanReadableSnapshotSerializer(serializer.Options with { });
            options(clone.Options);

            serializers[index] = clone;
            settings.Serializers.Clear();
            foreach (var item in serializers)
            {
                settings.Serializers.Add(item);
            }
        }

        public void AddConverter(HumanReadableConverter converter)
        {
            settings.ConfigureHumanReadableSerializer(options =>
            {
                options.Converters.Add(converter);
            });
        }
    }
}
