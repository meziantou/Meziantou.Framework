namespace Meziantou.Framework.Win32;

[Flags]
public enum JobIoRateFlags
{
    Enable = 1,
    StandaloneVolume = 2,
    ForceUnitAccessAll = 4,
    ForceUnitAccessOnSoftCap = 8,
    ValidFlags = 15,
}
