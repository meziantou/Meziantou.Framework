using System.Runtime.CompilerServices;
using Meziantou.Framework.SnapshotTesting;

namespace Meziantou.Framework.Mermaid.Tests;

internal static class DefaultSnapshotSettings
{
    // Set MEZIANTOU_MERMAID_UPDATE_SNAPSHOTS=1 to write the snapshots on a developer machine, then review them
    private const string UpdateSnapshotsVariable = "MEZIANTOU_MERMAID_UPDATE_SNAPSHOTS";

    [ModuleInitializer]
    public static void Initialize()
    {
        SnapshotSettings.Default = Environment.GetEnvironmentVariable(UpdateSnapshotsVariable) is "1"
            ? SnapshotSettings.Default with
            {
                SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
                AutoDetectContinuousEnvironment = false,
            }
            : SnapshotSettings.Default with
            {
                SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow,
            };
    }
}
