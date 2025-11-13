using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32;

/// <summary>Represents the CPU rate hard cap settings for a job object.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct JobObjectCpuHardCap
{
    /// <summary>Whether the CPU hard cap is enabled for this job.</summary>
    public bool Enabled { get; set; }

    /// <summary>The value of the CPU hard cap.</summary>
    public int Rate { get; set; }
}
