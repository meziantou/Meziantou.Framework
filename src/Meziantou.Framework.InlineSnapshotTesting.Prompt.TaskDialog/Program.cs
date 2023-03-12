using System.Text.Json;
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

namespace Meziantou.Framework.InlineSnapshotTesting.Prompt;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var filePath = args[0];
        var processName = args[1];

        var result = GetResult(filePath, processName);
        var json = JsonSerializer.Serialize(result);
        Console.WriteLine(json);
    }

    private static PromptResult GetResult(string filePath, string processName)
    {
        var expirationPeriod = TimeSpan.FromHours(1);

#if DEBUG
        var envMode = Environment.GetEnvironmentVariable("Meziantou_Framework_InlineSnapshotTesting_Prompt_TaskDialog_Mode");
        var envScope = Environment.GetEnvironmentVariable("Meziantou_Framework_InlineSnapshotTesting_Prompt_TaskDialog_Scope");

        if (Enum.TryParse<PromptConfigurationMode>(envMode, ignoreCase: true, out var parsedMode) && Enum.TryParse<PromptConfigurationScope>(envMode, ignoreCase: true, out var parsedScope))
            return new PromptResult(parsedMode, expirationPeriod, parsedScope);
#endif

        Application.EnableVisualStyles();
        var btnDisallow = new TaskDialogCommandLinkButton("Do &not update the snapshot");
        var btnMerge = new TaskDialogCommandLinkButton("Open Merge &Tool");
        var btnOverwrite = new TaskDialogCommandLinkButton("&Overwrite the snapshot");
        var btnOverwriteWithoutError = new TaskDialogCommandLinkButton("Overwrite the snapshot &without notification");
        var rememberForAllFiles = new TaskDialogVerificationCheckBox()
        {
            Text = "&Apply to all snapshots",
        };

        var page = new TaskDialogPage()
        {
            Caption = "InlineSnapshot Configuration",
            Heading = "A snapshot must be updated. What do you want to do?",
            Text = $"File: {filePath}",
            Buttons =
            {
                btnDisallow,
                btnMerge,
                btnOverwrite,
                btnOverwriteWithoutError,
            },
            AllowCancel = true,
            Verification = rememberForAllFiles,
            Icon = TaskDialogIcon.Warning,
            DefaultButton = btnDisallow,
            Footnote = !string.IsNullOrEmpty(processName) ?
                $"This choice is persisted for this instance of {processName}" :
                $"This choice is persisted for {expirationPeriod.TotalMinutes:0} minutes",
        };

        var result = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);

        var scope = rememberForAllFiles.Checked ? PromptConfigurationScope.ParentProcess : PromptConfigurationScope.CurrentFile;
        var mode = PromptConfigurationMode.Default;
        if (result == TaskDialogButton.Cancel)
        {
            mode = PromptConfigurationMode.Disallow;
            expirationPeriod = TimeSpan.Zero; // Do not persist the choice
            scope = PromptConfigurationScope.CurrentSnapshot;
        }
        else if (result == btnDisallow)
        {
            mode = PromptConfigurationMode.Disallow;
        }
        else if (result == btnMerge)
        {
            mode = PromptConfigurationMode.MergeTool;
        }
        else if (result == btnOverwrite)
        {
            mode = PromptConfigurationMode.Overwrite;
        }
        else if (result == btnOverwriteWithoutError)
        {
            mode = PromptConfigurationMode.OverwriteWithoutFailure;
        }

        return new PromptResult(mode, expirationPeriod, scope);
    }
}