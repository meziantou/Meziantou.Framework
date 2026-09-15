using Meziantou.Framework.SnapshotTesting.MergeTools;

namespace Meziantou.Framework.SnapshotTesting.SnapshotUpdateStrategies;

internal abstract class MergeToolStrategyBase : SnapshotUpdateStrategy
{
    public override bool CanUpdateSnapshot(SnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(SnapshotSettings settings, string path) => true;

    /// <summary>
    /// Starts a merge tool for each snapshot to update. The verified files the assertion no longer produces are not
    /// deleted: the developer reviews the change in the merge tool and may reject it, and the assertion failure
    /// already lists them.
    /// </summary>
    public override void UpdateFiles(SnapshotSettings settings, IReadOnlyList<SnapshotUpdateFile> filesToUpdate, IReadOnlyList<string> filesToDelete)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filesToUpdate);
        ArgumentNullException.ThrowIfNull(filesToDelete);

        foreach (var fileToUpdate in filesToUpdate)
        {
            UpdateFile(settings, fileToUpdate.VerifiedFilePath, fileToUpdate.ActualFilePath);
        }
    }

    /// <summary>
    /// Starts the merge tool, or returns <see langword="null" /> when merge tools are switched off for this run, in which
    /// case the caller leaves the files alone and the snapshot difference is reported as a regular assertion failure.
    /// </summary>
    protected static MergeToolResult? TryLaunchMergeTool(SnapshotSettings settings, string currentFilePath, string newFilePath, bool waitForMerge)
    {
        if (SnapshotTesting.MergeTool.IsDisabled(settings.AutoDetectContinuousEnvironment))
            return null;

        var failures = new List<MergeToolLaunchFailure>();
        var result = SnapshotTesting.MergeTool.Launch(settings.MergeTools, currentFilePath, newFilePath, waitForMerge, failures);
        if (result is not null)
            return result;

        throw CreateCannotStartMergeToolException(currentFilePath, newFilePath, failures);
    }

    private static SnapshotException CreateCannotStartMergeToolException(string currentFilePath, string newFilePath, List<MergeToolLaunchFailure> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Snapshots do not match, and no merge tool could be started to review the difference.");
        sb.AppendLine();
        sb.Append("  * Verified: ").AppendLine(currentFilePath);
        sb.Append("    Actual:   ").AppendLine(newFilePath);

        if (failures.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Merge tools that failed to start:");
            foreach (var failure in failures)
            {
                sb.Append("  - ").Append(failure.Tool).Append(": ").AppendLine(failure.Exception.Message);
            }
        }

        sb.AppendLine();
        sb.AppendLine("Resolution guidance:");
        sb.AppendLine("  - Compare the Verified and Actual files listed above. If the new behavior is correct, copy the Actual file to the Verified file.");
        sb.AppendLine($"  - To use a merge tool, configure '{nameof(SnapshotSettings)}.{nameof(SnapshotSettings.MergeTools)}', set the 'DiffEngine_Tool' environment variable to the name of a '{nameof(SnapshotTesting.MergeTool)}' property, or configure 'merge.tool' or 'diff.tool' with a 'cmd' in the git configuration.");
        sb.AppendLine("  - To report snapshot differences without starting a merge tool, set the 'DiffEngine_Disabled' environment variable to 'true'.");
        sb.AppendLine($"  - To update snapshots without a merge tool, re-run the test with SNAPSHOTTESTING_STRATEGY=Overwrite, or set '{nameof(SnapshotSettings)}.{nameof(SnapshotSettings.SnapshotUpdateStrategy)}' to '{nameof(SnapshotUpdateStrategy)}.{nameof(Overwrite)}'.");

        var message = sb.ToString().TrimEnd();
        return failures.Count switch
        {
            0 => new SnapshotException(message),
            1 => new SnapshotException(message, failures[0].Exception),
            _ => new SnapshotException(message, new AggregateException(failures.Select(static failure => failure.Exception))),
        };
    }
}
