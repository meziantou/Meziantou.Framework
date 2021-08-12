using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

/// <summary>
/// Uniquely identifies a process by its PID and the time the process began.
/// An array of RM_UNIQUE_PROCESS structures can be passed to the RmRegisterResources function. 
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RM_UNIQUE_PROCESS
{
    public int ProcessId;
    public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;

    public static RM_UNIQUE_PROCESS GetProcesses(Process process)
    {
        var rp = new RM_UNIQUE_PROCESS
        {
            ProcessId = process.Id,
        };

        NativeMethods.GetProcessTimes(process.Handle, out var creationTime, out _, out _, out _);
        rp.ProcessStartTime = creationTime;
        return rp;
    }
}
