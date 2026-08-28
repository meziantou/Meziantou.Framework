using System.Runtime.Versioning;
using Windows.Win32.System.RestartManager;

namespace Meziantou.Framework.Win32;

/// <summary>Describes an application or service that is using a resource registered with a Restart Manager session.</summary>
[SupportedOSPlatform("windows")]
public sealed class RestartManagerProcessInfo
{
    internal RestartManagerProcessInfo(RM_PROCESS_INFO processInfo)
    {
        ProcessId = (int)processInfo.Process.dwProcessId;
        StartTime = processInfo.Process.ProcessStartTime.ToDateTime();
        ApplicationName = processInfo.strAppName.ToString();
        ServiceShortName = processInfo.strServiceShortName.ToString();
        ApplicationType = (RestartManagerApplicationType)processInfo.ApplicationType;
        Status = (RestartManagerApplicationStatus)processInfo.AppStatus;
        TerminalServicesSessionId = (int)processInfo.TSSessionId;
        IsRestartable = processInfo.bRestartable;
    }

    /// <summary>Gets the identifier of the process.</summary>
    public int ProcessId { get; }

    /// <summary>Gets the time at which the process started. Windows recycles process identifiers, so only the combination of <see cref="ProcessId"/> and <see cref="StartTime"/> identifies a process.</summary>
    public DateTime StartTime { get; }

    /// <summary>Gets the friendly name of the application.</summary>
    public string ApplicationName { get; }

    /// <summary>Gets the short name of the service, or an empty string when the process is not a service.</summary>
    public string ServiceShortName { get; }

    /// <summary>Gets the type of the application.</summary>
    public RestartManagerApplicationType ApplicationType { get; }

    /// <summary>Gets the current status of the application.</summary>
    public RestartManagerApplicationStatus Status { get; }

    /// <summary>Gets the Terminal Services session identifier in which the process is running.</summary>
    public int TerminalServicesSessionId { get; }

    /// <summary>Gets a value indicating whether the Restart Manager can restart the application after shutting it down.</summary>
    public bool IsRestartable { get; }
}
