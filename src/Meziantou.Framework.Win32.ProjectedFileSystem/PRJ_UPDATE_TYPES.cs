namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>Specifies which file states are allowed to be updated or deleted.</summary>
[Flags]
public enum PRJ_UPDATE_TYPES
{
    /// <summary>No updates allowed.</summary>
    PRJ_UPDATE_NONE = 0x00000000,

    /// <summary>Allow updating files with dirty metadata.</summary>
    PRJ_UPDATE_ALLOW_DIRTY_METADATA = 0x00000001,

    /// <summary>Allow updating files with dirty data.</summary>
    PRJ_UPDATE_ALLOW_DIRTY_DATA = 0x00000002,

    /// <summary>Allow updating tombstone files.</summary>
    PRJ_UPDATE_ALLOW_TOMBSTONE = 0x00000004,
    //PRJ_UPDATE_RESERVED1 = 0x00000008,
    //PRJ_UPDATE_RESERVED2 = 0x00000010,

    /// <summary>Allow updating read-only files.</summary>
    PRJ_UPDATE_ALLOW_READ_ONLY = 0x00000020,

    /// <summary>Maximum value for update types.</summary>
    PRJ_UPDATE_MAX_VAL = PRJ_UPDATE_ALLOW_READ_ONLY << 1,
}
