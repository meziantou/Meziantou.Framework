using System;

namespace TestUtilities
{
    [Flags]
    public enum FactOperatingSystem
    {
        None = 0,
        Windows = 1,
        Linux = 2,
        OSX = 4,
    }
}
