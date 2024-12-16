namespace Meziantou.Framework.Win32;

[Flags]
public enum ChangeJournalFlags : uint
{
    /// <summary>
    /// Range tracking is not turned on for the volume.
    /// </summary>
    None,

    /// <summary>
    /// Range tracking is turned on for the volume.
    /// </summary>
    TrackModifiedRangesEnable = 1,
}
