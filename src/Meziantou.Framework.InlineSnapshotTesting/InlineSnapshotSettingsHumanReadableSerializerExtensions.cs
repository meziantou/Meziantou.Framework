using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting;

public static class InlineSnapshotSettingsHumanReadableSerializerExtensions
{
    public static void UseHumanReadableSerializer(this InlineSnapshotSettings settings)
    {
        if (settings.SnapshotSerializer is not HumanReadableSnapshotSerializer)
        {
            settings.SnapshotSerializer = new HumanReadableSnapshotSerializer();
        }
    }

    public static void UseHumanReadableSerializer(this InlineSnapshotSettings settings, HumanReadableSerializerOptions options)
    {
        settings.SnapshotSerializer = new HumanReadableSnapshotSerializer(options);
    }

    public static void UseHumanReadableSerializer(this InlineSnapshotSettings settings, Action<HumanReadableSerializerOptions>? configure)
    {
        // Preserve existing options if the current serializer is already HumanReadableSerializer
        if (settings.SnapshotSerializer is HumanReadableSnapshotSerializer existing)
        {
            var clone = existing.Options with { };
            configure(clone);
            settings.SnapshotSerializer = new HumanReadableSnapshotSerializer(clone);
        }
        else
        {
            settings.SnapshotSerializer = new HumanReadableSnapshotSerializer(configure);
        }
    }
}
