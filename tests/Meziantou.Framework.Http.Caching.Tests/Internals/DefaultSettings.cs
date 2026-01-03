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
        };
    }
}
