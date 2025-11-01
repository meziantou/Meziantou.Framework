namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Specifies the on-disk state of a file or directory in a virtual file system.</summary>
[Flags]
public enum PRJ_FILE_STATE
{
    /// <summary>The file or directory is a virtual placeholder that has not yet been hydrated with content.</summary>
    PRJ_FILE_STATE_PLACEHOLDER = 0x00000001,

    /// <summary>The file has been hydrated with its content from the provider.</summary>
    PRJ_FILE_STATE_HYDRATED_PLACEHOLDER = 0x00000002,

    /// <summary>The file is a placeholder that has been modified by the user.</summary>
    PRJ_FILE_STATE_DIRTY_PLACEHOLDER = 0x00000004,

    /// <summary>The file is a full file that is no longer managed by the virtual file system.</summary>
    PRJ_FILE_STATE_FULL = 0x00000008,

    /// <summary>The file or directory has been deleted and exists as a tombstone.</summary>
    PRJ_FILE_STATE_TOMBSTONE = 0x00000010,
}
