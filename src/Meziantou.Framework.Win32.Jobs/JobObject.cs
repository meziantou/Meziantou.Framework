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
        : this(name, inheritHandle: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="JobObject"/> class.
    /// </summary>
    /// <param name="name">The job object name. May be null.</param>
    /// <param name="inheritHandle">A Boolean value that specifies whether the returned handle is inherited when a new process is created. If this member is <see langword="true" />, the new process inherits the handle.</param>
    public unsafe JobObject(string? name, bool inheritHandle)
    {
        var atts = new Windows.Win32.Security.SECURITY_ATTRIBUTES
        {
            bInheritHandle = inheritHandle,
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

    // Win32 error code
    private const int ERROR_FILE_NOT_FOUND = 2;

    public static bool TryOpen(JobObjectAccessRights desiredAccess, bool inherited, string name, out JobObject jobObject)
    {
        var handle = Windows.Win32.PInvoke.OpenJobObject((uint)desiredAccess, inherited, name);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            var lastError = Marshal.GetLastWin32Error();
            if (lastError == ERROR_FILE_NOT_FOUND)
            {
                jobObject = null;
                return false;
            }
            throw new Win32Exception(lastError);
        }

        jobObject = new JobObject(handle);
        return true;
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
        ArgumentNullException.ThrowIfNull(process);

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
    /// Sets limits to the job.
    /// </summary>
    /// <param name="limits">The limits. May not be null.</param>
    public unsafe void SetLimits(JobObjectLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                ActiveProcessLimit = limits.ActiveProcessLimit,
                Affinity = limits.Affinity,
                MaximumWorkingSetSize = limits.MaximumWorkingSetSize,
                MinimumWorkingSetSize = limits.MinimumWorkingSetSize,
                PerJobUserTimeLimit = limits.PerJobUserTimeLimit,
                PerProcessUserTimeLimit = limits.PerProcessUserTimeLimit,
                PriorityClass = limits.PriorityClass,
                SchedulingClass = limits.SchedulingClass,
                LimitFlags = limits.InternalFlags,
            },
            ProcessMemoryLimit = limits.ProcessMemoryLimit,
            JobMemoryLimit = limits.JobMemoryLimit,
        };

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &info, (uint)Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    public unsafe void SetUIRestrictions(JobObjectUILimit limits)
    {
        var restriction = new JOBOBJECT_BASIC_UI_RESTRICTIONS
        {
            UIRestrictionsClass = (JOB_OBJECT_UILIMIT)limits,
        };

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectBasicUIRestrictions, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_BASIC_UI_RESTRICTIONS>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    /// <summary>
    /// Set the job's CPU rate is a hard limit. After the job reaches its CPU
    /// cycle limit for the current scheduling interval, no threads associated
    /// with the job will run until the next interval.
    /// </summary>
    /// <param name="cpuRate">
    /// Specifies the portion of processor cycles that the threads in a job object
    /// can use during each scheduling interval, as the number of cycles per 10,000 cycles.
    /// For example, to let the job use 20% of the CPU, set CpuRate to 20 times 100, or 2,000.
    /// </param>
    public unsafe void SetCpuRateHardCap(int cpuRate)
    {
        var restriction = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL.JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
            Anonymous = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION._Anonymous_e__Union
            {
                CpuRate = (uint)cpuRate,
            },
        };

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectCpuRateControlInformation, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    /// <summary>
    /// Get the job's CPU rate limit enabled status and value.
    /// </summary>
    /// <returns>Bool indicating if CPU rate control is enabled and the job's CPU rate limit.</returns>
    public unsafe JobObjectCpuHardCap GetCpuRateHardCap()
    {
        var restriction = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();

        if (!Windows.Win32.PInvoke.QueryInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectCpuRateControlInformation, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>(), null))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }

        var cpuRateEnabled = restriction.ControlFlags.HasFlag(JOB_OBJECT_CPU_RATE_CONTROL.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE);

        return new JobObjectCpuHardCap
        {
            Enabled = cpuRateEnabled,
            Rate = (int)restriction.Anonymous.CpuRate,
        };
    }

    /// <summary>
    /// Disables the job's CPU rate limit.
    /// </summary>
    public unsafe void DisableCpuRateHardCap()
    {
        var restriction = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectCpuRateControlInformation, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    /// <summary>
    /// Set the job's CPU rate is calculated based on its relative weight to the weight of other jobs.
    /// </summary>
    /// <param name="weight">
    /// Specifies the scheduling weight of the job object, which determines the share of processor time given to the job relative to other workloads on the processor.
    /// This member can be a value from 1 through 9, where 1 is the smallest share and 9 is the largest share.The default is 5, which should be used for most workloads.
    /// </param>
    public unsafe void SetCpuRateWeight(int weight)
    {
        var restriction = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL.JOB_OBJECT_CPU_RATE_CONTROL_WEIGHT_BASED,
            Anonymous = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION._Anonymous_e__Union
            {
                Weight = (uint)weight,
            },
        };

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectCpuRateControlInformation, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    /// <summary>
    /// Set the job's CPU rate is a hard limit. After the job reaches its CPU cycle limit for the current scheduling interval, no threads associated with the job will run until the next interval.
    /// </summary>
    /// <param name="minRate">
    /// Specifies the minimum portion of the processor cycles that the threads in a job object can reserve during each scheduling interval.
    /// Specify this rate as a percentage times 100. For example, to set a minimum rate of 50%, specify 50 times 100, or 5,000.
    /// </param>
    /// <param name="maxRate">
    /// Specifies the maximum portion of processor cycles that the threads in a job object can use during each scheduling interval.
    /// Specify this rate as a percentage times 100. For example, to set a maximum rate of 50%, specify 50 times 100, or 5,000.
    ///
    /// After the job reaches this limit for a scheduling interval, no threads associated with the job can run until the next scheduling interval.
    /// </param>
    public unsafe void SetCpuRate(int minRate, int maxRate)
    {
        var restriction = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = JOB_OBJECT_CPU_RATE_CONTROL.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | JOB_OBJECT_CPU_RATE_CONTROL.JOB_OBJECT_CPU_RATE_CONTROL_MIN_MAX_RATE,
            Anonymous = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION._Anonymous_e__Union
            {
                Anonymous = new JOBOBJECT_CPU_RATE_CONTROL_INFORMATION._Anonymous_e__Union._Anonymous_e__Struct
                {
                    MinRate = (ushort)minRate,
                    MaxRate = (ushort)maxRate,
                },
            },
        };

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectCpuRateControlInformation, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    /// <summary>
    /// Set bandwidth limits for the job.
    /// </summary>
    /// <param name="maxBandwidth">The maximum bandwidth for outgoing network traffic for the job, in bytes.</param>
    public unsafe void SetNetRateLimits(ulong maxBandwidth)
    {
        var restriction = new JOBOBJECT_NET_RATE_CONTROL_INFORMATION
        {
            ControlFlags = JOB_OBJECT_NET_RATE_CONTROL_FLAGS.JOB_OBJECT_NET_RATE_CONTROL_ENABLE | JOB_OBJECT_NET_RATE_CONTROL_FLAGS.JOB_OBJECT_NET_RATE_CONTROL_MAX_BANDWIDTH,
            MaxBandwidth = maxBandwidth,
        };

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectNetRateControlInformation, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_NET_RATE_CONTROL_INFORMATION>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }

    /// <summary>
    /// Set security limits for the job.
    /// </summary>
    public unsafe void SetSecurityLimits(JobObjectSecurityLimit securityLimit)
    {
        var restriction = new JOBOBJECT_SECURITY_LIMIT_INFORMATION
        {
            SecurityLimitFlags = (JOB_OBJECT_SECURITY)securityLimit,
        };

        if (!Windows.Win32.PInvoke.SetInformationJobObject(_jobHandle, JOBOBJECTINFOCLASS.JobObjectSecurityLimitInformation, &restriction, (uint)Marshal.SizeOf<JOBOBJECT_SECURITY_LIMIT_INFORMATION>()))
        {
            var err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
    }
    /// <summary>
    /// Sets I/O limits on a job object.
    /// </summary>
    [SupportedOSPlatform("windows10.0.10240")]
    public unsafe void SetIoLimits(JobIoRateLimits limits)
    {
        fixed (char* volumeNamePtr = limits.VolumeName)
        {
            var restriction = new JOBOBJECT_IO_RATE_CONTROL_INFORMATION
            {
                ControlFlags = (JOB_OBJECT_IO_RATE_CONTROL_FLAGS)limits.ControlFlags,
                BaseIoSize = limits.BaseIoSize,
                MaxBandwidth = limits.MaxBandwidth,
                MaxIops = limits.MaxIops,
                ReservationIops = limits.ReservationIops,
                VolumeName = volumeNamePtr,
            };

            if (Windows.Win32.PInvoke.SetIoRateControlInformationJobObject(_jobHandle, in restriction) == 0)
            {
                var err = Marshal.GetLastWin32Error();
                throw new Win32Exception(err);
            }
        }
    }

    public unsafe bool IsAssignedToProcess(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        BOOL result = default;
        if (Windows.Win32.PInvoke.IsProcessInJob((HANDLE)process.Handle, (HANDLE)_jobHandle.DangerousGetHandle(), &result))
            return result;

        var err = Marshal.GetLastWin32Error();
        throw new Win32Exception(err);
    }
}
