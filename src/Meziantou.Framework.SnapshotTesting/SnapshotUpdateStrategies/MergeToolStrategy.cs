namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal sealed class MergeToolStrategy : MergeToolStrategyBase
{
    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(SnapshotSettings settings, string path) => true;
    public override void UpdateFile(SnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        var placeholder = VerifiedFilePlaceholder.TryCreate(currentFilePath);
        try
        {
            using (LaunchMergeTool(settings, currentFilePath, newFilePath))
            {
            }
        }
        catch
        {
            placeholder?.DeleteIfUnused();
            throw;
        }

        // The merge tool runs without blocking the test, so it is still open and expects to write to the
        // verified file when the developer saves. The placeholder can only be cleaned up once the process is
        // over.
        placeholder?.DeleteOnProcessExitIfUnused();
    }
}
