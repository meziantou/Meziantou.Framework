namespace Meziantou.Framework.Win32.AccessTokenConsoleTests;

// Examples of the readme (src/Meziantou.Framework.Win32.AccessToken/readme.md) that change the privileges of the
// current process. They are compiled in CI to keep the documentation in sync with the API, but not run.
public static class ReadmeExamples
{
    public static void ManagePrivileges()
    {
        #region AccessToken_ManagePrivileges
        using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges);

        // Enable a privilege
        token.EnablePrivilege(Privileges.SE_DEBUG_NAME);

        // Disable a privilege
        token.DisablePrivilege(Privileges.SE_DEBUG_NAME);

        // Remove a privilege
        token.RemovePrivilege(Privileges.SE_DEBUG_NAME);

        // Disable all privileges
        token.DisableAllPrivileges();
        #endregion
    }
}
