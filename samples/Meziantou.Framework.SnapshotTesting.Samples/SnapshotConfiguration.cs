using System.Runtime.CompilerServices;

namespace Meziantou.Framework.SnapshotTesting.Samples;

#region Snapshot_ModuleInitializer
internal static class SnapshotConfiguration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Runs once, before any test of the assembly
        SnapshotSettings.Default.SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeTool;
        SnapshotSettings.Default.MergeTools = [MergeTool.VisualStudioCode];
        SnapshotSettings.Default.ScrubUserName();
    }
}
#endregion
