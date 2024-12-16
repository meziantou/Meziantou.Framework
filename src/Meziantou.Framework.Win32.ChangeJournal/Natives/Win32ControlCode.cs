namespace Meziantou.Framework.Win32.Natives;

internal enum Win32ControlCode : uint
{
    CreateUsnJournal = 0X000900E7,
    DeleteUsnJournal = 0X000900F8,
    QueryUsnJournal = 0X000900F4,
    ReadUsnJournal = 0X000900BB,
    ReadUnprivilegedUsnJournal = 0x000903AB,
    TrackModifiedRanges = 0x000902F4,
}
