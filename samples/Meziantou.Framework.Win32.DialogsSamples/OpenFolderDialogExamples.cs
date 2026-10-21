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
}
