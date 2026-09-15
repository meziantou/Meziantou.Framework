using Meziantou.Framework.DiffEngine;
using Meziantou.Framework.InlineSnapshotTesting.MergeTools;
using Meziantou.Framework.InlineSnapshotTesting.Utils;
using Meziantou.Framework.LLMContext;

namespace Meziantou.Framework.InlineSnapshotTesting;

/// <summary>Represents a merge tool that can be used to compare and update snapshots.</summary>
public abstract class MergeTool
{
    public static MergeTool AraxisMerge { get; } = new DiffEngineTool(DiffTool.AraxisMerge);
    public static MergeTool BeyondCompare { get; } = new DiffEngineTool(DiffTool.BeyondCompare);
    public static MergeTool Cursor { get; } = new DiffEngineTool(DiffTool.Cursor);
    public static MergeTool DeltaWalker { get; } = new DiffEngineTool(DiffTool.DeltaWalker);
    public static MergeTool Diffinity { get; } = new DiffEngineTool(DiffTool.Diffinity);
    public static MergeTool ExamDiff { get; } = new DiffEngineTool(DiffTool.ExamDiff);
    public static MergeTool Guiffy { get; } = new DiffEngineTool(DiffTool.Guiffy);
    public static MergeTool Kaleidoscope { get; } = new DiffEngineTool(DiffTool.Kaleidoscope);
    public static MergeTool KDiff3 { get; } = new DiffEngineTool(DiffTool.KDiff3);
    public static MergeTool Meld { get; } = new DiffEngineTool(DiffTool.Meld);
    public static MergeTool MsExcelDiff { get; } = new DiffEngineTool(DiffTool.MsExcelDiff);
    public static MergeTool MsWordDiff { get; } = new DiffEngineTool(DiffTool.MsWordDiff);
    public static MergeTool Neovim { get; } = new DiffEngineTool(DiffTool.Neovim);
    public static MergeTool P4Merge { get; } = new DiffEngineTool(DiffTool.P4Merge);
    public static MergeTool Rider { get; } = new DiffEngineTool(DiffTool.Rider);
    public static MergeTool SublimeMerge { get; } = new DiffEngineTool(DiffTool.SublimeMerge);
    public static MergeTool TkDiff { get; } = new DiffEngineTool(DiffTool.TkDiff);
    public static MergeTool TortoiseGitIDiff { get; } = new DiffEngineTool(DiffTool.TortoiseGitIDiff);
    public static MergeTool TortoiseGitMerge { get; } = new DiffEngineTool(DiffTool.TortoiseGitMerge);
    public static MergeTool TortoiseIDiff { get; } = new DiffEngineTool(DiffTool.TortoiseIDiff);
    public static MergeTool TortoiseMerge { get; } = new DiffEngineTool(DiffTool.TortoiseMerge);
    public static MergeTool Vim { get; } = new DiffEngineTool(DiffTool.Vim);
    public static MergeTool VisualStudio { get; } = new DiffEngineTool(DiffTool.VisualStudio);
    public static MergeTool VisualStudioCode { get; } = new DiffEngineTool(DiffTool.VisualStudioCode);
    public static MergeTool VisualStudioMerge { get; } = new VisualStudioMergeTool();
    public static MergeTool WinMerge { get; } = new DiffEngineTool(DiffTool.WinMerge);

    public static MergeTool RiderIfCurrentProcess { get; } = new MergeToolIfCurrentProcess(Rider, ["rider64", "rider64.exe"]);
    public static MergeTool VisualStudioIfCurrentProcess { get; } = new MergeToolIfCurrentProcess(VisualStudio, ["devenv", "devenv.exe"]);
    public static MergeTool VisualStudioCodeIfCurrentProcess { get; } = new MergeToolIfCurrentProcess(VisualStudioCode, ["code", "code.exe"]);
    public static MergeTool VisualStudioMergeIfCurrentProcess { get; } = new MergeToolIfCurrentProcess(VisualStudioMerge, ["devenv", "devenv.exe"]);

    public static MergeTool DiffToolFromEnvironmentVariable { get; } = new MergeToolFromEnvironment();
    public static MergeTool GitDiffTool { get; } = new GitDiffTool();
    public static MergeTool GitMergeTool { get; } = new GitMergeTool();

    /// <summary>Starts the merge tool to compare the current file with the new file.</summary>
    /// <param name="currentFilePath">The path to the current snapshot file.</param>
    /// <param name="newFilePath">The path to the new snapshot file.</param>
    /// <returns>A <see cref="MergeToolResult"/> that represents the merge tool process, or null if the tool cannot be started.</returns>
    public abstract MergeToolResult? Start(string currentFilePath, string newFilePath);

    /// <summary>
    /// Starts the merge tool. <paramref name="waitForMerge" /> asks the tool to keep its process running until the
    /// developer is done with the merge, for the tools that need an extra argument to do so.
    /// </summary>
    internal virtual MergeToolResult? Start(string currentFilePath, string newFilePath, bool waitForMerge) => Start(currentFilePath, newFilePath);

    /// <summary>
    /// Indicates whether merge tools must not be started. <c>DiffEngine_Disabled</c> always wins. The build server,
    /// continuous testing and LLM detections only apply when <paramref name="autoDetectContinuousEnvironment" /> is
    /// set, so that turning <see cref="InlineSnapshotSettings.AutoDetectContinuousEnvironment" /> off re-enables them.
    /// </summary>
    internal static bool IsDisabled(bool autoDetectContinuousEnvironment)
    {
        if (IsDisabledByEnvironmentVariable())
            return true;

        return autoDetectContinuousEnvironment &&
               (BuildServerDetector.Detected || ContinuousTestingDetector.Detected || LLMEnvironmentDetector.Detected);
    }

    internal static bool IsDisabledByEnvironmentVariable()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_Disabled")?.Trim();
        return string.Equals(variable, "true", StringComparison.OrdinalIgnoreCase) || variable is "1";
    }

    /// <summary>
    /// Starts the first merge tool that can be started. A tool that fails to start - a stale executable, an invalid
    /// <c>DiffEngine_&lt;Tool&gt;</c> variable, a broken git command - must not prevent the next ones from being
    /// tried, so its exception is recorded in <paramref name="failures" /> and the search goes on.
    /// </summary>
    internal static MergeToolResult? Launch(IEnumerable<MergeTool?>? mergeTools, string currentFilePath, string newFilePath, bool waitForMerge, List<MergeToolLaunchFailure> failures)
    {
        if (mergeTools is null)
            return null;

        foreach (var mergeTool in mergeTools)
        {
            if (mergeTool is null)
                continue;

            try
            {
                var result = mergeTool.Start(currentFilePath, newFilePath, waitForMerge);
                if (result is not null)
                    return result;
            }
            catch (Exception exception)
            {
                failures.Add(new MergeToolLaunchFailure(mergeTool, exception));
            }
        }

        return null;
    }

    /// <summary>
    /// Copies a file into a new directory of its own under the temporary directory, so the copy keeps the name - and
    /// therefore the extension and syntax highlighting - of the original. Remove it with <see cref="DeleteTemporaryCopy" />.
    /// </summary>
    private protected static FullPath CopyFileToTemp(string path)
    {
        var sourcePath = FullPath.FromPath(path);
        var tempDirectory = FullPath.GetTempPath() / Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(tempDirectory);
        var filePath = tempDirectory / sourcePath.Name;
        try
        {
            File.Copy(sourcePath, filePath, overwrite: false);
        }
        catch
        {
            DeleteTemporaryCopy(filePath);
            throw;
        }

        return filePath;
    }

    /// <summary>Deletes a copy made by <see cref="CopyFileToTemp" />, including the directory created for it.</summary>
    private protected static void DeleteTemporaryCopy(FullPath path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Exists)
            {
                fileInfo.TrySetReadOnly(false);
            }

            var directory = path.Parent;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
