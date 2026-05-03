#if INLINE_SNAPSHOT_TESTING
using MergeToolType = Meziantou.Framework.InlineSnapshotTesting.MergeTool;
using SnapshotExceptionType = Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotException;
using SnapshotSettingsType = Meziantou.Framework.InlineSnapshotTesting.InlineSnapshotSettings;
#else
using MergeToolType = Meziantou.Framework.SnapshotTesting.MergeTool;
using SnapshotExceptionType = Meziantou.Framework.SnapshotTesting.SnapshotException;
using SnapshotSettingsType = Meziantou.Framework.SnapshotTesting.SnapshotSettings;
#endif

#if INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;
#else
namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;
#endif

internal abstract class MergeToolStrategyBase : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(SnapshotSettingsType settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(SnapshotSettingsType settings, string path) => true;

    protected static MergeToolResult LaunchMergeTool(SnapshotSettingsType settings, string currentFilePath, string newFilePath)
    {
        var process = MergeToolType.Launch(settings.MergeTools, currentFilePath, newFilePath);
        return process ?? throw new SnapshotExceptionType("Cannot start the merge tool");
    }
}
