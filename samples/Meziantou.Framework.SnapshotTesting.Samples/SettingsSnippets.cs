namespace Meziantou.Framework.SnapshotTesting.Samples;

/// <summary>The examples of docs/14-SnapshotTesting that change the global settings, so they cannot run as tests.</summary>
public static class SettingsSnippets
{
    public static void ForceUpdateSnapshots()
    {
        #region Snapshot_ForceUpdateSnapshots
        SnapshotSettings.Default.ForceUpdateSnapshots = true; // Rewrite the snapshots, even the ones that match
        SnapshotSettings.Default.SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure;
        #endregion
    }

    public static void MergeToolSync()
    {
        #region Snapshot_MergeTools
        SnapshotSettings.Default.SnapshotUpdateStrategy = SnapshotUpdateStrategy.MergeToolSync;
        SnapshotSettings.Default.MergeTools = [MergeTool.BeyondCompare, MergeTool.WinMerge, MergeTool.VisualStudioCode];
        #endregion
    }
}
