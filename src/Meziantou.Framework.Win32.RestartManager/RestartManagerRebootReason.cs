namespace Meziantou.Framework.Win32;

/// <summary>Describes why a system restart is required to free the resources registered with a Restart Manager session.</summary>
/// <remarks>Any value other than <see cref="None"/> means the Restart Manager cannot free the registered resources by shutting applications down, and that a system restart is required instead.</remarks>
[Flags]
public enum RestartManagerRebootReason
{
    /// <summary>A system restart is not required.</summary>
    None = 0,

    /// <summary>The current process does not have enough privileges to shut down one or more of the applications.</summary>
    PermissionDenied = 0x1,

    /// <summary>One or more of the applications is running in a different Terminal Services session.</summary>
    SessionMismatch = 0x2,

    /// <summary>One or more of the applications is a critical process.</summary>
    CriticalProcess = 0x4,

    /// <summary>One or more of the applications is a critical service.</summary>
    CriticalService = 0x8,

    /// <summary>The current process is using one of the registered resources itself.</summary>
    DetectedSelf = 0x10,
}
