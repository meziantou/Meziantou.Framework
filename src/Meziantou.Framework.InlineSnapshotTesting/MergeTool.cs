using DiffEngine;

using Meziantou.Framework.InlineSnapshotTesting.MergeTools;

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

    private static bool IsDisable()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_Disabled");
        return string.Equals(variable, "true", StringComparison.OrdinalIgnoreCase) ||
               BuildServerDetector.Detected ||
               ContinuousTestingDetector.Detected;
    }

    internal static MergeToolResult? Launch(IEnumerable<MergeTool?>? mergeTools, string currentFilePath, string newFilePath)
    {
        if (IsDisable())
            return null;

        foreach (var mergeTool in mergeTools)
        {
            if (mergeTool is null)
                continue;

            var process = mergeTool.Start(currentFilePath, newFilePath);
            if (process is not null)
                return process;
        }

        return null;
    }

    private protected static string CopyFileToTemp(string path)
    {
        var temp = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(temp);
        var filePath = Path.Combine(temp, Path.GetFileName(path));
        File.Copy(path, filePath, overwrite: false);
        return filePath;
    }
}
