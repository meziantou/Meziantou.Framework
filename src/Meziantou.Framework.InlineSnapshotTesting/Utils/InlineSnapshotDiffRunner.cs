using System.Diagnostics;
using DiffEngine;
using EmptyFiles;
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
internal static partial class InlineSnapshotDiffRunner
{
    internal static bool IsDisable()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_Disabled");
        return string.Equals(variable, "true", StringComparison.OrdinalIgnoreCase) ||
               BuildServerDetector.Detected ||
               ContinuousTestingDetector.Detected;
    }

    private static DiffTool? GetDiffToolFromEnvironment()
    {
        var variable = Environment.GetEnvironmentVariable("DiffEngine_Tool");
        if (Enum.TryParse<DiffTool>(variable, ignoreCase: true, out var tool))
            return tool;

        return null;
    }

    private static DiffTool? GetDiffToolFromCurrentProcess()
    {
        var process = ProcessInfo.GetContextProcess();
        if (process == null)
            return null;

        var name = process.ProcessName;
        if (string.Equals(name, "devenv", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "devenv.exe", StringComparison.OrdinalIgnoreCase))
            return DiffTool.VisualStudio;

        if (string.Equals(name, "code", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "code.exe", StringComparison.OrdinalIgnoreCase))
            return DiffTool.VisualStudioCode;

        if (string.Equals(name, "rider64", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "rider64.exe", StringComparison.OrdinalIgnoreCase))
            return DiffTool.Rider;

        return null;
    }

    public static bool Disabled { get; set; } = IsDisable();

    public static Process? Launch(DiffTool tool, string tempFile, string targetFile)
    {
        return InnerLaunch(
             ([NotNullWhen(true)] out ResolvedTool? resolved) => DiffTools.TryFindByName(tool, out resolved),
             tempFile,
             targetFile);
    }

    public static Process? Launch(string tempFile, string targetFile)
    {
        return InnerLaunch(
              ([NotNullWhen(true)] out ResolvedTool? tool) =>
              {
                  var env = GetDiffToolFromEnvironment();
                  if (env != null)
                      return DiffTools.TryFindByName(env.Value, out tool);

                  var processTool = GetDiffToolFromCurrentProcess();
                  if(processTool != null)
                      return DiffTools.TryFindByName(processTool.Value, out tool);

                  var extension = FileExtensions.GetExtension(tempFile);
                  return DiffTools.TryFindByExtension(extension, out tool);
              },
              tempFile,
              targetFile);
    }

    private static Process? InnerLaunch(TryResolveTool tryResolveTool, string tempFile, string targetFile)
    {
        if (ShouldExitLaunch(tryResolveTool, targetFile, out var tool))
            return null;

        var arguments = tool.GetArguments(targetFile, tempFile);
        return LaunchProcess(tool, arguments);
    }

    private static bool ShouldExitLaunch(TryResolveTool tryResolveTool, string targetFile, [NotNullWhen(false)] out ResolvedTool? tool)
    {
        if (Disabled)
        {
            tool = null;
            return true;
        }

        if (!tryResolveTool(out tool))
            return true;

        if (!TryCreate(tool, targetFile))
            return true;

        return false;
    }

    private static bool TryCreate(ResolvedTool tool, string targetFile)
    {
        var targetExists = File.Exists(targetFile);
        if (tool.RequiresTarget && !targetExists)
        {
            if (!AllFiles.TryCreateFile(targetFile, useEmptyStringForTextFiles: true))
                return false;
        }

        return true;
    }

    private static Process LaunchProcess(ResolvedTool tool, string arguments)
    {
        var startInfo = new ProcessStartInfo(tool.ExePath, arguments)
        {
            UseShellExecute = true,
        };

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process != null)
                return process;

            throw new InlineSnapshotException($"Failed to launch diff tool: {tool.ExePath} {arguments}");
        }
        catch (Exception exception)
        {
            process?.Dispose();
            throw new InlineSnapshotException($"Failed to launch diff tool: {tool.ExePath} {arguments}", exception);
        }
    }

    private delegate bool TryResolveTool([NotNullWhen(true)] out ResolvedTool? resolved);
}
