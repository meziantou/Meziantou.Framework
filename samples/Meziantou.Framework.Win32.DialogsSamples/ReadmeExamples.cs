using System.Runtime.Versioning;

namespace Meziantou.Framework.Win32.DialogsSamples;

// Examples of the readme (src/Meziantou.Framework.Win32.Dialogs/readme.md) that do not need an owner window
internal static class ReadmeExamples
{
    [SupportedOSPlatform("windows6.0.6000")]
    public static void BasicUsage()
    {
        #region OpenFolderDialog_Basic
        var dialog = new OpenFolderDialog();
        var result = dialog.ShowDialog();
        if (result == DialogResult.OK)
        {
            Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
        }
        #endregion
    }

    [SupportedOSPlatform("windows6.0.6000")]
    public static void AdvancedUsage()
    {
        #region OpenFolderDialog_Advanced
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder",
            OkButtonLabel = "Choose",
            InitialDirectory = @"C:\Users",
            ChangeCurrentDirectory = false,
        };

        var result = dialog.ShowDialog();
        if (result == DialogResult.OK)
        {
            Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
        }
        #endregion
    }
}
