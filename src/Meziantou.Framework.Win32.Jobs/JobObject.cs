using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Win32.Natives;
using Microsoft.Win32.SafeHandles;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;

namespace Meziantou.Framework.Win32;

/// <summary>
/// A utility class that represents a Windows job object. Job objects allow groups of processes to be managed as a unit.
/// </summary>
[SupportedOSPlatform("windows5.1.2600")]
public sealed class JobObject : IDisposable
{
    private readonly SafeFileHandle _jobHandle;

    private JobObject(SafeFileHandle jobHandle)
    {
        _jobHandle = jobHandle;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobObject"/> class. The associated job object will have no name.
    /// </summary>
    public JobObject()
        : this(name: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobObject"/> class.
    /// </summary>
    /// <param name="name">The job object name. May be null.</param>
    public unsafe JobObject(string? name)
    {
        var atts = new Windows.Win32.Security.SECURITY_ATTRIBUTES
        {
            bInheritHandle = true,
            lpSecurityDescriptor = IntPtr.Zero.ToPointer(),
            nLength = (uint)Marshal.SizeOf(typeof(Windows.Win32.Security.SECURITY_ATTRIBUTES)),
        };

        _jobHandle = Windows.Win32.PInvoke.CreateJobObject(atts, name);
        if (_jobHandle.IsInvalid)
        {
            _jobHandle.Dispose();
            var lastError = Marshal.GetLastWin32Error();
            throw new Win32Exception(lastError);
        }
    }

    public static JobObject Open(JobObjectAccessRights desiredAccess, bool inherited, string name)
    {
        var handle = Windows.Win32.PInvoke.OpenJobObject((uint)desiredAccess, inherited, name);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            var lastError = Marshal.GetLastWin32Error();
            throw new Win32Exception(lastError);
        }

        return new JobObject(handle);
    }

    public void Dispose() => _jobHandle.Dispose();

    /// <summary>
    /// Terminates all processes currently associated with the job. If the job is nested, this function terminates all processes currently associated with the job and all of its child jobs in the hierarchy.
    /// </summary>
    public void Terminate()
    {
        Terminate(1);
    }

    /// <summary>
    /// Terminates all processes currently associated with the job. If the job is nested, this function terminates all processes currently associated with the job and all of its child jobs in the hierarchy.
    /// </summary>
    /// <param name="exitCode">The exit code to be used by all processes and threads in the job object.</param>
    public void Terminate(int exitCode)
    {
        Windows.Win32.PInvoke.TerminateJobObject(_jobHandle, unchecked((uint)exitCode));
    }

    /// <summary>
    /// Assigns a process to an existing job object.
    /// </summary>
    /// <param name="process">The process.</param>
    public void AssignProcess(Process process)
    {
        if (process is null)
            throw new ArgumentNullException(nameof(process));

        AssignProcess(process.Handle);
    }

    /// <summary>
    /// Assigns a process to an existing job object.
    /// </summary>
    /// <param name="processHandle">The process handle.</param>
    /// <returns>
    /// true if the function succeeds; otherwise false.
    /// </returns>
    public void AssignProcess(IntPtr processHandle)
    {
        if (!Windows.Win32.PInvoke.AssignProcessToJobObject((HANDLE)_jobHandle.DangerousGetHandle(), (HANDLE)processHandle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    /// <summary>
    /// Sets limits to the jhob.
    /// </summary>
    /// <param name="limits">The limits. May not be null.</param>
    public unsafe void SetLimits(JobObjectLimits limits)
    {
        if (limits is null)
            throw new ArgumentNullException(nameof(limits));
        var info = JOBOBJECT_INFO.From(limits);
        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &info, JOBOBJECT_INFO.Size))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    public unsafe void SetUIRestrictions(JobObjectUILimit limits)
    {
        var restriction = new JOBOBJECT_BASIC_UI_RESTRICTIONS
        {
            UIRestrictionsClass = limits,
        };

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectBasicUIRestrictions, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_BASIC_UI_RESTRICTIONS>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    public unsafe bool IsAssignedToProcess(Process process)
    {
        if (process is null)
            throw new ArgumentNullException(nameof(process));

        BOOL result = default;
        if (Windows.Win32.PInvoke.IsProcessInJob((HANDLE)process.Handle, (HANDLE)_jobHandle.DangerousGetHandle(), &result))
            return result;

        var err = Marshal.GetLastWin32Error();
        throw new Win32Exception(err);
    }
}
