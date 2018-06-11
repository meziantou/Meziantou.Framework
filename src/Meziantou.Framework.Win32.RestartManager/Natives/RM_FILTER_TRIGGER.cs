namespace Meziantou.Framework.Win32.Natives
{
    /// <summary>
    /// Describes the restart or shutdown actions for an application or service.
    /// </summary>
    internal enum RM_FILTER_TRIGGER
    {
        /// <summary>
        /// An invalid filter trigger.
        /// </summary>
        RmFilterTriggerInvalid = 0,

        /// <summary>
        /// Modifies the shutdown or restart actions for an application identified by its executable filename.
        /// </summary>
        RmFilterTriggerFile = 1,

        /// <summary>
        /// Modifies the shutdown or restart actions for an application identified by a RM_UNIQUE_PROCESS structure.
        /// </summary>
        RmFilterTriggerProcess = 2,

        /// <summary>
        /// Modifies the shutdown or restart actions for a service identified by a service short name.
        /// </summary>
        RmFilterTriggerService = 3
    }
}
