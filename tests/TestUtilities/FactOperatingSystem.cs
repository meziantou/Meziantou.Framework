namespace TestUtilities;

[Flags]
public enum FactOperatingSystem
{
    NotSpecified = 0,
    Windows = 1,
    Linux = 2,
    OSX = 4,
    All = Windows | Linux | OSX,
}
