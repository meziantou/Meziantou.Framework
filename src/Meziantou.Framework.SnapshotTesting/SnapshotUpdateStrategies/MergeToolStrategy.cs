namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal sealed class MergeToolStrategy : MergeToolStrategyBase
{
    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(SnapshotSettings settings, string path) => true;
    public override void UpdateFile(SnapshotSettings settings, string verifiedFilePath, string actualFilePath)
    {
        var placeholder = VerifiedFilePlaceholder.TryCreate(verifiedFilePath);
        MergeToolResult? result;
        try
        {
            result = TryLaunchMergeTool(settings, verifiedFilePath, actualFilePath, waitForMerge: false);
        }
        catch
        {
            placeholder?.DeleteIfUnused();
            throw;
        }

        if (result is null)
        {
            // Merge tools are switched off: the assertion failure reports the difference.
            placeholder?.DeleteIfUnused();
            return;
        }

        // Releasing the result does not stop the merge tool, and the cleanup it registered, such as deleting the
        // temporary copy of the verified file, still runs when the tool exits.
        result.Dispose();

        // The merge tool runs without blocking the test, so it is still open and expects to write to the
        // verified file when the developer saves. The placeholder can only be cleaned up once the process is
        // over.
        placeholder?.DeleteOnProcessExitIfUnused();
    }
}
