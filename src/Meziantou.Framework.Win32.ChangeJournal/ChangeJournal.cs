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

    public IEnumerable<JournalEntry> Entries { get; }

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

    public IEnumerable<JournalEntry> GetEntries(ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
    {
        return new ChangeJournalEntries(this, new ReadChangeJournalOptions(initialUSN: null, reasonFilter, returnOnlyOnClose, timeout, _unprivileged));
    }

    public IEnumerable<JournalEntry> GetEntries(Usn currentUSN, ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
    {
        if (currentUSN < Data.FirstUSN || currentUSN > Data.MaximumUSN)
            throw new ArgumentOutOfRangeException(nameof(currentUSN));

        return new ChangeJournalEntries(this, new ReadChangeJournalOptions(currentUSN, reasonFilter, returnOnlyOnClose, timeout, _unprivileged));
    }

    public void ReadJournalData()
    {
        Data = ReadJournalDataImpl();
    }

    private JournalData ReadJournalDataImpl()
    {
        try
        {
            var journalData = new USN_JOURNAL_DATA();
            Win32DeviceControl.ControlWithOutput(ChangeJournalHandle, Win32ControlCode.QueryUsnJournal, ref journalData);

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

    public void Delete()
    {
        var deletionData = new DELETE_USN_JOURNAL_DATA
        {
            UsnJournalID = Data.ID,
            DeleteFlags = DeletionFlag.WaitUntilDeleteCompletes,
        };

        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.CreateUsnJournal, ref deletionData, 0);
        ReadJournalData();
    }

    public void Create(long maximumSize, long allocationDelta)
    {
        var creationData = new CREATE_USN_JOURNAL_DATA
        {
            AllocationDelta = allocationDelta,
            MaximumSize = maximumSize,
        };

        Win32DeviceControl.ControlWithInput(ChangeJournalHandle, Win32ControlCode.CreateUsnJournal, ref creationData, 0);
        ReadJournalData();
    }
}
