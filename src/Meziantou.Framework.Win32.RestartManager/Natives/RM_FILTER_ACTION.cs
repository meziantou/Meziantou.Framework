namespace Meziantou.Framework.Win32.Natives;

/// <summary>
/// Specifies the type of modification that is applied to restart or shutdown actions.
/// </summary>
internal enum RM_FILTER_ACTION
{
    /// <summary>
    /// An invalid filter action.
    /// </summary>
    RmInvalidFilterAction = 0,

    /// <summary>
    /// Prevents the restart of the specified application or service.
    /// </summary>
    RmNoRestart = 1,

    /// <summary>
    /// Prevents the shut down and restart of the specified application or service.
    /// </summary>
    RmNoShutdown = 2,
}
