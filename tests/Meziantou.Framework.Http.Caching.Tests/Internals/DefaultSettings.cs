using System.Runtime.CompilerServices;
using Meziantou.Framework.InlineSnapshotTesting;

namespace Meziantou.Framework.Http.Caching.Tests.Internals;

internal static class DefaultSettings
{
    [ModuleInitializer]
    public static void ConfigureSnapshot()
    {
        InlineSnapshotSettings.Default = InlineSnapshotSettings.Default with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            //SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool,
        };
        InlineSnapshotSettings.Default.UseHumanReadableSerializer(settings =>
        {
            settings.PropertyOrder = StringComparer.OrdinalIgnoreCase;
            settings.DictionaryKeyOrder = StringComparer.OrdinalIgnoreCase;
        });
    }
}
