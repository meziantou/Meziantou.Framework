using System;

namespace Meziantou.Framework.Win32
{
    /// <summary>
    /// Defines options for a job object.
    /// </summary>
    // NOTE: must match Natives.LimitFlags
    [Flags]
    public enum JobObjectLimitFlags
    {
        /// <summary>
        /// Forces a call to the SetErrorMode function with the SEM_NOGPFAULTERRORBOX flag for each process associated with the job.
        /// </summary>
        DieOnUnhandledException = 0x00000400,

        /// <summary>
        /// If any process associated with the job creates a child process using the CREATE_BREAKAWAY_FROM_JOB flag while this limit is in effect, the child process is not associated with the job.
        /// </summary>
        BreakawayOk = 0x00000800,

        /// <summary>
        /// Allows any process associated with the job to create child processes that are not associated with the job.
        /// </summary>
        SilentBreakawayOk = 0x00001000,

        /// <summary>
        /// Causes all processes associated with the job to terminate when the last handle to the job is closed.
        /// </summary>
        KillOnJobClose = 0x00002000,

        /// <summary>
        /// Allows processes to use a subset of the processor affinity for all processes associated with the job.
        /// </summary>
        SubsetAffinity = 0x00004000,
    }
}
