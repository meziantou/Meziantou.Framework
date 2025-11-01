namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Specifies the notification types for file system events.</summary>
[Flags]
[SuppressMessage("Usage", "CA2217:Do not mark enums with FlagsAttribute", Justification = "Values from Windows definition")]
public enum PRJ_NOTIFY_TYPES : uint
{
    /// <summary>No notifications.</summary>
    NONE = 0x00000000,

    /// <summary>Suppress all notifications.</summary>
    SUPPRESS_NOTIFICATIONS = 0x00000001,

    /// <summary>A file handle has been opened.</summary>
    FILE_OPENED = 0x00000002,

    /// <summary>A new file has been created.</summary>
    NEW_FILE_CREATED = 0x00000004,

    /// <summary>An existing file has been overwritten.</summary>
    FILE_OVERWRITTEN = 0x00000008,

    /// <summary>A file is about to be deleted.</summary>
    PRE_DELETE = 0x00000010,

    /// <summary>A file is about to be renamed.</summary>
    PRE_RENAME = 0x00000020,

    /// <summary>A hard link is about to be created.</summary>
    PRE_SET_HARDLINK = 0x00000040,

    /// <summary>A file has been renamed.</summary>
    FILE_RENAMED = 0x00000080,

    /// <summary>A hard link has been created.</summary>
    HARDLINK_CREATED = 0x00000100,

    /// <summary>A file handle has been closed without modifications.</summary>
    FILE_HANDLE_CLOSED_NO_MODIFICATION = 0x00000200,

    /// <summary>A file handle has been closed after the file was modified.</summary>
    FILE_HANDLE_CLOSED_FILE_MODIFIED = 0x00000400,

    /// <summary>A file handle has been closed after the file was deleted.</summary>
    FILE_HANDLE_CLOSED_FILE_DELETED = 0x00000800,

    /// <summary>A placeholder file is about to be converted to a full file.</summary>
    FILE_PRE_CONVERT_TO_FULL = 0x00001000,

    /// <summary>Use the existing notification mask.</summary>
    USE_EXISTING_MASK = 0xFFFFFFFF,
}
