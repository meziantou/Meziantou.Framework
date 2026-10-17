using System.Runtime.CompilerServices;
using Meziantou.Framework.SnapshotTesting;
using Meziantou.Framework.SnapshotTesting.Imaging;

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
        SnapshotSettings.Default.Comparers.AddImageComparer();
    }
}