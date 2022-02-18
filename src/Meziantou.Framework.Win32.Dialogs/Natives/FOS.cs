namespace Meziantou.Framework.Win32.Natives
{
    [Flags]
    internal enum FOS : uint
    {
        /// <summary>
        /// When saving a file, prompt before overwriting an existing file of the same name. This is a default value for the Save dialog.
        /// </summary>
        FOS_OVERWRITEPROMPT = 0x00000002,

        /// <summary>
        /// In the Save dialog, only allow the user to choose a file that has one of the file name extensions specified through IFileDialog::SetFileTypes.
        /// </summary>
        FOS_STRICTFILETYPES = 0x00000004,

        /// <summary>
        /// Don't change the current working directory.
        /// </summary>
        FOS_NOCHANGEDIR = 0x00000008,

        /// <summary>
        /// Present an Open dialog that offers a choice of folders rather than files.
        /// </summary>
        FOS_PICKFOLDERS = 0x00000020,

        /// <summary>
        /// Ensures that returned items are file system items (SFGAO_FILESYSTEM). Note that this does not apply to items returned by IFileDialog::GetCurrentSelection.
        /// </summary>
        FOS_FORCEFILESYSTEM = 0x00000040,

        /// <summary>
        /// Enables the user to choose any item in the Shell namespace, not just those with SFGAO_STREAM or SFAGO_FILESYSTEM attributes. This flag cannot be combined with FOS_FORCEFILESYSTEM.
        /// </summary>
        FOS_ALLNONSTORAGEITEMS = 0x00000080,

        /// <summary>
        /// Do not check for situations that would prevent an application from opening the selected file, such as sharing violations or access denied errors.
        /// </summary>
        FOS_NOVALIDATE = 0x00000100,

        /// <summary>
        /// Enables the user to select multiple items in the open dialog. Note that when this flag is set, the IFileOpenDialog interface must be used to retrieve those items.
        /// </summary>
        FOS_ALLOWMULTISELECT = 0x00000200,

        /// <summary>
        /// The item returned must be in an existing folder. This is a default value.
        /// </summary>
        FOS_PATHMUSTEXIST = 0x00000800,

        /// <summary>
        /// The item returned must exist. This is a default value for the Open dialog.
        /// </summary>
        FOS_FILEMUSTEXIST = 0x00001000,

        /// <summary>
        /// Prompt for creation if the item returned in the save dialog does not exist. Note that this does not actually create the item.
        /// </summary>
        FOS_CREATEPROMPT = 0x00002000,

        /// <summary>
        /// In the case of a sharing violation when an application is opening a file, call the application back through OnShareViolation for guidance. This flag is overridden by FOS_NOVALIDATE.
        /// </summary>
        FOS_SHAREAWARE = 0x00004000,

        /// <summary>
        /// Do not return read-only items. This is a default value for the Save dialog.
        /// </summary>
        FOS_NOREADONLYRETURN = 0x00008000,

        /// <summary>
        /// Do not test whether creation of the item as specified in the Save dialog will be successful. If this flag is not set, the calling application must handle errors, such as denial of access, discovered when the item is created.
        /// </summary>
        FOS_NOTESTFILECREATE = 0x00010000,

        /// <summary>
        /// Hide the list of places from which the user has recently opened or saved items. This value is not supported as of Windows 7.
        /// </summary>
        FOS_HIDEMRUPLACES = 0x00020000,

        /// <summary>
        /// Hide items shown by default in the view's navigation pane. This flag is often used in conjunction with the IFileDialog::AddPlace method, to hide standard locations and replace them with custom locations.
        /// </summary>
        FOS_HIDEPINNEDPLACES = 0x00040000,

        /// <summary>
        /// Shortcuts should not be treated as their target items. This allows an application to open a .lnk file rather than what that file is a shortcut to.
        /// </summary>
        FOS_NODEREFERENCELINKS = 0x00100000,

        /// <summary>
        /// Do not add the item being opened or saved to the recent documents list (SHAddToRecentDocs).
        /// </summary>
        FOS_DONTADDTORECENT = 0x02000000,

        /// <summary>
        /// Include hidden and system items.
        /// </summary>
        FOS_FORCESHOWHIDDEN = 0x10000000,

        /// <summary>
        /// Indicates to the Save As dialog box that it should open in expanded mode. Expanded mode is the mode that is set and unset by clicking the button in the lower-left corner of the Save As dialog box that switches between Browse Folders and Hide Folders when clicked. This value is not supported as of Windows 7.
        /// </summary>
        FOS_DEFAULTNOMINIMODE = 0x20000000,

        /// <summary>
        /// Indicates to the Open dialog box that the preview pane should always be displayed.
        /// </summary>
        FOS_FORCEPREVIEWPANEON = 0x40000000,

        /// <summary>
        /// Indicates that the caller is opening a file as a stream (BHID_Stream), so there is no need to download that file.
        /// </summary>
        FOS_SUPPORTSTREAMABLEITEMS = 0x80000000,
    }
}
