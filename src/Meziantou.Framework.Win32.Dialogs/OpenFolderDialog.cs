using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

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
[SupportedOSPlatform("windows6.0.6000")]
public sealed class OpenFolderDialog
{
    /// <summary>S_OK.</summary>
    private const int Ok = 0;

    /// <summary>HRESULT_FROM_WIN32(ERROR_CANCELLED), returned by Show when the user dismisses the dialog.</summary>
    private const int ErrorCancelled = unchecked((int)0x800704C7);

    /// <summary>HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND), returned when the initial directory does not exist.</summary>
    private const int FileNotFound = unchecked((int)0x80070002);

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
        var hwndOwner = owner != IntPtr.Zero ? new HWND(owner) : PInvoke.GetActiveWindow();
        var dialog = (IFileOpenDialog)new FileOpenDialog();
        Configure(dialog);

        var hr = dialog.Show(hwndOwner);
        if (hr == ErrorCancelled)
            return DialogResult.Cancel;

        if (hr != Ok)
            return DialogResult.Abort;

        dialog.GetResult(out var item);
        SelectedPath = GetFileSystemPath(item);
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
            var result = PInvoke.SHCreateItemFromParsingName(InitialDirectory, null, out IShellItem? shellItem);
            switch ((int)result)
            {
                case Ok:
                    if (shellItem is not null)
                    {
                        dialog.SetFolder(shellItem);
                    }

                    break;
                case FileNotFound:
                    break;
                default:
                    throw new Win32Exception((int)result);
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

    private FILEOPENDIALOGOPTIONS CreateOptions()
    {
        var result = FILEOPENDIALOGOPTIONS.FOS_FORCEFILESYSTEM | FILEOPENDIALOGOPTIONS.FOS_PICKFOLDERS;
        if (!ChangeCurrentDirectory)
        {
            result |= FILEOPENDIALOGOPTIONS.FOS_NOCHANGEDIR;
        }

        return result;
    }

    /// <summary>Reads the file system path out of a shell item and frees the buffer the shell allocated for it.</summary>
    private static unsafe string GetFileSystemPath(IShellItem item)
    {
        PWSTR name = default;
        try
        {
            item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, &name);
            return name.ToString();
        }
        finally
        {
            Marshal.FreeCoTaskMem((IntPtr)name.Value);
        }
    }
}
