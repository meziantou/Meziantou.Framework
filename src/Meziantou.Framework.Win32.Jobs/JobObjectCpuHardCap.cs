using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

[StructLayout(LayoutKind.Sequential)]
public struct JobObjectCpuHardCap
{
    /// <summary>
    /// Whether the CPU hard cap is enabled for this job.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The value of the CPU hard cap.
    /// </summary>
    public int Rate { get; set; }
}
