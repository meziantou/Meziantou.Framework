namespace Meziantou.Framework.Win32
{
    /// <summary>
    /// Configures the shut down of applications.
    /// </summary>
    public enum RmShutdownType
    {
        /// <summary>
        /// Forces unresponsive applications and services to shut down after the timeout period. An application that does not respond to a shutdown request by the Restart Manager is forced to shut down after 30 seconds. A service that does not respond to a shutdown request is forced to shut down after 20 seconds.
        /// </summary>
        RmForceShutdown = 0x1,

        /// <summary>
        /// Shuts down applications if and only if all the applications have been registered for restart using the RegisterApplicationRestart function. If any processes or services cannot be restarted, then no processes or services are shut down.
        /// </summary>
        RmShutdownOnlyRegistered = 0x10
    }
}
