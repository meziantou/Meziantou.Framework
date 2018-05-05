namespace Meziantou.Framework.Win32.Natives
{
    /// <summary>
    /// Specifies the type of application that is described by the RM_PROCESS_INFO structure.
    /// </summary>
    internal enum RM_APP_TYPE
    {
        /// <summary>
        /// The application cannot be classified as any other type.
        /// An application of this type can only be shut down by a forced shutdown.
        /// </summary>
        RmUnknownApp = 0,
        /// <summary>
        /// A Windows application run as a stand-alone process that displays a top-level window.
        /// </summary>
        RmMainWindow = 1,
        /// <summary>
        /// A Windows application that does not run as a stand-alone process and does not display a top-level window.
        /// </summary>
        RmOtherWindow = 2,
        /// <summary>
        /// The application is a Windows service.
        /// </summary>
        RmService = 3,
        /// <summary>
        /// The application is Windows Explorer.
        /// </summary>
        RmExplorer = 4,
        /// <summary>
        /// The application is a stand-alone console application.
        /// </summary>
        RmConsole = 5,
        /// <summary>
        /// A system restart is required to complete the installation because a process cannot be shut down.
        /// The process cannot be shut down because of the following reasons.
        /// The process may be a critical process.
        /// The current user may not have permission to shut down the process.
        /// The process may belong to the primary installer that started the Restart Manager. 
        /// </summary>
        RmCritical = 1000,
    }
}
