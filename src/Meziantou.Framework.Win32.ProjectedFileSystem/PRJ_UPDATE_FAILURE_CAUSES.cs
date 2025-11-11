namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Specifies the reasons why a file update or deletion operation failed.</summary>
[Flags]
public enum PRJ_UPDATE_FAILURE_CAUSES
{
    /// <summary>No failure.</summary>
    NONE = 0x00000000,

    /// <summary>The file has dirty metadata.</summary>
    DIRTY_METADATA = 0x00000001,

    /// <summary>The file has dirty data.</summary>
    DIRTY_DATA = 0x00000002,

    /// <summary>The file is a tombstone.</summary>
    TOMBSTONE = 0x00000004,

    /// <summary>The file is read-only.</summary>
    READ_ONLY = 0x00000008,
}
