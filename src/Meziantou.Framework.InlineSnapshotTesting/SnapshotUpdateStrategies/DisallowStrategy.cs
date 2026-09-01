namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class DisallowStrategy : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => false;

    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

    public override void UpdateFile(InlineSnapshotSettings settings, string currentFilePath, string newFilePath)
        => throw new InvalidOperationException($"'{nameof(SnapshotUpdateStrategy)}.{nameof(SnapshotUpdateStrategy.Disallow)}' never writes to source files. Set '{nameof(InlineSnapshotSettings)}.{nameof(InlineSnapshotSettings.SnapshotUpdateStrategy)}' to '{nameof(SnapshotUpdateStrategy.Overwrite)}' or '{nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure)}' to update snapshots.");
}
