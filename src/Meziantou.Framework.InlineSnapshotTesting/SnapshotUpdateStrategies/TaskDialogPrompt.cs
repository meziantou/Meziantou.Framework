#if WINDOWS
using Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal sealed class TaskDialogPrompt : Prompt
{
    // https://github.com/dotnet/samples/blob/main/windowsforms/TaskDialogDemo/cs/Form1.cs
    public override PromptResult Ask(PromptContext context)
    {
        var expirationPeriod = TimeSpan.FromHours(1);
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
            Text = $"File: {context.FilePath}",
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
            Footnote = context?.ProcessName != null ?
                $"This choice is persisted for this instance of {context.ProcessName}" :
                $"This choice is persisted for {expirationPeriod.TotalMinutes:0} minutes",
        };

        //var mainWindow = context == null ? 0 : GetMainWindowHandle(context.ProcessId);
        var result = TaskDialog.ShowDialog(page, TaskDialogStartupLocation.CenterScreen);

        var mode = PromptConfigurationMode.Default;
        if (result == btnDisallow || result == TaskDialogButton.Cancel)
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

        return new PromptResult(mode, expirationPeriod, rememberForAllFiles.Checked);
    }
}
#endif
