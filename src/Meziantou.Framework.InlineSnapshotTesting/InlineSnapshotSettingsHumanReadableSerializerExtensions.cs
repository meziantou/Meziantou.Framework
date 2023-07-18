using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting;

public static class InlineSnapshotSettingsHumanReadableSerializerExtensions
{
    public static void UseHumanReadableSerializer(this InlineSnapshotSettings settings)
    {
        settings.SnapshotSerializer = new HumanReadableSnapshotSerializer();
    }

    public static void UseHumanReadableSerializer(this InlineSnapshotSettings settings, HumanReadableSerializerOptions options)
    {
        settings.SnapshotSerializer = new HumanReadableSnapshotSerializer(options);
    }

    public static void UseHumanReadableSerializer(this InlineSnapshotSettings settings, Action<HumanReadableSerializerOptions>? configure)
    {
        settings.SnapshotSerializer = new HumanReadableSnapshotSerializer(configure);
    }
}
