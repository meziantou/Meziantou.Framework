using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    public class ChangeJournal : IDisposable
    {
        internal ChangeJournalSafeHandle ChangeJournalHandle { get; }

        public JournalData Data { get; }

        public IEnumerable<JournalEntry> Entries { get; }

        public IEnumerable<JournalEntry> GetEntries(long currentUSN, ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
        {
            if (currentUSN < Data.FirstUSN || currentUSN > Data.MaximumUSN)
                throw new ArgumentOutOfRangeException(nameof(currentUSN));

            return new ChangeJournalEntries(this, new ReadChangeJournalOptions(currentUSN, reasonFilter, returnOnlyOnClose, timeout));
        }

        private ChangeJournal(ChangeJournalSafeHandle handle)
        {
            ChangeJournalHandle = handle;
            Data = ReadJournalData();
            Entries = new ChangeJournalEntries(this, new ReadChangeJournalOptions(Data.FirstUSN, ChangeReason.All, false, TimeSpan.Zero));
        }

        public static ChangeJournal Open(DriveInfo driveInfo)
        {
            if (driveInfo == null)
                throw new ArgumentNullException(nameof(driveInfo));

            var volume = VolumeHelper.GetValidVolumePath(driveInfo);
            var handle = new ChangeJournalSafeHandle(Win32Methods.CreateFile(volume, FileAccess.Read, FileShare.Read | FileShare.Write, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero));
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            return new ChangeJournal(handle);
        }

        private JournalData ReadJournalData()
        {
            try
            {
                var journalData = new USN_JOURNAL_DATA();
                Win32DeviceControl.ControlWithOutput(ChangeJournalHandle.Handle, Win32ControlCode.QueryUsnJournal, ref journalData);

                return new JournalData(journalData);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == (int)Win32ErrorCode.ERROR_JOURNAL_NOT_ACTIVE)
            {
                return new JournalData();
            }
        }

        public void Dispose()
        {
            ChangeJournalHandle.Dispose();
        }
    }
}