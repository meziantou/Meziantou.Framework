namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal sealed class BlockingDiffToolStrategy : MergeToolStrategyBase
{
    public override bool ReuseTemporaryFile => false;

    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;

    public override bool MustReportError(SnapshotSettings settings, string path) => true;

    public override void UpdateFile(SnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        var placeholder = VerifiedFilePlaceholder.TryCreate(currentFilePath);
        try
        {
            using var process = LaunchMergeTool(settings, currentFilePath, newFilePath);
            process.WaitForExit();
        }
        finally
        {
            // The merge tool has exited, so a placeholder that is still untouched means the developer closed
            // the tool without saving.
            placeholder?.DeleteIfUnused();
        }
    }
}
