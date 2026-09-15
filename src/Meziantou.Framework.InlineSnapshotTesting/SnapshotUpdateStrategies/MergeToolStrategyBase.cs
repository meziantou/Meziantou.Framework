using System.Collections.Concurrent;
using Meziantou.Framework.InlineSnapshotTesting.MergeTools;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal abstract class MergeToolStrategyBase : SnapshotUpdateStrategy
{
    private static readonly ConcurrentQueue<string> FilesToDeleteOnProcessExit = new();
    private static int s_processExitHandlerRegistered;

    public override bool CanUpdateSnapshot(InlineSnapshotSettings settings, string path, string? expectedSnapshot, string? actualSnapshot) => true;
    public override bool MustReportError(InlineSnapshotSettings settings, string path) => true;

    /// <summary>
    /// Starts the merge tool, or returns <see langword="null" /> when merge tools are switched off for this run
    /// (<c>DiffEngine_Disabled</c>, no <see cref="InlineSnapshotSettings.MergeTools" />, or a detected build server,
    /// continuous testing or LLM environment when <see cref="InlineSnapshotSettings.AutoDetectContinuousEnvironment" />
    /// is set). Leaving the file alone lets the caller report the snapshot difference, which is far more useful than an
    /// exception about a merge tool the user deliberately turned off.
    /// </summary>
    protected static MergeToolResult? TryLaunchMergeTool(InlineSnapshotSettings settings, string currentFilePath, string newFilePath, bool waitForMerge)
    {
        if (settings.MergeTools is null || !settings.MergeTools.Any() || InlineSnapshotTesting.MergeTool.IsDisabled(settings.AutoDetectContinuousEnvironment))
            return null;

        var failures = new List<MergeToolLaunchFailure>();
        var result = InlineSnapshotTesting.MergeTool.Launch(settings.MergeTools, currentFilePath, newFilePath, waitForMerge, failures);
        if (result is not null)
            return result;

        throw CreateCannotStartMergeToolException(currentFilePath, newFilePath, failures);
    }

    /// <summary>
    /// Deletes the file when the test process exits. A merge tool whose launcher does not wait for the merge may still
    /// be showing the file.
    /// </summary>
    private protected static void DeleteFileOnProcessExit(string path)
    {
        FilesToDeleteOnProcessExit.Enqueue(path);
        if (Interlocked.Exchange(ref s_processExitHandlerRegistered, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += DeletePendingFiles;
        }
    }

    private static void DeletePendingFiles(object? sender, EventArgs e)
    {
        while (FilesToDeleteOnProcessExit.TryDequeue(out var path))
        {
            TryDeleteFile(path);
        }
    }

    private static InlineSnapshotException CreateCannotStartMergeToolException(string currentFilePath, string newFilePath, List<MergeToolLaunchFailure> failures)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Snapshots do not match, and no merge tool could be started to review the difference.");
        sb.AppendLine();
        sb.Append("  * Source file:  ").AppendLine(currentFilePath);
        sb.Append("    Updated file: ").AppendLine(newFilePath);

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
        sb.AppendLine("  - Compare the source file with the updated file listed above, which contains the new snapshot.");
        sb.AppendLine($"  - To use a merge tool, configure '{nameof(InlineSnapshotSettings)}.{nameof(InlineSnapshotSettings.MergeTools)}', set the 'DiffEngine_Tool' environment variable to the name of a '{nameof(InlineSnapshotTesting.MergeTool)}' property, or configure 'merge.tool' or 'diff.tool' with a 'cmd' in the git configuration.");
        sb.AppendLine("  - To report snapshot differences without starting a merge tool, set the 'DiffEngine_Disabled' environment variable to 'true'.");
        sb.AppendLine($"  - To update snapshots without a merge tool, re-run the test with INLINESNAPSHOTTESTING_STRATEGY=Overwrite, or set '{nameof(InlineSnapshotSettings)}.{nameof(InlineSnapshotSettings.SnapshotUpdateStrategy)}' to '{nameof(SnapshotUpdateStrategy)}.{nameof(Overwrite)}'.");

        var message = sb.ToString().TrimEnd();
        return failures.Count switch
        {
            0 => new InlineSnapshotException(message),
            1 => new InlineSnapshotException(message, failures[0].Exception),
            _ => new InlineSnapshotException(message, new AggregateException(failures.Select(static failure => failure.Exception))),
        };
    }
}
