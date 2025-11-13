namespace Meziantou.Framework.Win32.Natives;

internal enum RmResult
{
    /// <summary>The resources specified have been registered.</summary>
    ERROR_SUCCESS = 0,

    /// <summary>
    /// A Restart Manager function could not obtain a Registry write mutex in the allotted time.
    /// A system restart is recommended because further use of the Restart Manager is likely to fail.
    /// </summary>
    ERROR_SEM_TIMEOUT = 121,

    /// <summary>
    /// One or more arguments are not correct.
    /// This error value is returned by Restart Manager function if a NULL pointer or 0 is passed in a parameter that requires a non-null and non-zero value.
    /// </summary>
    ERROR_BAD_ARGUMENTS = 160,

    /// <summary>An operation was unable to read or write to the registry.</summary>
    ERROR_WRITE_FAULT = 29,

    /// <summary>A Restart Manager operation could not complete because not enough memory was available.</summary>
    ERROR_OUTOFMEMORY = 14,

    /// <summary>
    /// An invalid handle was passed to the function.
    /// No Restart Manager session exists for the handle supplied.
    /// </summary>
    ERROR_INVALID_HANDLE = 6,

    /// <summary>The maximum number of sessions has been reached.</summary>
    ERROR_MAX_SESSIONS_REACHED = 353,

    /// <summary>This error value is returned by the RmGetList function if the rgAffectedApps buffer is too small to hold all application information in the list.</summary>
    ERROR_MORE_DATA = 234,

    /// <summary>The current operation is canceled by user.</summary>
    ERROR_CANCELLED = 1223,
}
