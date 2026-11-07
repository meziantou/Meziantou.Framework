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

            // Two concurrent calls, such as test classes adding converters to SnapshotSettings.Default from their
            // constructors, must both apply. Cloning the options of a serializer read outside the lock would let the
            // second call start from the options without the first converter.
            settings.Serializers.ReplaceFirst<HumanReadableSnapshotSerializer>(serializer =>
            {
                var clone = new HumanReadableSnapshotSerializer(serializer.Options with { });
                options(clone.Options);
                return clone;
            });
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
