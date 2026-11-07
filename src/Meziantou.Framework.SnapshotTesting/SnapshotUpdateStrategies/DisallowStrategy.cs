namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal sealed class DisallowStrategy : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => false;

    public override bool MustReportError(SnapshotSettings settings, string path) => true;

    public override void UpdateFile(SnapshotSettings settings, string currentFilePath, string newFilePath)
        => throw new InvalidOperationException($"'{nameof(SnapshotUpdateStrategy)}.{nameof(SnapshotUpdateStrategy.Disallow)}' never writes to verified snapshot files. Set '{nameof(SnapshotSettings)}.{nameof(SnapshotSettings.SnapshotUpdateStrategy)}' to '{nameof(SnapshotUpdateStrategy.Overwrite)}' or '{nameof(SnapshotUpdateStrategy.OverwriteWithoutFailure)}' to update snapshots.");

    public override string ToString() => nameof(Disallow);
}
