namespace Meziantou.Framework.Win32;

/// <summary>Provides constants for Windows privilege names that can be enabled or disabled on access tokens.</summary>
/// <example>
/// <code>
/// using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.AdjustPrivileges | TokenAccessLevels.Query);
/// 
/// // Enable debug privilege to access other processes
/// token.EnablePrivilege(Privileges.SE_DEBUG_NAME);
/// 
/// // Enable backup privilege to bypass file security checks
/// token.EnablePrivilege(Privileges.SE_BACKUP_NAME);
/// 
/// // Disable a privilege
/// token.DisablePrivilege(Privileges.SE_SHUTDOWN_NAME);
/// </code>
/// </example>
/// <remarks>
/// For more information about privileges, see <see href="https://docs.microsoft.com/en-us/windows/win32/secauthz/privilege-constants"/>.
/// </remarks>
public static class Privileges
{
    /// <summary>Required to assign the primary token of a process.</summary>
    public const string SE_ASSIGNPRIMARYTOKEN_NAME = "SeAssignPrimaryTokenPrivilege";

    /// <summary>Required to generate audit-log entries.</summary>
    public const string SE_AUDIT_NAME = "SeAuditPrivilege";

    /// <summary>Required to perform backup operations.</summary>
    public const string SE_BACKUP_NAME = "SeBackupPrivilege";

    /// <summary>Required to receive notifications of changes to files or directories.</summary>
    public const string SE_CHANGE_NOTIFY_NAME = "SeChangeNotifyPrivilege";

    /// <summary>Required to create named file mapping objects in the global namespace.</summary>
    public const string SE_CREATE_GLOBAL_NAME = "SeCreateGlobalPrivilege";

    /// <summary>Required to create a paging file.</summary>
    public const string SE_CREATE_PAGEFILE_NAME = "SeCreatePagefilePrivilege";

    /// <summary>Required to create a permanent object.</summary>
    public const string SE_CREATE_PERMANENT_NAME = "SeCreatePermanentPrivilege";

    /// <summary>Required to create a symbolic link.</summary>
    public const string SE_CREATE_SYMBOLIC_LINK_NAME = "SeCreateSymbolicLinkPrivilege";

    /// <summary>Required to create a primary token.</summary>
    public const string SE_CREATE_TOKEN_NAME = "SeCreateTokenPrivilege";

    /// <summary>Required to debug and adjust the memory of a process owned by another account.</summary>
    public const string SE_DEBUG_NAME = "SeDebugPrivilege";

    /// <summary>Required to mark user and computer accounts as trusted for delegation.</summary>
    public const string SE_ENABLE_DELEGATION_NAME = "SeEnableDelegationPrivilege";

    /// <summary>Required to impersonate a client after authentication.</summary>
    public const string SE_IMPERSONATE_NAME = "SeImpersonatePrivilege";

    /// <summary>Required to increase the base priority of a process.</summary>
    public const string SE_INC_BASE_PRIORITY_NAME = "SeIncreaseBasePriorityPrivilege";

    /// <summary>Required to increase the quota assigned to a process.</summary>
    public const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";

    /// <summary>Required to allocate more memory for applications that run in the context of users.</summary>
    public const string SE_INC_WORKING_SET_NAME = "SeIncreaseWorkingSetPrivilege";

    /// <summary>Required to load or unload a device driver.</summary>
    public const string SE_LOAD_DRIVER_NAME = "SeLoadDriverPrivilege";

    /// <summary>Required to lock physical pages in memory.</summary>
    public const string SE_LOCK_MEMORY_NAME = "SeLockMemoryPrivilege";

    /// <summary>Required to create a computer account.</summary>
    public const string SE_MACHINE_ACCOUNT_NAME = "SeMachineAccountPrivilege";

    /// <summary>Required to enable volume management privileges.</summary>
  public const string SE_MANAGE_VOLUME_NAME = "SeManageVolumePrivilege";

    /// <summary>Required to gather profiling information for a single process.</summary>
    public const string SE_PROF_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";

    /// <summary>Required to modify the mandatory integrity level of an object.</summary>
    public const string SE_RELABEL_NAME = "SeRelabelPrivilege";

    /// <summary>Required to shut down a system using a network request.</summary>
    public const string SE_REMOTE_SHUTDOWN_NAME = "SeRemoteShutdownPrivilege";

    /// <summary>Required to perform restore operations.</summary>
    public const string SE_RESTORE_NAME = "SeRestorePrivilege";

    /// <summary>Required to perform a number of security-related functions.</summary>
    public const string SE_SECURITY_NAME = "SeSecurityPrivilege";

    /// <summary>Required to shut down a local system.</summary>
    public const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

    /// <summary>Required for a domain controller to use the LDAP directory synchronization services.</summary>
    public const string SE_SYNC_AGENT_NAME = "SeSyncAgentPrivilege";

    /// <summary>Required to modify firmware environment variables.</summary>
    public const string SE_SYSTEM_ENVIRONMENT_NAME = "SeSystemEnvironmentPrivilege";

    /// <summary>Required to gather profiling information for the entire system.</summary>
    public const string SE_SYSTEM_PROFILE_NAME = "SeSystemProfilePrivilege";

    /// <summary>Required to modify the system time.</summary>
    public const string SE_SYSTEMTIME_NAME = "SeSystemtimePrivilege";

    /// <summary>Required to take ownership of an object without being granted discretionary access.</summary>
    public const string SE_TAKE_OWNERSHIP_NAME = "SeTakeOwnershipPrivilege";

    /// <summary>Required to act as part of the operating system.</summary>
    public const string SE_TCB_NAME = "SeTcbPrivilege";

    /// <summary>Required to adjust the time zone associated with the computer's internal clock.</summary>
  public const string SE_TIME_ZONE_NAME = "SeTimeZonePrivilege";

    /// <summary>Required to access Credential Manager as a trusted caller.</summary>
    public const string SE_TRUSTED_CREDMAN_ACCESS_NAME = "SeTrustedCredManAccessPrivilege";

    /// <summary>Required to remove a computer from a docking station.</summary>
    public const string SE_UNDOCK_NAME = "SeUndockPrivilege";

    /// <summary>Required to read unsolicited input from a terminal device.</summary>
    public const string SE_UNSOLICITED_INPUT_NAME = "SeUnsolicitedInputPrivilege";
}
