#if INLINE_SNAPSHOT_TESTING
using SnapshotSettingsType = Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings;
#else
using SnapshotSettingsType = Meziantou.Framework.SnapshotTesting.SnapshotSettings;
#endif

#if INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
#else
namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;
#endif

internal sealed class AlwaysWithoutFailureStrategy : SnapshotUpdateStrategy
{
    public override bool ReuseTemporaryFile => false;

    public override bool CanUpdateSnapshot(SnapshotSettingsType settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;

    public override bool MustReportError(SnapshotSettingsType settings, string path) => false;

    public override void UpdateFile(SnapshotSettingsType settings, string currentFilePath, string newFilePath)
    {
        MoveFile(newFilePath, currentFilePath);
    }
}
