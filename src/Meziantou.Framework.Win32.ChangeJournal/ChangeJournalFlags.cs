namespace Meziantou.Framework.Win32;

/// <summary>Specifies flags for the change journal.</summary>
[Flags]
public enum ChangeJournalFlags : uint
{
    /// <summary>Range tracking is not turned on for the volume.</summary>
    None,

    /// <summary>Range tracking is turned on for the volume.</summary>
    TrackModifiedRangesEnable = 1,
}
