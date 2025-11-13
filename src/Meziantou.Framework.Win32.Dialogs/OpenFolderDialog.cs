using System.ComponentModel;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32;

/// <summary>Provides a modern Windows folder selection dialog using the IFileOpenDialog COM interface.</summary>
/// <example>
/// <code>
/// var dialog = new OpenFolderDialog
/// {
///     Title = "Select a folder",
///     InitialDirectory = @"C:\Users",
///     OkButtonLabel = "Select Folder"
/// };
/// 
/// if (dialog.ShowDialog() == DialogResult.OK)
/// {
///     Console.WriteLine($"Selected folder: {dialog.SelectedPath}");
/// }
/// </code>
/// </example>
/// <remarks>
/// This dialog provides a modern Windows folder picker experience similar to the one used in
/// File Explorer. It is only supported on Windows platforms.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class OpenFolderDialog
{
    /// <summary>Shows the folder selection dialog.</summary>
    /// <returns>A <see cref="DialogResult"/> indicating whether the user clicked OK, Cancel, or if the operation was aborted.</returns>
    public DialogResult ShowDialog()
    {
        return ShowDialog(IntPtr.Zero);
    }

    /// <summary>Shows the folder selection dialog with the specified owner window.</summary>
    /// <param name="owner">The handle to the owner window (HWND). Use <see cref="IntPtr.Zero"/> for no owner.</param>
    /// <returns>A <see cref="DialogResult"/> indicating whether the user clicked OK, Cancel, or if the operation was aborted.</returns>
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

    /// <summary>Gets or sets the title text displayed in the dialog's title bar.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets the label text for the OK button.</summary>
    public string? OkButtonLabel { get; set; }

    /// <summary>Gets or sets the initial directory to display when the dialog opens.</summary>
    public string? InitialDirectory { get; set; }

    /// <summary>Gets the path of the folder selected by the user. This property is populated after <see cref="ShowDialog()"/> returns <see cref="DialogResult.OK"/>.</summary>
    public string? SelectedPath { get; set; }

    /// <summary>Gets or sets a value indicating whether to change the current working directory to the selected folder.</summary>
    /// <value>
    /// <see langword="true"/> to change the current directory to the selected folder; 
    /// <see langword="false"/> to preserve the current working directory. The default is <see langword="false"/>.
    /// </value>
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
