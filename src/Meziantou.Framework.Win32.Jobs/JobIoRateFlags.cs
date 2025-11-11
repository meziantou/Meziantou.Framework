namespace Meziantou.Framework.Win32;

/// <summary>Defines flags for controlling I/O rate limits on a job object.</summary>
[Flags]
public enum JobIoRateFlags
{
    /// <summary>Enables the I/O rate control.</summary>
    Enable = 1,

    /// <summary>Indicates that the I/O rate control is applied to a standalone volume.</summary>
    StandaloneVolume = 2,

    /// <summary>Forces all I/O operations to be treated as unit access operations.</summary>
    ForceUnitAccessAll = 4,

    /// <summary>Forces unit access on soft cap limits.</summary>
    ForceUnitAccessOnSoftCap = 8,

    /// <summary>Combines all valid flags for I/O rate control.</summary>
    ValidFlags = 15,
}
