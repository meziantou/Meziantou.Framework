namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class DisallowStrategy : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string expectSnapshot, string actualSnapshot) => false;

    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

    public override void UpdateFile(InlineSnapshotSettings settings, string targetFile, string tempFile) => throw new InvalidOperationException();
}
