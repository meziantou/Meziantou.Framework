using System.ComponentModel;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32;

/// <summary>
/// Provides a modern Vista+ style folder browser dialog using the native Windows IFileOpenDialog COM interface.
/// </summary>
/// <example>
/// <code>
/// var dialog = new OpenFolderDialog
/// {
///     Title = "Select a folder",
///     InitialDirectory = @"C:\Users"
/// };
/// 
/// if (dialog.ShowDialog() == DialogResult.OK)
/// {
///     Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
/// }
/// </code>
/// </example>
[SupportedOSPlatform("windows")]
public sealed class OpenFolderDialog
{
    /// <summary>Shows the folder browser dialog.</summary>
    /// <returns>A <see cref="DialogResult"/> value indicating the user action.</returns>
    public DialogResult ShowDialog()
    {
        return ShowDialog(IntPtr.Zero);
    }

    /// <summary>Shows the folder browser dialog with the specified owner window.</summary>
    /// <param name="owner">A handle to the window that owns the dialog.</param>
    /// <returns>A <see cref="DialogResult"/> value indicating the user action.</returns>
    public DialogResult ShowDialog(IntPtr owner) // IWin32Window
    {
        var hwndOwner = owner != IntPtr.Zero ? owner : NativeMethods.GetActiveWindow();
        var dialog = (IFileOpenDialog)new NativeFileOpenDialog();
        Configure(dialog);

        var hr = dialog.Show(hwndOwner);
        if (hr == NativeMethods.ERROR_CANCELLED)
            return DialogResult.Cancel;

        if (hr != NativeMethods.S_OK)
            return DialogResult.Abort;

        dialog.GetResult(out var item);
        item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out var path);
        SelectedPath = path;
        return DialogResult.OK;
    }

    /// <summary>Gets or sets the text displayed in the title bar of the dialog.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the text displayed on the OK button.</summary>
    public string? OkButtonLabel { get; set; }

    /// <summary>Gets or sets the initial directory displayed by the dialog.</summary>
    public string? InitialDirectory { get; set; }

    /// <summary>Gets or sets the path selected by the user.</summary>
    public string? SelectedPath { get; set; }

    /// <summary>Gets or sets a value indicating whether the dialog changes the current working directory when a folder is selected.</summary>
    public bool ChangeCurrentDirectory { get; set; }

    private void Configure(IFileOpenDialog dialog)
    {
        dialog.SetOptions(CreateOptions());

        if (!string.IsNullOrEmpty(InitialDirectory))
        {
            var result = NativeMethods.SHCreateItemFromParsingName(InitialDirectory, IntPtr.Zero, typeof(IShellItem).GUID, out var item);
            switch (result)
            {
                case NativeMethods.S_OK:
                    if (item is not null)
                    {
                        dialog.SetFolder(item);
                    }

                    break;
                case NativeMethods.FILE_NOT_FOUND:
                    break;
                default:
                    throw new Win32Exception(result);
            }
        }

        if (Title is not null)
        {
            dialog.SetTitle(Title);
        }

        if (OkButtonLabel is not null)
        {
            dialog.SetOkButtonLabel(OkButtonLabel);
        }
    }

    private FOS CreateOptions()
    {
        var result = FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PICKFOLDERS;
        if (!ChangeCurrentDirectory)
        {
            result |= FOS.FOS_NOCHANGEDIR;
        }

        return result;
    }
}
