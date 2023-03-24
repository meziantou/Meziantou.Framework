using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed class TaskDialogPrompt : Prompt
{
    private const string PromptExeFileNameWithoutExtension = "Meziantou.Framework.InlineSnapshotTesting.Prompt.TaskDialog.exe";
    private const string NotificationTrayExeFileNameWithoutExtension = "Meziantou.Framework.InlineSnapshotTesting.Prompt.NotificationTray.exe";

    private const string UriScheme = "meziantou-inlinesnapshot";
    private const string FriendlyName = "Meziantou.Framework.InlineSnapshot";

    public bool MustRegisterUriScheme { get; set; }
    public bool MustStartNotificationTray { get; set; } = true;
    public bool MustShowDialog { get; set; }

    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported()
    {
        return IsWindowsVistaOrAbove() && GetExePath(PromptExeFileNameWithoutExtension) != null;

        static bool IsWindowsVistaOrAbove()
        {
#if NETSTANDARD2_0
            return Environment.OSVersion.Platform == PlatformID.Win32NT && Environment.OSVersion.Version.Major >= 6;
#else
            return OperatingSystem.IsWindowsVersionAtLeast(6);
#endif
        }
    }

    public override void OnSnapshotChanged()
    {
        if (MustRegisterUriScheme)
        {
            RegisterUriScheme();
        }

        if (MustStartNotificationTray)
        {
            StartNotificationTray();
        }
    }

    // https://github.com/dotnet/samples/blob/main/windowsforms/TaskDialogDemo/cs/Form1.cs
    public override PromptResult Ask(PromptContext context)
    {
        if (!MustShowDialog)
            return new PromptResult(PromptConfigurationMode.MergeTool, TimeSpan.Zero, PromptConfigurationScope.CurrentSnapshot);

        var path = GetExePath(PromptExeFileNameWithoutExtension);
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

    private static string? GetExePath(string fileName)
    {
        string? exeLocation;
        var dllLocation = typeof(TaskDialogPrompt).Assembly.Location;
        if (dllLocation != null)
        {
            exeLocation = Path.Combine(dllLocation, fileName);
            if (File.Exists(exeLocation))
                return exeLocation;
        }

        exeLocation = Path.Combine(Environment.CurrentDirectory, fileName);
        if (File.Exists(exeLocation))
            return exeLocation;

#if DEBUG_TaskDialogPrompt
        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var pathFromRoot = Path.Combine("src", Path.GetFileNameWithoutExtension(fileName), "bin", configuration, "net6.0-windows", fileName);
            if (dllLocation != null)
            {
                var root = FindParentDirectoryByName(Path.GetDirectoryName(exeLocation), "Meziantou.Framework");
                if (root != null)
                {
                    exeLocation = Path.GetFullPath(Path.Combine(root, pathFromRoot));
                    if (File.Exists(exeLocation))
                        return exeLocation;
                }
            }

            {
                var root = FindParentDirectoryByName(Environment.CurrentDirectory, "Meziantou.Framework");
                if (root != null)
                {
                    exeLocation = Path.GetFullPath(Path.Combine(root, pathFromRoot));
                    if (File.Exists(exeLocation))
                        return exeLocation;
                }
            }

            exeLocation = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, pathFromRoot));
            if (File.Exists(exeLocation))
                return exeLocation;
        }
#endif

        return null;
    }

    private static void StartNotificationTray()
    {
        var path = GetExePath(NotificationTrayExeFileNameWithoutExtension);
        if (path != null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
            };

            Process.Start(psi);
        }
    }

    private static void RegisterUriScheme()
    {
        if (IsSupported())
        {
            var applicationLocation = GetExePath(NotificationTrayExeFileNameWithoutExtension);

            using var key = Registry.CurrentUser.CreateSubKey("SOFTWARE\\Classes\\" + UriScheme);
            key.SetValue("", "URL:" + FriendlyName);
            key.SetValue("URL Protocol", "");

            using var defaultIcon = key.CreateSubKey("DefaultIcon");
            defaultIcon.SetValue("", applicationLocation + ",1");

            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey.SetValue("", "\"" + applicationLocation + "\" \"%1\"");
        }
    }

#if DEBUG_TaskDialogPrompt
    private static string? FindParentDirectoryByName(string currentFolder, string name)
    {
        do
        {
            var folderName = Path.GetFileName(currentFolder);
            if (string.Equals(folderName, name, StringComparison.OrdinalIgnoreCase))
                return currentFolder;

            currentFolder = Path.GetDirectoryName(currentFolder)!;
        }
        while (!string.IsNullOrEmpty(currentFolder));

        return null;
    }
#endif
}
