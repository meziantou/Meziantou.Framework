using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Meziantou.Framework.Win32.Native.Journal;
using Meziantou.Framework.Win32.Native.Win32;

namespace Meziantou.Framework.Win32
{
    public class ChangeJournal : IDisposable
    {
        /// <summary>
        ///     The size, in bytes, to use for buffering
        ///     entries when reading from the opened journal.
        /// </summary>
        private readonly int _propBufferLength = 8192;

        /// <summary>
        ///     The handle to the currently opened journal volume.
        /// </summary>
        private IntPtr _propJournalHandle = new IntPtr(-1);

        private readonly List<JournalEntry> _entries;

        /// <summary>
        ///     Constructs a new instance of this class.
        /// </summary>
        public ChangeJournal()
        {
            Data = new JournalData();
            IsOpen = false;
            IsDisposed = false;
        }

        /// <summary>
        ///     Gets the USN_JOURNAL_DATA of the open journal.
        /// </summary>
        public JournalData Data { get; private set; }

        /// <summary>
        ///     Gets the last set of entries read from the open journal.
        /// </summary>
        public IReadOnlyList<JournalEntry> Entries => _entries;

        /// <summary>
        ///     Gets a value indicating whether a journal volume is open, or not.
        /// </summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        ///     Gets or sets a value indicating whether
        ///     this instance has been disposed of, or not.
        /// </summary>
        private bool IsDisposed { get; set; }


        /// <summary>
        ///     Disposes of this instance, if nto already disposed.
        /// </summary>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                Close();
                IsDisposed = true;
            }
        }

        /// <summary>
        ///     Destructs the instance of this class.
        /// </summary>
        ~ChangeJournal()
        {
            if (_propJournalHandle != new IntPtr(-1))
            {
                Win32Methods.CloseHandle(_propJournalHandle);
                _propJournalHandle = new IntPtr(-1);
            }
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(GetType().FullName);
        }

        /// <summary>
        ///     Opens the specified volume, and it's journal if active.
        /// </summary>
        /// <param name="drive">A properly formatted volume string.</param>
        public void Open(string volume)
        {
            ThrowIfDisposed();

            if (volume == null)
                throw new ArgumentNullException(nameof(volume));

            if (!VolumeHelper.VolumePathIsValid(volume))
                throw new ArgumentException("The specified value must be a properly formatted volume string, for example: \\\\.\\X.", nameof(volume));

            if (IsOpen)
            {
                Close();
            }

            _propJournalHandle = Win32Methods.CreateFile(volume, FileAccess.Read, FileShare.Read | FileShare.Write, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (_propJournalHandle.ToInt32() == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            IsOpen = true;
            Query();
        }

        /// <summary>
        ///     Closes the currently open journal and volume.
        /// </summary>
        public void Close()
        {
            if (IsDisposed)
                throw new ObjectDisposedException("ChangeJournal");

            if (!IsOpen)
                throw new InvalidOperationException("A volume must be open in order to close it.");

            if (!Win32Methods.CloseHandle(_propJournalHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            _entries.Clear();
            Data = new JournalData();
            IsOpen = false;
            _propJournalHandle = new IntPtr(-1);
        }

        /// <summary>
        ///     Creates a journal for the open volume - if a journal
        ///     already exists and the specified MaximumSize is larger
        ///     the journal will be expanded.
        /// </summary>
        /// <param name="maximumSize">
        ///     The maximum size,
        ///     in bytes, the created journal can reach before
        ///     reallocation takes place.
        /// </param>
        /// <param name="allocationDelta">
        ///     The size,
        ///     in bytes, of the allocation delta to be used
        ///     when reallocation takes place.
        /// </param>
        public void Create(long maximumSize, long allocationDelta)
        {
            ThrowIfDisposed();

            if (!IsOpen)
                throw new InvalidOperationException("A volume must be open in order to create a journal.");

            if (allocationDelta < maximumSize && allocationDelta != 0)
                throw new ArgumentException("AllocationDelta must be greater than MaximumSize, or zero", nameof(allocationDelta));

            if (maximumSize < 4096 && maximumSize != 0)
                throw new ArgumentException("MaximumSize must be a reasonable value (4096 or above), or 0 for default.", nameof(maximumSize));

            if (allocationDelta < 4096 && allocationDelta != 0)
                throw new ArgumentException("AllocationDelta must be a reasonable value (4096 or above), or 0 for default.", nameof(allocationDelta));

            var creationData = new CREATE_USN_JOURNAL_DATA
            {
                AllocationDelta = allocationDelta,
                MaximumSize = maximumSize
            };

            Win32DeviceControl.ControlWithInput(_propJournalHandle, Win32ControlCode.CreateUsnJournal, ref creationData, 0);
            Query();
        }

        /// <summary>
        ///     Deletes the currently open journal, leaving the volume open.
        /// </summary>
        public void Delete()
        {
            ThrowIfDisposed();

            if (!IsOpen)
                throw new InvalidOperationException("A volume must be open in order to delete it's journal.");

            if (!Data.IsActive)
                throw new InvalidOperationException("A volume's journal can only be deleted when it is active.");

            var deletionData = new DELETE_USN_JOURNAL_DATA
            {
                UsnJournalID = (ulong)Data.ID,
                DeleteFlags = DeletionFlag.WaitUntilDeleteCompletes
            };

            Win32DeviceControl.ControlWithInput(_propJournalHandle, Win32ControlCode.CreateUsnJournal, ref deletionData, 0);
            Query();
        }

        /// <summary>
        ///     Queries the currently open journal, populating the Data property.
        /// </summary>
        public void Query()
        {
            ThrowIfDisposed();

            if (!IsOpen)
                throw new InvalidOperationException("A volume must be open in order to query it's journal.");

            try
            {
                var journalData = new USN_JOURNAL_DATA();
                Win32DeviceControl.ControlWithOutput(_propJournalHandle, Win32ControlCode.QueryUsnJournal, ref journalData);

                Data = new JournalData(journalData)
                {
                    CurrentUSN = Data.FirstUSN,
                    IsActive = true,
                    AtEndOfJournal = false
                };
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == (int)Win32ErrorCode.ERROR_JOURNAL_NOT_ACTIVE)
            {
                Data = new JournalData();
            }
        }

        /// <summary>
        ///     Reads the next set of records from the currently open journal,
        ///     if the end of the journal was reached with the last read then
        ///     the journal is re-queried and reading starts from the first USN.
        /// </summary>
        public void Read(ChangeReason reasonFilter, bool returnOnlyOnClose, long timeout)
        {
            ThrowIfDisposed();

            if (!IsOpen)
                throw new InvalidOperationException("A volume must be open in order to read it's journal.");

            if (!Data.IsActive)
                throw new InvalidOperationException("A volume's journal must be active in order to read it.");

            _entries.Clear();
            if (Data.AtEndOfJournal)
            {
                Query();
            }

            var readData = new READ_USN_JOURNAL_DATA_V0
            {
                StartUsn = Data.CurrentUSN,
                ReasonMask = reasonFilter,
                UsnJournalID = (ulong)Data.ID,
                ReturnOnlyOnClose = returnOnlyOnClose ? (uint)1 : 0,
                Timeout = (ulong)timeout
            };

            var entryData = Win32DeviceControl.ControlWithInput(_propJournalHandle, Win32ControlCode.ReadUsnJournal, ref readData, _propBufferLength);
            if (entryData.Length > sizeof(long))
            {
                var bufferHandle = GCHandle.Alloc(entryData, GCHandleType.Pinned);
                var bufferPointer = bufferHandle.AddrOfPinnedObject();
                //set StartUsn for next read as the next usn
                Data.CurrentUSN = Marshal.ReadInt64(entryData, 0);
                //enumerate collected entries
                long offset = sizeof(long);
                while (offset < entryData.Length)
                {
                    var entry = GetBufferedEntry(bufferPointer, offset);
                    offset += entry.Length;
                    _entries.Add(entry);
                }

                bufferHandle.Free();
            }
            else
            {
                Data.AtEndOfJournal = true;
            }
        }

        private static JournalEntry GetBufferedEntry(IntPtr bufferPointer, long offset)
        {
            var entryPointer = new IntPtr(bufferPointer.ToInt32() + offset);
            var nativeEntry = Marshal.PtrToStructure<USN_RECORD_V2>(entryPointer);
            var filenamePointer = new IntPtr(bufferPointer.ToInt32() + offset + nativeEntry.FileNameOffset);
            return new JournalEntry(nativeEntry, Marshal.PtrToStringAuto(filenamePointer));
        }
    }
}