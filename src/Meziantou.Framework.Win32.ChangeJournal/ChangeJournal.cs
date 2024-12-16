using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;

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
        if (driveInfo is null)
            throw new ArgumentNullException(nameof(driveInfo));

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
            var journalData = new Windows.Win32.System.Ioctl.USN_JOURNAL_DATA_V2();
            Win32DeviceControl.ControlWithOutput(ChangeJournalHandle, Win32ControlCode.QueryUsnJournal, ref journalData);

            return new JournalData(journalData);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == (int)Win32ErrorCode.ERROR_JOURNAL_NOT_ACTIVE)
        {
            return new JournalData();
        }
    }

    public void Dispose() => ChangeJournalHandle.Dispose();

    public void Delete() => Delete(waitForCompletion: true);

    public void Delete(bool waitForCompletion)
    {
        var deletionData = new Windows.Win32.System.Ioctl.DELETE_USN_JOURNAL_DATA
        {
            UsnJournalID = Data.ID,
            DeleteFlags = waitForCompletion ? Windows.Win32.System.Ioctl.USN_DELETE_FLAGS.USN_DELETE_FLAG_NOTIFY : Windows.Win32.System.Ioctl.USN_DELETE_FLAGS.USN_DELETE_FLAG_DELETE,
        };

        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.CreateUsnJournal, ref deletionData, bufferlen: 0);
        RefreshJournalData();
    }

    public void Create(ulong maximumSize, ulong allocationDelta)
    {
        var creationData = new Windows.Win32.System.Ioctl.CREATE_USN_JOURNAL_DATA
        {
            AllocationDelta = allocationDelta,
            MaximumSize = maximumSize,
        };

        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.CreateUsnJournal, ref creationData, bufferlen: 0);
        RefreshJournalData();
    }

    public void Create(long maximumSize, long allocationDelta)
    {
        var creationData = new Windows.Win32.System.Ioctl.CREATE_USN_JOURNAL_DATA
        {
            AllocationDelta = (ulong)allocationDelta,
            MaximumSize = (ulong)maximumSize,
        };

        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.CreateUsnJournal, ref creationData, bufferlen: 0);
        RefreshJournalData();
    }

    public void EnableTrackModifiedRanges(ulong chunkSize, long fileSizeThreshold)
    {
        var trackData = new Windows.Win32.System.Ioctl.USN_TRACK_MODIFIED_RANGES
        {
            Flags = PInvoke.FLAG_USN_TRACK_MODIFIED_RANGES_ENABLE,
            ChunkSize = chunkSize,
            FileSizeThreshold = fileSizeThreshold,

        };
        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.TrackModifiedRanges, ref trackData, bufferlen: 0);
    }
}
