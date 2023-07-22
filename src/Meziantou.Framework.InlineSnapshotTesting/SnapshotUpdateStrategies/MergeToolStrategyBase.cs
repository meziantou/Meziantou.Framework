using System.Diagnostics;
using Meziantou.Framework.InlineSnapshotTesting.Utils;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal abstract class MergeToolStrategyBase : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string expectSnapshot, string actualSnapshot) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

    protected static Process LaunchMergeTool(InlineSnapshotSettings settings, string targetFile, string tempFile)
    {
        var process = settings.MergeTool switch
        {
            null => InlineSnapshotDiffRunner.Launch(tempFile, targetFile),
            _ => InlineSnapshotDiffRunner.Launch(settings.MergeTool.GetValueOrDefault(), tempFile, targetFile),
        };

        return process ?? throw new InlineSnapshotException("Cannot start the merge tool");
    }
}
