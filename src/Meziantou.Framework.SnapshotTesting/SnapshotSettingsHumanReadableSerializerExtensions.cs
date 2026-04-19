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

            var serializer = settings.Serializers.OfType<HumanReadableSnapshotSerializer>().FirstOrDefault();
            if (serializer is not null)
            {
                options(serializer.Options);
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
