using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Ioctl;

namespace Meziantou.Framework.Win32;

[SupportedOSPlatform("windows5.1.2600")]
public sealed class ChangeJournal : IDisposable
{
    private readonly bool _unprivileged;

    internal SafeFileHandle ChangeJournalHandle { get; }

    public JournalData Data { get; private set; }

    public IEnumerable<ChangeJournalEntry> Entries { get; }

    private ChangeJournal(SafeFileHandle handle, bool unprivileged)
    {
        ChangeJournalHandle = handle;
        _unprivileged = unprivileged;
        Data = ReadJournalDataImpl();
        Entries = new ChangeJournalEntries(this, new ReadChangeJournalOptions(initialUSN: null, ChangeReason.All, returnOnlyOnClose: false, TimeSpan.Zero, unprivileged));
    }

    public static ChangeJournal Open(DriveInfo driveInfo)
    {
        return Open(driveInfo, unprivileged: false);
    }

    public static ChangeJournal Open(DriveInfo driveInfo, bool unprivileged)
    {
        ArgumentNullException.ThrowIfNull(driveInfo);

        var volume = VolumeHelper.GetValidVolumePath(driveInfo);
        var fileAccessRights = FILE_ACCESS_RIGHTS.FILE_TRAVERSE;
        var handle = PInvoke.CreateFile(
            volume,
            (uint)fileAccessRights,
            FILE_SHARE_MODE.FILE_SHARE_READ | FILE_SHARE_MODE.FILE_SHARE_WRITE,
            lpSecurityAttributes: null,
            FILE_CREATION_DISPOSITION.OPEN_EXISTING,
            FILE_FLAGS_AND_ATTRIBUTES.FILE_ATTRIBUTE_NORMAL,
            hTemplateFile: null);

        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new ChangeJournal(handle, unprivileged);
    }

    public IEnumerable<ChangeJournalEntry> GetEntries(ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
    {
        return new ChangeJournalEntries(this, new ReadChangeJournalOptions(initialUSN: null, reasonFilter, returnOnlyOnClose, timeout, _unprivileged));
    }

    public IEnumerable<ChangeJournalEntry> GetEntries(Usn currentUSN, ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
    {
        if (currentUSN < Data.FirstUSN || currentUSN > Data.MaximumUSN)
            throw new ArgumentOutOfRangeException(nameof(currentUSN));

        return new ChangeJournalEntries(this, new ReadChangeJournalOptions(currentUSN, reasonFilter, returnOnlyOnClose, timeout, _unprivileged));
    }

    public static ChangeJournalEntryVersion2or3 GetEntry(string path)
    {
        using var handle = File.OpenHandle(path);
        return GetEntry(handle);
    }

    public static unsafe ChangeJournalEntryVersion2or3 GetEntry(SafeFileHandle handle)
    {
        var buffer = new byte[USN_RECORD_V3.SizeOf(512)];
        fixed (void* bufferPtr = buffer)
        {
            using var handleScope = new SafeHandleValue(handle);
            uint returnedSize;
            var controlResult = PInvoke.DeviceIoControl((HANDLE)handleScope.Value, PInvoke.FSCTL_READ_FILE_USN_DATA, lpInBuffer: null, 0, bufferPtr, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
            if (!controlResult)
            {
                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode == (int)WIN32_ERROR.ERROR_MORE_DATA)
                {
                    buffer = new byte[returnedSize];
                    controlResult = PInvoke.DeviceIoControl((HANDLE)handleScope.Value, PInvoke.FSCTL_READ_FILE_USN_DATA, lpInBuffer: null, 0, bufferPtr, (uint)buffer.Length, &returnedSize, lpOverlapped: null);
                    if (!controlResult)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                else
                {
                    throw new Win32Exception(errorCode);
                }
            }

            var header = Marshal.PtrToStructure<USN_RECORD_COMMON_HEADER>((nint)bufferPtr);
            return (ChangeJournalEntryVersion2or3)ChangeJournalEntries.GetBufferedEntry((nint)bufferPtr, header);
        }
    }

    public void RefreshJournalData()
    {
        Data = ReadJournalDataImpl();
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public void ReadJournalData()
    {
        RefreshJournalData();
    }

    private JournalData ReadJournalDataImpl()
    {
        try
        {
            var journalData = new USN_JOURNAL_DATA_V2();
            Win32DeviceControl.ControlWithOutput(ChangeJournalHandle, Win32ControlCode.QueryUsnJournal, ref journalData);

            return new JournalData(journalData);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == (int)WIN32_ERROR.ERROR_JOURNAL_NOT_ACTIVE)
        {
            return new JournalData();
        }
    }

    public void Dispose() => ChangeJournalHandle.Dispose();

    public void Delete() => Delete(waitForCompletion: true);

    public void Delete(bool waitForCompletion)
    {
        var deletionData = new DELETE_USN_JOURNAL_DATA
        {
            UsnJournalID = Data.ID,
            DeleteFlags = waitForCompletion ? USN_DELETE_FLAGS.USN_DELETE_FLAG_NOTIFY : USN_DELETE_FLAGS.USN_DELETE_FLAG_DELETE,
        };

        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.CreateUsnJournal, ref deletionData, initialBufferLength: 0);
        RefreshJournalData();
    }

    public void Create(ulong maximumSize, ulong allocationDelta)
    {
        var creationData = new CREATE_USN_JOURNAL_DATA
        {
            AllocationDelta = allocationDelta,
            MaximumSize = maximumSize,
        };

        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.CreateUsnJournal, ref creationData, initialBufferLength: 0);
        RefreshJournalData();
    }

    public void Create(long maximumSize, long allocationDelta)
    {
        var creationData = new CREATE_USN_JOURNAL_DATA
        {
            AllocationDelta = (ulong)allocationDelta,
            MaximumSize = (ulong)maximumSize,
        };

        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.CreateUsnJournal, ref creationData, initialBufferLength: 0);
        RefreshJournalData();
    }

    public void EnableTrackModifiedRanges(ulong chunkSize, long fileSizeThreshold)
    {
        var trackData = new USN_TRACK_MODIFIED_RANGES
        {
            Flags = PInvoke.FLAG_USN_TRACK_MODIFIED_RANGES_ENABLE,
            ChunkSize = chunkSize,
            FileSizeThreshold = fileSizeThreshold,

        };
        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.TrackModifiedRanges, ref trackData, initialBufferLength: 0);
    }
}
