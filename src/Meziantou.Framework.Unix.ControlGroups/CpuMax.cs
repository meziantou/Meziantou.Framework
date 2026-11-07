using System.Runtime.InteropServices;

namespace Meziantou.Framework.Unix.ControlGroups;

/// <summary>Represents the CPU bandwidth limit of a cgroup.</summary>
/// <param name="MaxMicroseconds">Maximum time in microseconds the cgroup can run during one period, or <see langword="null"/> when the cgroup is unlimited.</param>
/// <param name="PeriodMicroseconds">Length of the accounting period in microseconds.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct CpuMax(long? MaxMicroseconds, long PeriodMicroseconds);
