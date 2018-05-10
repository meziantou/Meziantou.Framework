namespace Meziantou.Framework.Win32.Native.Win32
{
    internal enum Win32ControlCode : uint
    {
        CreateUsnJournal = 0x000900e7,
        DeleteUsnJournal = 0x000900f8,
        QueryUsnJournal = 0x000900f4,
        ReadUsnJournal = 0x000900bb
    }
}