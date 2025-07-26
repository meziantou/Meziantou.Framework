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

    private static readonly PromptResult Default = new(PromptConfigurationMode.MergeTool, TimeSpan.Zero, PromptConfigurationScope.CurrentSnapshot);

    public bool MustRegisterUriScheme { get; set; }
    public bool MustStartNotificationTray { get; set; } = true;
    public bool MustShowDialog { get; set; }

    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported()
    {
        return IsWindowsVistaOrAbove() && GetExePath(PromptExeFileNameWithoutExtension) is not null;

        static bool IsWindowsVistaOrAbove()
        {
#if NETSTANDARD2_0 || NET472 || NET48
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
            return Default;

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
            throw new InvalidOperationException("An exception occurred while running " + psi.FileName + ".\n" + process.StandardOutput.ReadToEnd());

        var json = process.StandardOutput.ReadToEnd();
        return JsonSerializer.Deserialize<PromptResult>(json) ?? Default;
    }

    private static string? GetExePath(string fileName)
    {
        string? exeLocation;
        var dllLocation = typeof(TaskDialogPrompt).Assembly.Location;
        if (dllLocation is not null)
        {
            exeLocation = Path.Combine(dllLocation, fileName);
            if (File.Exists(exeLocation))
                return exeLocation;
        }

        exeLocation = Path.Combine(Environment.CurrentDirectory, fileName);
        if (File.Exists(exeLocation))
            return exeLocation;

#if DEBUG_TaskDialogPrompt
        //Debugger.Launch();
        //Debugger.Break();

        foreach (var configuration in new[] { "release", "debug" })
        {
            var pathFromRoot = Path.Combine("artifacts", "bin", Path.GetFileNameWithoutExtension(fileName), configuration, fileName);
            if (dllLocation is not null)
            {
                var root = FindParentDirectoryByName(Path.GetDirectoryName(dllLocation)!, "Meziantou.Framework");
                if (root is not null)
                {
                    exeLocation = Path.GetFullPath(Path.Combine(root, pathFromRoot));
                    if (File.Exists(exeLocation))
                        return exeLocation;
                }
            }

            {
                var root = FindParentDirectoryByName(Environment.CurrentDirectory, "Meziantou.Framework");
                if (root is not null)
                {
                    exeLocation = Path.GetFullPath(Path.Combine(root, pathFromRoot));
                    if (File.Exists(exeLocation))
                        return exeLocation;
                }
            }

            {
                var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
                if (!string.IsNullOrEmpty(root))
                {
                    exeLocation = Path.GetFullPath(Path.Combine(root, pathFromRoot));
                    if (File.Exists(exeLocation))
                        return exeLocation;
                }
            }

            {
                var mfCurrentDirectory = Environment.GetEnvironmentVariable("MF_CurrentDirectory");
                if (!string.IsNullOrEmpty(mfCurrentDirectory))
                {
                    var root = FindParentDirectoryByName(mfCurrentDirectory, "Meziantou.Framework");
                    if (root is not null)
                    {
                        exeLocation = Path.GetFullPath(Path.Combine(root, pathFromRoot));
                        if (File.Exists(exeLocation))
                            return exeLocation;
                    }
                }
            }

            exeLocation = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, pathFromRoot));
            if (File.Exists(exeLocation))
                return exeLocation;
        }

        throw new InvalidOperationException($"""
            Cannot find the executable.
            Current directory: {Environment.CurrentDirectory}
            DLL location: {dllLocation}
            EXE location: {exeLocation}
            GITHUB_WORKSPACE: {Environment.GetEnvironmentVariable("GITHUB_WORKSPACE")}
            """);
#else
        return null;
#endif
    }

    private static void StartNotificationTray()
    {
        var path = GetExePath(NotificationTrayExeFileNameWithoutExtension);
        if (path is not null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = false,
            };

            var process = Process.Start(psi);
            process?.Dispose();
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
