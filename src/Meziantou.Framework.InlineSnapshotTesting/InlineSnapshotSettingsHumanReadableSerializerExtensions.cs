using Meziantou.Framework.HumanReadable;
using Meziantou.Framework.InlineSnapshotTesting.Serialization;

namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>
/// Provides extension methods for configuring the human-readable serializer on <see cref="InlineSnapshotSettings"/>.
/// </summary>
public static class InlineSnapshotSettingsHumanReadableSerializerExtensions
{
    /// <summary>Configures the settings to use the human-readable serializer with default options.</summary>
    public static void UseHumanReadableSerializer(this InlineSnapshotSettings settings)
    {
        if (settings.SnapshotSerializer is not HumanReadableSnapshotSerializer)
        {
            settings.SnapshotSerializer = new HumanReadableSnapshotSerializer();
        }
    }

    /// <summary>Configures the settings to use the human-readable serializer with the specified options.</summary>
    public static void UseHumanReadableSerializer(this InlineSnapshotSettings settings, HumanReadableSerializerOptions options)
    {
        settings.SnapshotSerializer = new HumanReadableSnapshotSerializer(options);
    }

    /// <summary>Configures the settings to use the human-readable serializer with options configured by the specified action.</summary>
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
