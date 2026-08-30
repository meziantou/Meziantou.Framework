using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;
using Windows.Win32;

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
        var hwndOwner = owner != IntPtr.Zero ? owner : (IntPtr)PInvoke.GetActiveWindow();
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
    /// <remarks>
    /// The value is resolved by the Windows shell, so it accepts a file system path as well as a
    /// shell location such as <c>shell:Downloads</c>. A path that uses <c>/</c> as a separator is
    /// normalized before being resolved. When the value cannot be resolved — it does not exist, it
    /// is malformed, or it names a drive that is not currently mounted — it is ignored and the
    /// dialog opens on the folder the shell would have chosen by default.
    /// </remarks>
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
        dialog.SetOptions(CreateOptions(ChangeCurrentDirectory));

        if (!string.IsNullOrEmpty(InitialDirectory))
        {
            var shellItem = TryCreateShellItem(InitialDirectory);
            if (shellItem is not null)
            {
                dialog.SetFolder(shellItem);
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

    /// <summary>Resolves a path or shell location to a shell item, or <see langword="null"/> when the shell cannot parse it.</summary>
    internal static IShellItem? TryCreateShellItem(string path)
    {
        if (TryParse(path, out var shellItem))
        {
            return shellItem;
        }

        // The shell parser only accepts the canonical Windows form. "C:/Users" names an existing
        // directory as far as .NET is concerned but the shell rejects it, so retry once normalized.
        if (TryGetFullPath(path, out var fullPath) && !string.Equals(fullPath, path, StringComparison.Ordinal) && TryParse(fullPath, out shellItem))
        {
            return shellItem;
        }

        return null;

        static bool TryParse(string value, [NotNullWhen(true)] out IShellItem? item)
        {
            var hr = PInvoke.SHCreateItemFromParsingName(value, null, out IShellItem? result);
            item = (int)hr == NativeMethods.S_OK ? result : null;
            return item is not null;
        }

        static bool TryGetFullPath(string value, [NotNullWhen(true)] out string? fullPath)
        {
            try
            {
                fullPath = Path.GetFullPath(value);
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                fullPath = null;
                return false;
            }
        }
    }

    internal static FOS CreateOptions(bool changeCurrentDirectory)
    {
        var result = FOS.FOS_FORCEFILESYSTEM | FOS.FOS_PICKFOLDERS;
        if (!changeCurrentDirectory)
        {
            result |= FOS.FOS_NOCHANGEDIR;
        }

        return result;
    }
}
