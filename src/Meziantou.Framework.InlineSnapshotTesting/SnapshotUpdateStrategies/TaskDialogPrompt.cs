using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class TaskDialogPrompt : Prompt
{
    private static string? GetExePath()
    {
        const string ExeFileName = "Meziantou.Framework.InlineSnapshotTesting.Prompt.TaskDialog.exe";

        string? exeLocation;
        var dllLocation = typeof(TaskDialogPrompt).Assembly.Location;
        if (dllLocation != null)
        {
            exeLocation = Path.Combine(dllLocation, ExeFileName);
            if (File.Exists(exeLocation))
                return exeLocation;
        }

        exeLocation = Path.Combine(Environment.CurrentDirectory, ExeFileName);
        if (File.Exists(exeLocation))
            return exeLocation;

#if DEBUG
        if (dllLocation != null)
        {
            var root = FindParentDirectoryByName(Path.GetDirectoryName(exeLocation), "Meziantou.Framework");
            if (root != null)
            {
                exeLocation = Path.GetFullPath(Path.Combine(root, "src", "Meziantou.Framework.InlineSnapshotTesting.Prompt.TaskDialog", "bin", "Debug", "net6.0-windows", ExeFileName));
                if (File.Exists(exeLocation))
                    return exeLocation;
            }
        }

        {
            var root = FindParentDirectoryByName(Environment.CurrentDirectory, "Meziantou.Framework");
            if (root != null)
            {
                exeLocation = Path.GetFullPath(Path.Combine(root, "src", "Meziantou.Framework.InlineSnapshotTesting.Prompt.TaskDialog", "bin", "Debug", "net6.0-windows", ExeFileName));
                if (File.Exists(exeLocation))
                    return exeLocation;
            }
        }
#endif

        return null;
    }

    public static bool IsSupported()
    {
        return IsWindowsVistaOrAbove() && GetExePath() != null;

        static bool IsWindowsVistaOrAbove()
        {
#if NETSTANDARD2_0
            return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 6;
#else
            return OperatingSystem.IsWindowsVersionAtLeast(6);
#endif
        }
    }

    // https://github.com/dotnet/samples/blob/main/windowsforms/TaskDialogDemo/cs/Form1.cs
    public override PromptResult Ask(PromptContext context)
    {
        var path = GetExePath();
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = CommandLineBuilder.WindowsQuotedArguments(path ?? "", context.ProcessName ?? "", context.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? ""),
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Cannot start the process " + psi.FileName);
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("An exception occurred while running " + psi.FileName);

        var json = process.StandardOutput.ReadToEnd();
        return JsonSerializer.Deserialize<PromptResult>(json);
    }

#if DEBUG
    private static string? FindParentDirectoryByName(string currentFolder, string name)
    {
        do
        {
            var folderName = Path.GetFileName(currentFolder);
            if (folderName == name)
                return currentFolder;

            currentFolder = Path.GetDirectoryName(currentFolder)!;
        }
        while (!string.IsNullOrEmpty(currentFolder));

        return null;
    }
#endif
}
