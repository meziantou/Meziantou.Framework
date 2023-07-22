namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class BlockingDiffToolStrategy : MergeToolStrategyBase
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string expectSnapshot, string actualSnapshot) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;
    public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile)
    {
        using var process = LaunchMergeTool(settings, tempFile, targetFile);
        process.WaitForExit();
        File.Delete(tempFile);
    }
}