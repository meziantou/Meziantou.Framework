using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;

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
/// // Get processes locking a file
/// var processes = RestartManager.GetProcessesLockingFile(@"C:\path\to\file.txt");
/// foreach (var process in processes)
/// {
///     Console.WriteLine($"{process.ProcessName} (PID: {process.Id})");
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
[SupportedOSPlatform("windows")]
public sealed class RestartManager : IDisposable
{
    private int SessionHandle { get; }

    /// <summary>Gets the session key for this Restart Manager session.</summary>
    public string SessionKey { get; }

    private RestartManager(int sessionHandle, string sessionKey)
    {
        SessionHandle = sessionHandle;
        SessionKey = sessionKey;
    }

    /// <summary>Creates a new Restart Manager session.</summary>
    /// <returns>A new <see cref="RestartManager"/> instance representing the session.</returns>
    /// <exception cref="Win32Exception">Thrown when the session creation fails.</exception>
    public static RestartManager CreateSession()
    {
        var sessionKey = Guid.NewGuid().ToString();
        var result = NativeMethods.RmStartSession(out var handle, 0, strSessionKey: sessionKey);
        if (result != RmResult.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmStartSession failed ({result})");

        return new RestartManager(handle, sessionKey);
    }

    /// <summary>Joins an existing Restart Manager session using the specified session key.</summary>
    /// <param name="sessionKey">The session key of an existing Restart Manager session.</param>
    /// <returns>A <see cref="RestartManager"/> instance representing the joined session.</returns>
    /// <exception cref="Win32Exception">Thrown when joining the session fails.</exception>
    public static RestartManager JoinSession(string sessionKey)
    {
        var result = NativeMethods.RmJoinSession(out var handle, sessionKey);
        if (result != RmResult.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmStartSession failed ({result})");

        return new RestartManager(handle, sessionKey);
    }

    /// <summary>Registers a file to be monitored by the Restart Manager session.</summary>
    /// <param name="path">The full path of the file to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">Thrown when the registration fails.</exception>
    public void RegisterFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string[] resources = [path];
        var result = NativeMethods.RmRegisterResources(SessionHandle, (uint)resources.Length, resources, 0, rgApplications: null, 0, rgsServiceNames: null);
        if (result != RmResult.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmRegisterResources failed ({result})");
    }

    /// <summary>Registers multiple files to be monitored by the Restart Manager session.</summary>
    /// <param name="paths">An array of full file paths to register.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="paths"/> is <see langword="null"/>.</exception>
    /// <exception cref="Win32Exception">Thrown when the registration fails.</exception>
    public void RegisterFiles(string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var result = NativeMethods.RmRegisterResources(SessionHandle, (uint)paths.LongLength, paths, 0, rgApplications: null, 0, rgsServiceNames: null);
        if (result != RmResult.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmRegisterResources failed ({result})");
    }

    /// <summary>Determines whether any of the registered resources are currently locked by running processes.</summary>
    /// <returns><see langword="true"/> if at least one registered resource is locked; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public bool IsResourcesLocked()
    {
        uint arraySize = 1;
        while (true)
        {
            var array = new RM_PROCESS_INFO[arraySize];
            var result = NativeMethods.RmGetList(SessionHandle, out var arrayCount, ref arraySize, array, out _);
            if (result is RmResult.ERROR_SUCCESS or RmResult.ERROR_MORE_DATA)
            {
                return arrayCount > 0;
            }

            throw new Win32Exception((int)result, $"RmGetList failed ({result})");
        }
    }

    /// <summary>Gets a list of processes that are currently locking the registered resources.</summary>
    /// <returns>A read-only list of <see cref="Process"/> instances that are locking the registered resources.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public IReadOnlyList<Process> GetProcessesLockingResources()
    {
        uint arraySize = 10;
        while (true)
        {
            var array = new RM_PROCESS_INFO[arraySize];
            var result = NativeMethods.RmGetList(SessionHandle, out var arrayCount, ref arraySize, array, out _);
            if (result == RmResult.ERROR_SUCCESS)
            {
                var processes = new List<Process>((int)arrayCount);
                for (var i = 0; i < arrayCount; i++)
                {
                    try
                    {
                        var process = Process.GetProcessById(array[i].Process.ProcessId);
                        if (process is not null)
                            processes.Add(process);
                    }
                    catch
                    {
                    }
                }

                return processes;
            }
            else if (result == RmResult.ERROR_MORE_DATA)
            {
                arraySize = arrayCount;
            }
            else
            {
                throw new Win32Exception((int)result, $"RmGetList failed ({result})");
            }
        }
    }

    /// <summary>Shuts down applications and services that are using the registered resources.</summary>
    /// <param name="action">The shutdown options to use.</param>
    /// <exception cref="Win32Exception">Thrown when the shutdown operation fails.</exception>
    public void Shutdown(RestartManagerShutdownType action)
    {
        Shutdown(action, statusCallback: null);
    }

    /// <summary>Shuts down applications and services that are using the registered resources.</summary>
    /// <param name="action">The shutdown options to use.</param>
    /// <param name="statusCallback">An optional callback to receive progress updates during the shutdown operation.</param>
    /// <exception cref="Win32Exception">Thrown when the shutdown operation fails.</exception>
    public void Shutdown(RestartManagerShutdownType action, RestartManagerWriteStatusCallback? statusCallback)
    {
        var result = NativeMethods.RmShutdown(SessionHandle, action, statusCallback);
        if (result != RmResult.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmShutdown failed ({result})");
    }

    /// <summary>Restarts applications and services that were shut down by the Restart Manager and that were registered for restart.</summary>
    /// <exception cref="Win32Exception">Thrown when the restart operation fails.</exception>
    public void Restart()
    {
        Restart(statusCallback: null);
    }

    /// <summary>Restarts applications and services that were shut down by the Restart Manager and that were registered for restart.</summary>
    /// <param name="statusCallback">An optional callback to receive progress updates during the restart operation.</param>
    /// <exception cref="Win32Exception">Thrown when the restart operation fails.</exception>
    public void Restart(RestartManagerWriteStatusCallback? statusCallback)
    {
        var result = NativeMethods.RmRestart(SessionHandle, 0, statusCallback);
        if (result != RmResult.ERROR_SUCCESS)
            throw new Win32Exception((int)result, $"RmShutdown failed ({result})");
    }

    /// <summary>Ends the Restart Manager session and releases all resources.</summary>
    /// <exception cref="Win32Exception">Thrown when ending the session fails.</exception>
    [SuppressMessage("Design", "CA1065:Do not raise exceptions in unexpected locations", Justification = "<Pending>")]
    public void Dispose()
    {
        if (SessionHandle != 0)
        {
            var result = NativeMethods.RmEndSession(SessionHandle);
            if (result != RmResult.ERROR_SUCCESS)
                throw new Win32Exception((int)result, $"RmEndSession failed ({result})");
        }
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
    /// <returns>A read-only list of <see cref="Process"/> instances that are locking the file.</returns>
    /// <exception cref="Win32Exception">Thrown when the operation fails.</exception>
    public static IReadOnlyList<Process> GetProcessesLockingFile(string path)
    {
        using var restartManager = CreateSession();
        restartManager.RegisterFile(path);
        return restartManager.GetProcessesLockingResources();
    }
}
