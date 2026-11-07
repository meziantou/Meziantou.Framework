namespace Meziantou.Framework.Win32;

/// <summary>Describes the current status of an application that is using a resource registered with a Restart Manager session.</summary>
[Flags]
public enum RestartManagerApplicationStatus
{
    /// <summary>The status is not known.</summary>
    Unknown = 0,

    /// <summary>The application is running.</summary>
    Running = 0x1,

    /// <summary>The application has been stopped by the Restart Manager.</summary>
    Stopped = 0x2,

    /// <summary>The application has been stopped by a means other than the Restart Manager.</summary>
    StoppedOther = 0x4,

    /// <summary>The application has been restarted by the Restart Manager.</summary>
    Restarted = 0x8,

    /// <summary>The Restart Manager failed to stop the application.</summary>
    ErrorOnStop = 0x10,

    /// <summary>The Restart Manager failed to restart the application.</summary>
    ErrorOnRestart = 0x20,

    /// <summary>Shutdown of the application has been masked by a filter.</summary>
    ShutdownMasked = 0x40,

    /// <summary>Restart of the application has been masked by a filter.</summary>
    RestartMasked = 0x80,
}
