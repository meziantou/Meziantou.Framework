namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [Flags]
    public enum PRJ_UPDATE_FAILURE_CAUSES
    {
        NONE = 0x00000000,
        DIRTY_METADATA = 0x00000001,
        DIRTY_DATA = 0x00000002,
        TOMBSTONE = 0x00000004,
        READ_ONLY = 0x00000008,
    }
}
