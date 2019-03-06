namespace Meziantou.Framework.Win32.Natives
{
    /// <summary>
    ///     Flags to be used in conjunction with Usn structures
    /// </summary>
    internal enum DeletionFlag
    {
        /// <summary>
        ///     Causes a ChangeJournal to be deleted
        /// </summary>
        Delete = 0x00000001,

        /// <summary>
        ///     Causes a ChangeJournal to be deleted and
        ///     specifies to wait until the process is complete
        /// </summary>
        WaitUntilDeleteCompletes = 0x00000002,
    }
}