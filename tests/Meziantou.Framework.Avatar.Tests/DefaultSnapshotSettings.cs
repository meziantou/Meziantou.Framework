using System.Runtime.CompilerServices;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Tests;

internal static class DefaultSnapshotSettings
{
    [ModuleInitializer]
    public static void Initialize()
    {
        SnapshotSettings.Default = SnapshotSettings.Default with
        {
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
        };
    }
}
