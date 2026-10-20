namespace Meziantou.Framework.Win32.AccessTokenConsoleTests;

public static class AccessTokenExamples
{
    public static void InspectCurrentProcessToken()
    {
        #region AccessToken_InspectCurrentProcessToken
        // Open the current process token. Adjusting privileges requires the AdjustPrivileges access right.
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges);

        // Check if the process is elevated
        bool isElevated = token.IsElevated();
        Console.WriteLine($"Elevated: {isElevated}");

        // Get token information
        Console.WriteLine($"Token Type: {token.GetTokenType()}");
        Console.WriteLine($"Elevation Type: {token.GetElevationType()}");
        Console.WriteLine($"Owner: {token.GetOwner()}");

        // Enumerate groups
        foreach (var group in token.EnumerateGroups())
        {
            Console.WriteLine($"Group: {group.Sid} ({group.Attributes})");
        }

        // Enumerate privileges
        foreach (var privilege in token.EnumeratePrivileges())
        {
            Console.WriteLine($"Privilege: {privilege.Name} ({privilege.Attributes})");
        }

        // Enable a privilege. The token must hold it: SeDebugPrivilege is only granted to administrators.
        token.EnablePrivilege(Privileges.SE_DEBUG_NAME);
        #endregion
    }

    public static void AdjustPrivileges()
    {
        #region Privileges_EnableAndDisable
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.AdjustPrivileges | TokenAccessLevels.Query);

        // Enable debug privilege to access other processes
        token.EnablePrivilege(Privileges.SE_DEBUG_NAME);

        // Enable backup privilege to bypass file security checks
        token.EnablePrivilege(Privileges.SE_BACKUP_NAME);

        // Disable a privilege
        token.DisablePrivilege(Privileges.SE_SHUTDOWN_NAME);
        #endregion
    }
}
