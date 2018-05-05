using System;
using System.Runtime.InteropServices;
using FILETIME = System.Runtime.InteropServices.ComTypes.FILETIME;

namespace Meziantou.Framework.Win32.Natives
{
    internal static class NativeMethods
    {
        /// <summary>
        /// Registers resources to a Restart Manager session.
        /// The Restart Manager uses the list of resources registered with the session to determine which applications
        /// and services must be shut down and restarted. Resources can be identified by filenames, service short names,
        /// or RM_UNIQUE_PROCESS structures that describe running applications.
        /// The RmRegisterResources function can be used by a primary or secondary installer.
        /// </summary>
        /// <param name="dwSessionHandle">A handle to an existing Restart Manager session.</param>
        /// <param name="nFiles">The number of files being registered.</param>
        /// <param name="rgsFilenames">An array of null-terminated strings of full filename paths. This parameter can be NULL if nFiles is 0.</param>
        /// <param name="nApplications">The number of processes being registered.</param>
        /// <param name="rgApplications">An array of RM_UNIQUE_PROCESS structures. This parameter can be NULL if nApplications is 0.</param>
        /// <param name="nServices">The number of services to be registered.</param>
        /// <param name="rgsServiceNames">An array of null-terminated strings of service short names. This parameter can be NULL if nServices is 0.</param>
        /// <returns>This is the most recent error received. The function can return one of the system error codes that are defined in Winerror.h.</returns>
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        public static extern RmResult RmRegisterResources(
            int dwSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices,
            string[] rgsServiceNames);

        /// <summary>
        /// Starts a new Restart Manager session.
        /// A maximum of 64 Restart Manager sessions per user session can be open on the system at the same time.
        /// When this function starts a session, it returns a session handle and session key that can be used in subsequent calls to the Restart Manager API.
        /// </summary>
        /// <param name="pSessionHandle">
        /// A pointer to the handle of a Restart Manager session.
        /// The session handle can be passed in subsequent calls to the Restart Manager API.
        /// </param>
        /// <param name="dwSessionFlags">Reserved. This parameter should be 0.</param>
        /// <param name="strSessionKey">
        /// A null-terminated string that contains the session key to the new session.
        /// The string must be allocated before calling the RmStartSession function.
        /// </param>
        /// <returns>This is the most recent error received. The function can return one of the system error codes that are defined in Winerror.h.</returns>
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
        public static extern RmResult RmStartSession(out int pSessionHandle, int dwSessionFlags, string strSessionKey);

        /// <summary>
        /// Joins a secondary installer to an existing Restart Manager session. This function must be called with a session key that can only be obtained from the primary installer that started the session. A valid session key is required to use any of the Restart Manager functions. After a secondary installer joins a session, it can call the RmRegisterResources function to register resources.
        /// </summary>
        /// <param name="pSessionHandle">
        /// A pointer to the handle of a Restart Manager session.
        /// </param>
        /// <param name="strSessionKey">
        /// A null-terminated string that contains the session key of an existing session.
        /// </param>
        /// <returns>This is the most recent error received. The function can return one of the system error codes that are defined in Winerror.h.</returns>
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Auto)]
        public static extern RmResult RmJoinSession(out int pSessionHandle, string strSessionKey);

        /// <summary>
        /// Ends the Restart Manager session.
        /// This function should be called by the primary installer that has previously started the session by calling the RmStartSession function.
        /// The RmEndSession function can be called by a secondary installer that is joined to the session once no more resources need to be registered
        /// by the secondary installer. 
        /// </summary>
        /// <param name="dwSessionHandle">A handle to an existing Restart Manager session.</param>
        /// <returns>This is the most recent error received. The function can return one of the system error codes that are defined in Winerror.h.</returns>
        [DllImport("rstrtmgr.dll")]
        public static extern RmResult RmEndSession(int dwSessionHandle);

        /// <summary>
        /// Gets a list of all applications and services that are currently using resources that have been registered with the Restart Manager session.
        /// </summary>
        /// <param name="dwSessionHandle">A handle to an existing Restart Manager session.</param>
        /// <param name="pnProcInfoNeeded">
        /// A pointer to an array size necessary to receive RM_PROCESS_INFO structures required to return information for all affected applications and services.
        /// </param>
        /// <param name="pnProcInfo">A pointer to the total number of RM_PROCESS_INFO structures in an array and number of structures filled.</param>
        /// <param name="rgAffectedApps">
        /// An array of RM_PROCESS_INFO structures that list the applications and services using resources that have been registered with the session.
        /// </param>
        /// <param name="lpdwRebootReasons">
        /// Pointer to location that receives a value of the RM_REBOOT_REASON enumeration that describes the reason a system restart is needed.
        /// </param>
        /// <returns>This is the most recent error received. The function can return one of the system error codes that are defined in Winerror.h.</returns>
        [DllImport("rstrtmgr.dll")]
        public static extern RmResult RmGetList(
            int dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            out RM_REBOOT_REASON lpdwRebootReasons);

        /// <summary>
        /// Initiates the shutdown of applications. This function can only be called from the installer that started the Restart Manager session using the RmStartSession function.
        /// </summary>
        /// <param name="pSessionHandle">A handle to an existing Restart Manager session.</param>
        /// <param name="lActionFlags">One or more RM_SHUTDOWN_TYPE options that configure the shut down of components. The following values can be combined by an OR operator to specify that unresponsive applications and services are to be forced to shut down if, and only if, all applications have been registered for restart.</param>
        /// <param name="fnStatus">A pointer to an RM_WRITE_STATUS_CALLBACK function that is used to communicate detailed status while this function is executing. If NULL, no status is provided.</param>
        /// <returns>This is the most recent error received. The function can return one of the system error codes that are defined in Winerror.h.</returns>
        [DllImport("rstrtmgr.dll")]
        public static extern RmResult RmShutdown(int pSessionHandle, RmShutdownType lActionFlags, RmWriteStatusCallback fnStatus);

        /// <summary>
        /// Restarts applications and services that have been shut down by the RmShutdown function and that have been registered to be restarted using the RegisterApplicationRestart function. This function can only be called by the primary installer that called the RmStartSession function to start the Restart Manager session.
        /// </summary>
        /// <param name="pSessionHandle">A handle to the existing Restart Manager session.</param>
        /// <param name="dwRestartFlags">Reserved. This parameter should be 0.</param>
        /// <param name="fnStatus">A pointer to a status message callback function that is used to communicate status while the RmRestart function is running. If NULL, no status is provided.</param>
        /// <returns>This is the most recent error received. The function can return one of the system error codes that are defined in Winerror.h.</returns>
        [DllImport("rstrtmgr.dll")]
        public static extern RmResult RmRestart(int pSessionHandle, int dwRestartFlags, RmWriteStatusCallback fnStatus);

        [DllImport("kernel32.dll")]
        public static extern bool GetProcessTimes(IntPtr hProcess, out FILETIME lpCreationTime, out FILETIME lpExitTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);
    }
}
