#nullable disable
using System;

namespace Meziantou.Framework.Win32.Natives
{
    /// <summary>
    /// Describes the reasons a restart of the system is needed.
    /// </summary>
    [Flags]
    internal enum RM_REBOOT_REASON
    {
        /// <summary>
        /// A system restart is not required.
        /// </summary>
        RmRebootReasonNone = 0x0,

        /// <summary>
        /// The current user does not have sufficient privileges to shut down one or more processes.
        /// </summary>
        RmRebootReasonPermissionDenied = 0x1,

        /// <summary>
        /// One or more processes are running in another Terminal Services session.
        /// </summary>
        RmRebootReasonSessionMismatch = 0x2,

        /// <summary>
        /// A system restart is needed because one or more processes to be shut down are critical processes.
        /// </summary>
        RmRebootReasonCriticalProcess = 0x4,

        /// <summary>
        /// A system restart is needed because one or more services to be shut down are critical services.
        /// </summary>
        RmRebootReasonCriticalService = 0x8,

        /// <summary>
        /// A system restart is needed because the current process must be shut down.
        /// </summary>
        RmRebootReasonDetectedSelf = 0x10,
    }
}
