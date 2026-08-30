using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RestartManager;

namespace Meziantou.Framework.Win32;

/// <summary>Provides a wrapper around the Windows Restart Manager API to detect which processes are locking files and manage application restarts.</summary>
/// <example>
/// <code>
/// // Check if a file is locked
/// if (RestartManager.IsFileLocked(@"C:\path\to\file.txt"))
/// {
///     Console.WriteLine("File is locked");
/// }
///
/// // Get processes locking a file. The caller owns the returned Process instances.
/// var processes = RestartManager.GetProcessesLockingFile(@"C:\path\to\file.txt");
/// foreach (var process in processes)
/// {
///     Console.WriteLine($"{process.ProcessName} (PID: {process.Id})");
///     process.Dispose();
/// }
///
/// // Manual session management
/// using var session = RestartManager.CreateSession();
/// session.RegisterFile(@"C:\path\to\file.txt");
/// if (session.IsResourcesLocked())
/// {
///     var lockingProcesses = session.GetProcessesLockingResources();
///     // Handle locked resources
/// }
/// </code>
/// </example>
[SupportedOSPlatform("windows6.0.6000")]
public sealed class RestartManager : IDisposable
{
    private readonly RestartManagerSessionHandle _sessionHandle;

    /// <summary>Gets the session key for this Restart Manager session.</summary>
    public string SessionKey { get; }

    /// <summary>Gets the reason a system restart is required to free the registered resources.</summary>
    /// <remarks>
    /// This reflects the most recent call to <see cref="IsResourcesLocked"/>, <see cref="GetProcessesLockingResources"/>
    /// or <see cref="GetLockingProcesses"/>, and is <see cref="RestartManagerRebootReason.None"/> until one of them runs.
    /// When it is not <see cref="RestartManagerRebootReason.None"/>, calling <see cref="Shutdown(RestartManagerShutdownType)"/>
    /// will not free the resources and the user should be prompted for a system restart instead.
    /// </remarks>
    public RestartManagerRebootReason RebootReason { get; private set; }

    private RestartManager(uint sessionHandle, string sessionKey)
    {
        _sessionHandle = new RestartManagerSessionHandle(sessionHandle);
        SessionKey = sessionKey;
    }

    /// <summary>Creates a new Restart Manager session.</summary>
    /// <returns>A new <see cref="RestartManager"/> instance representing the session.</returns>
    /// <exception cref="Win32Exception">Thrown when the session creation fails.</exception>
    public static RestartManager CreateSession()
    {
        Span<char> sessionKeyBuffer = stackalloc char[(int)PInvoke.CCH_RM_SESSION_KEY + 1];
        var result = StartSession(out var handle, sessionKeyBuffer);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmStartSession failed ({result})");

        var sessionKeyLength = sessionKeyBuffer.IndexOf('\0');
        var sessionKey = sessionKeyLength >= 0 ? new string(sessionKeyBuffer[..sessionKeyLength]) : new string(sessionKeyBuffer);
        return new RestartManager(handle, sessionKey);
    }

    /// <summary>Joins an existing Restart Manager session using the specified session key.</summary>
    /// <param name="sessionKey">The session key of an existing Restart Manager session.</param>
    /// <returns>A <see cref="RestartManager"/> instance representing the joined session.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sessionKey"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">Thrown when joining the session fails.</exception>
    public static RestartManager JoinSession(string sessionKey)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);

        var result = PInvoke.RmJoinSession(out var handle, sessionKey);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmJoinSession failed ({result})");

        return new RestartManager(handle, sessionKey);
    }

    /// <summary>Registers a file to be monitored by the Restart Manager session.</summary>
    /// <param name="path">The full path of the file to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">Thrown when the registration fails.</exception>
    public void RegisterFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        ObjectDisposedException.ThrowIf(_sessionHandle.IsClosed, this);

        string[] resources = [path];
        using var handleScope = new SafeHandleValue(_sessionHandle);
        var result = PInvoke.RmRegisterResources((uint)handleScope.Value, resources, rgApplications: default, rgsServiceNames: default);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmRegisterResources failed ({result})");
    }

    /// <summary>Registers multiple files to be monitored by the Restart Manager session.</summary>
    /// <param name="paths">An array of full file paths to register.</param>
    /// <remarks>The Restart Manager persists registrations to the registry, so a single session can only hold a bounded number of paths. Registering a very large set fails with a <see cref="Win32Exception"/> for ERROR_WRITE_FAULT (29) rather than a dedicated error; split such sets across several sessions.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="paths"/> contains a <see langword="null"/> element.</exception>
    /// <exception cref="Win32Exception">Thrown when the registration fails.</exception>
    public void RegisterFiles(string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ThrowIfContainsNull(paths);
        ObjectDisposedException.ThrowIf(_sessionHandle.IsClosed, this);

        using var handleScope = new SafeHandleValue(_sessionHandle);
        var result = PInvoke.RmRegisterResources((uint)handleScope.Value, paths, rgApplications: default, rgsServiceNames: default);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmRegisterResources failed ({result})");
    }

    /// <summary>Determines whether any of the registered resources are currently locked by running processes.</summary>
    /// <returns><see langword="true"/> if at least one registered resource is locked; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public bool IsResourcesLocked()
    {
        ObjectDisposedException.ThrowIf(_sessionHandle.IsClosed, this);

        // A single-element buffer is enough here. ERROR_MORE_DATA already means more than one process is
        // affected, and arrayCount reports the total number needed in both cases, so there is nothing to retry.
        using var handleScope = new SafeHandleValue(_sessionHandle);
        uint arraySize = 1;
        var array = new RM_PROCESS_INFO[arraySize];
        var result = PInvoke.RmGetList((uint)handleScope.Value, out var arrayCount, ref arraySize, array, out var rebootReason);
        if (result is WIN32_ERROR.ERROR_SUCCESS or WIN32_ERROR.ERROR_MORE_DATA)
        {
            RebootReason = (RestartManagerRebootReason)rebootReason;
            return arrayCount > 0;
        }

        throw new Win32Exception((int)result, $"RmGetList failed ({result})");
    }

    /// <summary>Gets a list of processes that are currently locking the registered resources.</summary>
    /// <returns>A read-only list of <see cref="Process"/> instances that are locking the registered resources. The caller owns the returned instances and should dispose them.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public IReadOnlyList<Process> GetProcessesLockingResources()
    {
        var (array, count) = GetList();
        var processes = new List<Process>((int)count);
        for (var i = 0; i < count; i++)
        {
            var process = TryGetProcess(array[i].Process);
            if (process is not null)
                processes.Add(process);
        }

        return processes;
    }

    /// <summary>Gets the applications and services that are currently using the registered resources.</summary>
    /// <returns>A read-only list of <see cref="RestartManagerProcessInfo"/> describing each application or service.</returns>
    /// <remarks>Unlike <see cref="GetProcessesLockingResources"/>, this method reports everything the Restart Manager knows about each application, including its name, type, status and whether it can be restarted, and it also reports applications that have already exited.</remarks>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public IReadOnlyList<RestartManagerProcessInfo> GetLockingProcesses()
    {
        var (array, count) = GetList();
        var result = new List<RestartManagerProcessInfo>((int)count);
        for (var i = 0; i < count; i++)
        {
            result.Add(new RestartManagerProcessInfo(array[i]));
        }

        return result;
    }

    private (RM_PROCESS_INFO[] Array, uint Count) GetList()
    {
        ObjectDisposedException.ThrowIf(_sessionHandle.IsClosed, this);

        using var handleScope = new SafeHandleValue(_sessionHandle);
        uint arraySize = 10;
        while (true)
        {
            var array = new RM_PROCESS_INFO[arraySize];
            var result = PInvoke.RmGetList((uint)handleScope.Value, out var arrayCount, ref arraySize, array, out var rebootReason);
            if (result == WIN32_ERROR.ERROR_SUCCESS)
            {
                RebootReason = (RestartManagerRebootReason)rebootReason;
                return (array, arrayCount);
            }

            if (result != WIN32_ERROR.ERROR_MORE_DATA)
                throw new Win32Exception((int)result, $"RmGetList failed ({result})");

            arraySize = arrayCount;
        }
    }

    /// <summary>Shuts down applications and services that are using the registered resources.</summary>
    /// <param name="action">The shutdown options to use. The Restart Manager documents this parameter as taking one or more of the defined options, so pass at least one flag.</param>
    /// <exception cref="Win32Exception">Thrown when the shutdown operation fails.</exception>
    public void Shutdown(RestartManagerShutdownType action)
    {
        Shutdown(action, statusCallback: null);
    }

    /// <summary>Shuts down applications and services that are using the registered resources.</summary>
    /// <param name="action">The shutdown options to use. The Restart Manager documents this parameter as taking one or more of the defined options, so pass at least one flag.</param>
    /// <param name="statusCallback">An optional callback to receive progress updates during the shutdown operation. The callback is invoked by native code and must not throw; use <see cref="CancelCurrentTask"/> from another thread to stop the operation instead.</param>
    /// <exception cref="Win32Exception">Thrown when the shutdown operation fails.</exception>
    public void Shutdown(RestartManagerShutdownType action, RestartManagerWriteStatusCallback? statusCallback)
    {
        ObjectDisposedException.ThrowIf(_sessionHandle.IsClosed, this);

        RM_WRITE_STATUS_CALLBACK? callback = statusCallback is null ? null : statusCallback.Invoke;
        using var handleScope = new SafeHandleValue(_sessionHandle);
        var result = PInvoke.RmShutdown((uint)handleScope.Value, (uint)action, callback);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmShutdown failed ({result})");
    }

    /// <summary>Restarts applications and services that were shut down by the Restart Manager and that were registered for restart.</summary>
    /// <exception cref="Win32Exception">Thrown when the restart operation fails.</exception>
    public void Restart()
    {
        Restart(statusCallback: null);
    }

    /// <summary>Restarts applications and services that were shut down by the Restart Manager and that were registered for restart.</summary>
    /// <param name="statusCallback">An optional callback to receive progress updates during the restart operation. The callback is invoked by native code and must not throw; use <see cref="CancelCurrentTask"/> from another thread to stop the operation instead.</param>
    /// <exception cref="Win32Exception">Thrown when the restart operation fails.</exception>
    public void Restart(RestartManagerWriteStatusCallback? statusCallback)
    {
        ObjectDisposedException.ThrowIf(_sessionHandle.IsClosed, this);

        RM_WRITE_STATUS_CALLBACK? callback = statusCallback is null ? null : statusCallback.Invoke;
        using var handleScope = new SafeHandleValue(_sessionHandle);
        var result = PInvoke.RmRestart((uint)handleScope.Value, 0, callback);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmRestart failed ({result})");
    }

    /// <summary>Cancels the <see cref="Shutdown(RestartManagerShutdownType)"/> or <see cref="Restart()"/> operation that is currently running on this session.</summary>
    /// <remarks>
    /// <see cref="Shutdown(RestartManagerShutdownType)"/> and <see cref="Restart()"/> block until they complete, so this method
    /// has to be called from another thread while one of them is running. It can only be called by the session that was created
    /// with <see cref="CreateSession"/>, not by a session joined with <see cref="JoinSession(string)"/>.
    /// </remarks>
    /// <exception cref="Win32Exception">Thrown when the cancellation fails.</exception>
    public void CancelCurrentTask()
    {
        ObjectDisposedException.ThrowIf(_sessionHandle.IsClosed, this);

        using var handleScope = new SafeHandleValue(_sessionHandle);
        var result = PInvoke.RmCancelCurrentTask((uint)handleScope.Value);
        if (result != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmCancelCurrentTask failed ({result})");
    }

    // A null entry is marshalled as a NULL pointer, which the Restart Manager accepts silently: it reports
    // ERROR_SUCCESS and simply does not monitor that path, so the caller would be told the file is not locked.
    private static void ThrowIfContainsNull(string[] paths)
    {
        var index = Array.IndexOf(paths, null);
        if (index >= 0)
            throw new ArgumentException($"The path at index {index} is null", nameof(paths));
    }

    private static Process? TryGetProcess(RM_UNIQUE_PROCESS uniqueProcess)
    {
        Process process;
        try
        {
            process = Process.GetProcessById((int)uniqueProcess.dwProcessId);
        }
        catch (ArgumentException)
        {
            // The process exited between RmGetList and now.
            return null;
        }

        if (HasDifferentStartTime(process, uniqueProcess.ProcessStartTime))
        {
            process.Dispose();
            return null;
        }

        return process;
    }

    // Windows recycles process ids, so the id alone does not identify a process: the one running now may not
    // be the one RmGetList saw. RM_UNIQUE_PROCESS carries the start time precisely to tell those apart.
    private static bool HasDifferentStartTime(Process process, FILETIME startTime)
    {
        try
        {
            return process.StartTime.ToFileTime() != startTime.ToFileTime();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The start time is not readable, typically for a protected or system process. Keep the process:
            // dropping it here would hide a real lock holder, which is worse than the recycled-id race.
            return false;
        }
    }

    private static unsafe WIN32_ERROR StartSession(out uint handle, Span<char> sessionKeyBuffer)
    {
        uint localHandle = 0;
        fixed (char* sessionKeyBufferPtr = sessionKeyBuffer)
        {
            var result = PInvoke.RmStartSession(&localHandle, 0, new PWSTR(sessionKeyBufferPtr));
            handle = localHandle;
            return result;
        }
    }

    /// <summary>Ends the Restart Manager session and releases all resources. Calling this method more than once has no effect.</summary>
    public void Dispose()
    {
        _sessionHandle.Dispose();
    }

    /// <summary>Determines whether the specified file is currently locked by any process.</summary>
    /// <param name="path">The full path of the file to check.</param>
    /// <returns><see langword="true"/> if the file is locked; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public static bool IsFileLocked(string path)
    {
        using var restartManager = CreateSession();
        restartManager.RegisterFile(path);
        return restartManager.IsResourcesLocked();
    }

    /// <summary>Gets a list of processes that are currently locking the specified file.</summary>
    /// <param name="path">The full path of the file to check.</param>
    /// <returns>A read-only list of <see cref="Process"/> instances that are locking the file. The caller owns the returned instances and should dispose them.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public static IReadOnlyList<Process> GetProcessesLockingFile(string path)
    {
        using var restartManager = CreateSession();
        restartManager.RegisterFile(path);
        return restartManager.GetProcessesLockingResources();
    }

    /// <summary>Gets a list of processes that are currently locking any of the specified files.</summary>
    /// <param name="paths">An array of full file paths to check.</param>
    /// <returns>A read-only list of <see cref="Process"/> instances that are locking at least one of the files. The caller owns the returned instances and should dispose them.</returns>
    /// <remarks>Prefer this method over calling <see cref="GetProcessesLockingFile(string)"/> in a loop: registering resources performs relatively expensive write operations, so registering all the files in a single session is significantly cheaper, though a session can only hold a bounded number of paths - see <see cref="RegisterFiles(string[])"/>.</remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="paths"/> contains a <see langword="null"/> element.</exception>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public static IReadOnlyList<Process> GetProcessesLockingFiles(string[] paths)
    {
        using var restartManager = CreateSession();
        restartManager.RegisterFiles(paths);
        return restartManager.GetProcessesLockingResources();
    }
}
