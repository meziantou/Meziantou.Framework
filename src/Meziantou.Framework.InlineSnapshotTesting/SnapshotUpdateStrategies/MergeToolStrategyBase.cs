namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal abstract class MergeToolStrategyBase : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

    /// <summary>Starts the merge tool, or returns null when diff tools are switched off for this run.</summary>
    protected static MergeToolResult? TryLaunchMergeTool(InlineSnapshotSettings settings, string currentFilePath, string newFilePath)
    {
        var process = InlineSnapshotTesting.MergeTool.Launch(settings.MergeTools, currentFilePath, newFilePath);
        if (process is not null)
            return process;

        // Diff tools are switched off for this run (DiffEngine_Disabled, or a detected build server, continuous
        // testing or LLM environment). Leaving the file alone lets the caller report the snapshot difference,
        // which is far more useful than an exception about a merge tool the user deliberately turned off.
        if (InlineSnapshotTesting.MergeTool.IsDisabled())
            return null;

        throw new InlineSnapshotException($"Cannot start a merge tool. None of the configured merge tools could be started. Configure '{nameof(InlineSnapshotSettings)}.{nameof(InlineSnapshotSettings.MergeTools)}', or use '{nameof(SnapshotUpdateStrategy)}.{nameof(SnapshotUpdateStrategy.Overwrite)}' to update snapshots without a merge tool.");
    }
}
