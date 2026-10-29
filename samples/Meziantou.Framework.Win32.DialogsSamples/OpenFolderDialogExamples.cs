using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32.DialogsSamples;

public static class OpenFolderDialogExamples
{
    [SupportedOSPlatform("windows6.0.6000")]
    public static void SelectFolder()
    {
        #region OpenFolderDialog_ShowDialog
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder",
            InitialDirectory = @"C:\Users",
            OkButtonLabel = "Select Folder",
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
        }
        #endregion
    }

    [SupportedOSPlatform("windows6.0.6000")]
    public static void ShellLocation()
    {
        #region OpenFolderDialog_ShellLocation
        var dialog = new OpenFolderDialog
        {
            // A path or a shell location. The dialog ignores a location that does not exist.
            InitialDirectory = "shell:Downloads",
        };

        dialog.ShowDialog();
        #endregion
    }

    [SupportedOSPlatform("windows6.0.6000")]
    public static void HandleErrors()
    {
        #region OpenFolderDialog_HandleErrors
        var dialog = new OpenFolderDialog();
        switch (dialog.ShowDialog())
        {
            case DialogResult.OK:
                Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
                break;

            case DialogResult.Cancel:
                // The user closed the dialog
                break;

            default:
                // The dialog could not be shown: LastHResult tells why
                throw Marshal.GetExceptionForHR(dialog.LastHResult) ?? new InvalidOperationException("The dialog failed");
        }
        #endregion
    }
}
