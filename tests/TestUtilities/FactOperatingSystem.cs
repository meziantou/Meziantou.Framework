using System;

namespace TestUtilities
{
    [Flags]
    public enum FactOperatingSystem
    {
        All = 0,
        Windows = 1,
        Linux = 2,
        OSX = 4,
    }
}
