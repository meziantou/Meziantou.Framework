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

/// <summary>
/// Provides access to the NTFS change journal (Update Sequence Number journal) for a volume.
/// The change journal records information about file and directory changes on an NTFS volume.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class ChangeJournal : IDisposable
{
    private readonly bool _unprivileged;

    internal SafeFileHandle ChangeJournalHandle { get; }

    /// <summary>Gets the current metadata about the change journal.</summary>
    public JournalData Data { get; private set; }

    /// <summary>Gets a collection of all change journal entries.</summary>
    public IEnumerable<ChangeJournalEntry> Entries { get; }

    private ChangeJournal(SafeFileHandle handle, bool unprivileged)
    {
        ChangeJournalHandle = handle;
        _unprivileged = unprivileged;
        Data = ReadJournalDataImpl();
        Entries = new ChangeJournalEntries(this, new ReadChangeJournalOptions(initialUSN: null, ChangeReason.All, returnOnlyOnClose: false, TimeSpan.Zero, unprivileged));
    }

    /// <summary>Opens the change journal for the specified drive.</summary>
    /// <param name="driveInfo">The drive to open the change journal for.</param>
    /// <returns>A <see cref="ChangeJournal"/> instance for accessing the change journal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="driveInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public static ChangeJournal Open(DriveInfo driveInfo)
    {
        return Open(driveInfo, unprivileged: false);
    }

    /// <summary>Opens the change journal for the specified drive.</summary>
    /// <param name="driveInfo">The drive to open the change journal for.</param>
    /// <param name="unprivileged">If <see langword="true"/>, uses unprivileged access mode; otherwise, uses standard access mode.</param>
    /// <returns>A <see cref="ChangeJournal"/> instance for accessing the change journal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="driveInfo"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
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

    /// <summary>Gets change journal entries with the specified filter criteria.</summary>
    /// <param name="reasonFilter">A filter that specifies which types of changes to include.</param>
    /// <param name="returnOnlyOnClose">If <see langword="true"/>, returns only entries with the Close reason flag set.</param>
    /// <param name="timeout">The time to wait for new entries before returning.</param>
    /// <returns>A collection of change journal entries matching the filter criteria.</returns>
    public IEnumerable<ChangeJournalEntry> GetEntries(ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
    {
        return new ChangeJournalEntries(this, new ReadChangeJournalOptions(initialUSN: null, reasonFilter, returnOnlyOnClose, timeout, _unprivileged));
    }

    /// <summary>Gets change journal entries starting from a specific USN with the specified filter criteria.</summary>
    /// <param name="currentUSN">The USN to start reading from.</param>
    /// <param name="reasonFilter">A filter that specifies which types of changes to include.</param>
    /// <param name="returnOnlyOnClose">If <see langword="true"/>, returns only entries with the Close reason flag set.</param>
    /// <param name="timeout">The time to wait for new entries before returning.</param>
    /// <returns>A collection of change journal entries matching the filter criteria.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="currentUSN"/> is outside the valid range.</exception>
    public IEnumerable<ChangeJournalEntry> GetEntries(Usn currentUSN, ChangeReason reasonFilter, bool returnOnlyOnClose, TimeSpan timeout)
    {
        if (currentUSN < Data.FirstUSN || currentUSN > Data.MaximumUSN)
            throw new ArgumentOutOfRangeException(nameof(currentUSN));

        return new ChangeJournalEntries(this, new ReadChangeJournalOptions(currentUSN, reasonFilter, returnOnlyOnClose, timeout, _unprivileged));
    }

    /// <summary>Gets the change journal entry for a specific file or directory by path.</summary>
    /// <param name="path">The path to the file or directory.</param>
    /// <returns>The change journal entry for the specified file or directory.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public static ChangeJournalEntryVersion2or3 GetEntry(string path)
    {
        using var handle = File.OpenHandle(path);
        return GetEntry(handle);
    }

    /// <summary>Gets the change journal entry for a specific file or directory by handle.</summary>
    /// <param name="handle">A handle to the file or directory.</param>
    /// <returns>The change journal entry for the specified file or directory.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
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

    /// <summary>Refreshes the journal metadata by reading the current state from the change journal.</summary>
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

    /// <summary>
    /// Releases all resources used by the current instance of the <see cref="ChangeJournal"/> class.
    /// </summary>
    public void Dispose() => ChangeJournalHandle.Dispose();

    /// <summary>Deletes the change journal and waits for the deletion to complete.</summary>
    public void Delete() => Delete(waitForCompletion: true);

    /// <summary>Deletes the change journal.</summary>
    /// <param name="waitForCompletion">If <see langword="true"/>, waits for the deletion to complete; otherwise, deletes asynchronously.</param>
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

    /// <summary>Creates a new change journal or modifies an existing one.</summary>
    /// <param name="maximumSize">The maximum size, in bytes, that the journal can use on the volume.</param>
    /// <param name="allocationDelta">The size, in bytes, by which the journal grows when needed.</param>
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

    /// <summary>Creates a new change journal or modifies an existing one.</summary>
    /// <param name="maximumSize">The maximum size, in bytes, that the journal can use on the volume.</param>
    /// <param name="allocationDelta">The size, in bytes, by which the journal grows when needed.</param>
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

    /// <summary>Enables range tracking for the change journal.</summary>
    /// <param name="chunkSize">The granularity of tracked ranges.</param>
    /// <param name="fileSizeThreshold">The file size threshold to start tracking ranges for files with equal or larger size.</param>
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
