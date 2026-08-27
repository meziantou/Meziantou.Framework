namespace Meziantou.Framework.Win32;

/// <summary>Describes the type of an application that is using a resource registered with a Restart Manager session.</summary>
public enum RestartManagerApplicationType
{
    /// <summary>The application cannot be classified. It may not respond to shutdown requests and may have to be forced to shut down.</summary>
    Unknown = 0,

    /// <summary>A Windows application that runs in its own process and has a top-level window.</summary>
    MainWindow = 1,

    /// <summary>A Windows application that does not run in its own process and does not have a top-level window.</summary>
    OtherWindow = 2,

    /// <summary>A Windows service.</summary>
    Service = 3,

    /// <summary>Windows Explorer.</summary>
    Explorer = 4,

    /// <summary>A console application.</summary>
    Console = 5,

    /// <summary>A critical process that must be shut down by a system restart.</summary>
    Critical = 1000,
}
