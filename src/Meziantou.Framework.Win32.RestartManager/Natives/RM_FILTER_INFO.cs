using System.Runtime.InteropServices;

namespace Meziantou.Framework.Win32.Natives;

/// <summary>Contains information about modifications to restart or shutdown actions. Add, remove, and list modifications to specified applications and services that have been registered with the Restart Manager session by using the RmAddFilter, RmRemoveFilter, and the RmGetFilterList functions.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct RM_FILTER_INFO
{
    /// <summary>
    /// This member contains a RM_FILTER_ACTION enumeration value. Use the value RmNoRestart to prevent the restart of the application or service. Use the value RmNoShutdown to prevent the shutdown and restart of the application or service.
    /// </summary>
    public RM_FILTER_ACTION FilterAction;

    /// <summary>
    /// This member contains a RM_FILTER_TRIGGER enumeration value. Use the value RmFilterTriggerFile to modify the restart or shutdown actions of an application referenced by the executable's full path filename. Use the value RmFilterTriggerProcess to modify the restart or shutdown actions of an application referenced by a RM_UNIQUE_PROCESS structure. Use the value RmFilterTriggerService to modify the restart or shutdown actions of a service referenced by the short service name.
    /// </summary>
    public RM_FILTER_TRIGGER FilterTrigger;

    /// <summary>
    /// The offset in bytes to the next structure.
    /// </summary>
    public uint NextOffset;

    /// <summary>
    /// If the value of FilterTrigger is RmFilterTriggerFile, this member contains a pointer to a string value that contains the application filename.
    /// </summary>
    public string Filename;

    /// <summary>
    /// If the value of FilterTrigger is RmFilterTriggerProcess, this member is a RM_PROCESS_INFO structure for the application.
    /// </summary>
    public RM_UNIQUE_PROCESS Process;

    /// <summary>
    /// If the value of FilterTrigger is RmFilterTriggerService this member is a pointer to a string value that contains the short service name.
    /// </summary>
    public string ServiceShortName;
}
