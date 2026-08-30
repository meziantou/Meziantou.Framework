using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;
using Windows.Win32.System.Ioctl;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows5.1.2600")]
internal sealed class ChangeJournalEntries : IEnumerable<ChangeJournalEntry>
{
    /// <summary>
    ///     Offset of the extents inside a <see cref="USN_RECORD_V4"/>. It is a property of the layout rather than of any record,
    ///     so it is computed once instead of once per record.
    /// </summary>
    private static readonly nint ExtentsOffset = Marshal.OffsetOf<USN_RECORD_V4>(nameof(USN_RECORD_V4.Extents));

    private readonly ChangeJournal _changeJournal;
    private readonly ReadChangeJournalOptions _options;

    public ChangeJournalEntries(ChangeJournal changeJournal, ReadChangeJournalOptions options)
    {
        _changeJournal = changeJournal;
        _options = options;
    }

    public IEnumerator<ChangeJournalEntry> GetEnumerator()
    {
        return new ChangeJournalEntriesEnumerator(_changeJournal, _options);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    internal static unsafe ChangeJournalEntry GetBufferedEntry(IntPtr bufferPointer, USN_RECORD_COMMON_HEADER header)
    {
        if (header is { MajorVersion: 2, MinorVersion: 0 })
        {
            EnsureRecordHoldsItsStructure(header, sizeof(USN_RECORD_V2));
            var entry = Unsafe.ReadUnaligned<USN_RECORD_V2>((void*)bufferPointer);
            return new ChangeJournalEntryVersion2or3(entry, GetFileName(bufferPointer, header, entry.FileNameOffset, entry.FileNameLength));
        }
        else if (header is { MajorVersion: 3, MinorVersion: 0 })
        {
            EnsureRecordHoldsItsStructure(header, sizeof(USN_RECORD_V3));
            var entry = Unsafe.ReadUnaligned<USN_RECORD_V3>((void*)bufferPointer);
            return new ChangeJournalEntryVersion2or3(entry, GetFileName(bufferPointer, header, entry.FileNameOffset, entry.FileNameLength));
        }
        else if (header is { MajorVersion: 4, MinorVersion: 0 })
        {
            EnsureRecordHoldsItsStructure(header, sizeof(USN_RECORD_V4));
            var entry = Unsafe.ReadUnaligned<USN_RECORD_V4>((void*)bufferPointer);
            var extendOffset = ExtentsOffset;

            // The extents are stored inside the record, so they must not extend past the record's own length.
            if (entry.ExtentSize < sizeof(USN_RECORD_EXTENT) || (long)extendOffset + ((long)entry.NumberOfExtents * entry.ExtentSize) > header.RecordLength)
                throw new InvalidDataException($"The change journal returned a record of length {header.RecordLength} holding {entry.NumberOfExtents} extents of {entry.ExtentSize} bytes at offset {extendOffset}");

            var extents = new ChangeJournalEntryExtent[entry.NumberOfExtents];
            for (var i = 0; i < entry.NumberOfExtents; i++)
            {
                var extentPointer = bufferPointer + extendOffset + i * entry.ExtentSize;
                var extent = Unsafe.ReadUnaligned<USN_RECORD_EXTENT>((void*)extentPointer);
                extents[i] = new ChangeJournalEntryExtent(extent);
            }

            return new ChangeJournalEntryVersion4(entry, extents);
        }
        else
        {
            throw new NotSupportedException($"Record version {header.MajorVersion}.{header.MinorVersion} is not supported");
        }
    }

    private static void EnsureRecordHoldsItsStructure(USN_RECORD_COMMON_HEADER header, int structureLength)
    {
        // The versioned structure is read out of the record, so the record has to be at least that long. The record has already
        // been checked to fit in the buffer it came from, so this is what keeps that read inside the record as well.
        if (header.RecordLength < structureLength)
            throw new InvalidDataException($"The change journal returned a version {header.MajorVersion}.{header.MinorVersion} record of length {header.RecordLength}, which is smaller than the {structureLength}-byte structure it declares");
    }

    private static string GetFileName(IntPtr bufferPointer, USN_RECORD_COMMON_HEADER header, ushort fileNameOffset, ushort fileNameLength)
    {
        // The name is stored inside the record, so it must not extend past the record's own length.
        if ((uint)fileNameOffset + fileNameLength > header.RecordLength)
            throw new InvalidDataException($"The change journal returned a record of length {header.RecordLength} holding a file name of {fileNameLength} bytes at offset {fileNameOffset}");

        // Names in a USN record are always UTF-16, which is what the length in bytes is converted with.
        var name = Marshal.PtrToStringUni(bufferPointer + fileNameOffset, fileNameLength / sizeof(char));
        Debug.Assert(name is not null);
        return name;
    }

    private sealed class ChangeJournalEntriesEnumerator : IEnumerator<ChangeJournalEntry>
    {
        private const int BufferSize = 8192;

        private Usn _currentUSN;
        private bool _eof;

        private readonly List<ChangeJournalEntry> _entries = [];
        private int _currentIndex;

        public ChangeJournal ChangeJournal { get; }
        public ReadChangeJournalOptions Options { get; }

        public ChangeJournalEntriesEnumerator(ChangeJournal changeJournal, ReadChangeJournalOptions options)
        {
            ChangeJournal = changeJournal;
            Options = options;
            Reset();
        }

        public ChangeJournalEntry Current
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
            if (_currentIndex >= _entries.Count)
            {
                return Read();
            }

            return true;
        }

        public void Reset()
        {
            _currentUSN = Options.InitialUSN ?? Usn.Zero;
            _currentIndex = -1;

            // Both must be cleared, otherwise MoveNext either stops immediately because the previous
            // enumeration reached the end of the journal, or replays the last batch that was read.
            _eof = false;
            _entries.Clear();
        }

        private unsafe bool Read()
        {
            const int CurrentUSNLength = sizeof(long);

            var readData = new READ_USN_JOURNAL_DATA_V1
            {
                StartUsn = _currentUSN.Value,
                ReasonMask = (uint)Options.ReasonFilter,
                ReturnOnlyOnClose = Options.ReturnOnlyOnClose ? 1u : 0u,
                Timeout = Options.TimeoutInSeconds,
                UsnJournalID = ChangeJournal.Data.ID,
                MinMajorVersion = Options.MinimumMajorVersion,
                MaxMajorVersion = Options.MaximumMajorVersion,
            };

            var handle = ChangeJournal.ChangeJournalHandle;

            var controlCode = Options.Unprivileged ? Win32ControlCode.ReadUnprivilegedUsnJournal : Win32ControlCode.ReadUsnJournal;
            var entryData = Win32DeviceControl.ControlWithInput(handle, controlCode, ref readData, BufferSize);
            _entries.Clear();

            if (entryData.Length > CurrentUSNLength) // There are more data than just data currentUSN.
            {
                var headerLength = sizeof(USN_RECORD_COMMON_HEADER);
                fixed (byte* fixedBufferPointer = entryData)
                {
                    var bufferPointer = (nint)fixedBufferPointer;
                    _currentUSN = Marshal.ReadInt64(bufferPointer);

                    // Enumerate entries
                    var offset = CurrentUSNLength; // Skip currentUSN field
                    while (offset < entryData.Length)
                    {
                        var entryPointer = bufferPointer + offset;
                        var header = Unsafe.ReadUnaligned<USN_RECORD_COMMON_HEADER>((void*)entryPointer);

                        // A record must be large enough to hold its own header and must not extend past the data that
                        // was returned. Without this check, a length of 0 loops forever and an oversized length makes
                        // the next iteration read past the end of the buffer.
                        if (header.RecordLength < headerLength || header.RecordLength > (uint)(entryData.Length - offset))
                            throw new InvalidDataException($"The change journal returned a record of length {header.RecordLength} at offset {offset}, which does not fit in the {entryData.Length}-byte buffer");

                        _entries.Add(GetBufferedEntry(entryPointer, header));
                        offset += (int)header.RecordLength;
                    }
                }
            }

            // The buffer can hold the current USN without holding any record, in which case there is nothing to enumerate.
            if (_entries.Count is 0)
            {
                _eof = true;
                return false;
            }

            _currentIndex = 0;
            return true;
        }
    }
}
