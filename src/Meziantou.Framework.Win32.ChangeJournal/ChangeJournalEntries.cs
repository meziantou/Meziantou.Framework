using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows5.1.2600")]
internal sealed class ChangeJournalEntries : IEnumerable<JournalEntry>
{
    private readonly ChangeJournal _changeJournal;
    private readonly ReadChangeJournalOptions _options;

    public ChangeJournalEntries(ChangeJournal changeJournal, ReadChangeJournalOptions options)
    {
        _changeJournal = changeJournal;
        _options = options;
    }

    public IEnumerator<JournalEntry> GetEnumerator()
    {
        return new ChangeJournalEntriesEnumerator(_changeJournal, _options);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private sealed class ChangeJournalEntriesEnumerator : IEnumerator<JournalEntry>
    {
        private const int BufferSize = 8192;

        private Usn _currentUSN;
        private bool _eof;

        private readonly List<JournalEntry> _entries = [];
        private int _currentIndex;

        public ChangeJournal ChangeJournal { get; }
        public ReadChangeJournalOptions Options { get; }

        public ChangeJournalEntriesEnumerator(ChangeJournal changeJournal, ReadChangeJournalOptions options)
        {
            ChangeJournal = changeJournal;
            Options = options;
            Reset();
        }

        public JournalEntry Current
        {
            get
            {
                if (_currentIndex >= 0 && _currentIndex < _entries.Count)
                    return _entries[_currentIndex];

                throw new InvalidOperationException("You must call MoveNext before");
            }
        }

        object IEnumerator.Current => Current;

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            if (_eof)
                return false;

            _currentIndex++;
            if (_entries is null || _currentIndex >= _entries.Count)
            {
                return Read();
            }

            return true;
        }

        public void Reset()
        {
            _currentUSN = Options.InitialUSN ?? ChangeJournal.Data.FirstUSN;
            _currentIndex = -1;
        }

        private bool Read()
        {
            const int CurrentUSNLength = sizeof(long);

            var readData = new READ_USN_JOURNAL_DATA_V0
            {
                StartUsn = _currentUSN,
                ReasonMask = Options.ReasonFilter,
                UsnJournalID = ChangeJournal.Data.ID,
                ReturnOnlyOnClose = Options.ReturnOnlyOnClose ? 1u : 0u,
                Timeout = (ulong)Options.Timeout.TotalSeconds,
            };

            var handle = ChangeJournal.ChangeJournalHandle;
            var entryData = Win32DeviceControl.ControlWithInput(handle, Win32ControlCode.ReadUsnJournal, ref readData, BufferSize);
            if (entryData.Length > CurrentUSNLength) // There are more data than just data currentUSN.
            {
                var bufferHandle = GCHandle.Alloc(entryData, GCHandleType.Pinned);
                var bufferPointer = bufferHandle.AddrOfPinnedObject();

                _currentUSN = Marshal.ReadInt64(bufferPointer);

                // Enumerate entries
                _entries.Clear();
                var offset = CurrentUSNLength; // Skip currentUSN field
                while (offset < entryData.Length)
                {
                    var entry = GetBufferedEntry(bufferPointer, offset);
                    offset += entry.Length;
                    _entries.Add(entry);
                }

                bufferHandle.Free();
                _currentIndex = 0;
                return true;
            }
            else
            {
                _eof = true;
                return false;
            }
        }

        private static JournalEntry GetBufferedEntry(IntPtr bufferPointer, int offset)
        {
            var entryPointer = bufferPointer + offset;
            var nativeEntry = Marshal.PtrToStructure<USN_RECORD_V2>(entryPointer);
            var filenamePointer = bufferPointer + offset + nativeEntry.FileNameOffset;
            var name = Marshal.PtrToStringAuto(filenamePointer);
            Debug.Assert(name is not null);
            return new JournalEntry(nativeEntry, name);
        }
    }
}
