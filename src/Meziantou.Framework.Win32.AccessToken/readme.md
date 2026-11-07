# Meziantou.Framework.Win32.AccessToken

`Meziantou.Framework.Win32.AccessToken` is a .NET library that provides a managed wrapper for manipulating Windows Access Tokens. It allows you to query and modify security tokens, check privileges, enumerate groups, and manage token elevation.

## Features

- **Query Token Information**: Get token type, elevation type, owner, groups, privileges, and integrity level
- **Check Elevation**: Determine if a token is elevated or restricted
- **Manage Privileges**: Enable, disable, or remove privileges
- **Enumerate Groups and Privileges**: List all groups and privileges associated with a token
- **Duplicate Tokens**: Create duplicate tokens with different impersonation levels
- **Security Identifiers**: Work with Windows SIDs and well-known SID types

## Usage

The types are in the `Meziantou.Framework.Win32` namespace, and `Process` is in `System.Diagnostics`.

### Opening an Access Token

<!-- ref:../../tests/Meziantou.Framework.Win32.AccessToken.Tests/AccessTokenTests.cs;region=AccessToken_Open;format=md-fenced-code-block;dedent=true -->
```csharp
// Open the current process token
using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

// Open a token for a specific process
using var process = Process.GetCurrentProcess();
using var processToken = AccessToken.OpenProcessToken(process, TokenAccessLevels.Query);
```
<!-- endref -->

### Querying Token Information

<!-- ref:../../tests/Meziantou.Framework.Win32.AccessToken.Tests/AccessTokenTests.cs;region=AccessToken_Query;format=md-fenced-code-block;dedent=true -->
```csharp
using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

// Get token type (Primary or Impersonation)
var tokenType = token.GetTokenType();
Assert.Equal(TokenType.TokenPrimary, tokenType);

// Check if token is elevated
var isElevated = token.IsElevated();

// Get elevation type (Unknown, Default, Full, or Limited)
var elevationType = token.GetElevationType();
Assert.NotEqual(TokenElevationType.Unknown, elevationType);
if (elevationType == TokenElevationType.Limited)
{
    // The filtered token of an administrator when UAC is enabled
    Assert.False(isElevated);
}

// Check if token is restricted
var isRestricted = token.IsRestricted();
Assert.False(isRestricted);

// Get token owner
var owner = token.GetOwner();
Assert.StartsWith("S-1-", owner.Sid);

// Get mandatory integrity level
var integrityLevel = token.GetMandatoryIntegrityLevel();
Assert.StartsWith("S-1-16-", integrityLevel.Sid.Sid);

// Enumerate all groups
var everyone = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinWorldSid);
Assert.Contains(token.EnumerateGroups(), group => group.Sid == everyone);

// Enumerate restricted SIDs
Assert.Empty(token.EnumerateRestrictedSid());

// Enumerate all privileges
Assert.Contains(token.EnumeratePrivileges(), privilege => privilege.Name == Privileges.SE_CHANGE_NOTIFY_NAME);
```
<!-- endref -->

### Managing Privileges

A privilege can only be enabled when the token holds it: `SE_DEBUG_NAME` is granted to administrators.

<!-- ref:../../samples/Meziantou.Framework.Win32.AccessTokenConsole/ReadmeExamples.cs;region=AccessToken_ManagePrivileges;format=md-fenced-code-block;dedent=true -->
```csharp
using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query | TokenAccessLevels.AdjustPrivileges);

// Enable a privilege
token.EnablePrivilege(Privileges.SE_DEBUG_NAME);

// Disable a privilege
token.DisablePrivilege(Privileges.SE_DEBUG_NAME);

// Remove a privilege
token.RemovePrivilege(Privileges.SE_DEBUG_NAME);

// Disable all privileges
token.DisableAllPrivileges();
```
<!-- endref -->

### Checking for Administrator Privileges

<!-- ref:../../tests/Meziantou.Framework.Win32.AccessToken.Tests/AccessTokenTests.cs;region=AccessToken_IsAdministrator;format=md-fenced-code-block;dedent=true -->
```csharp
bool IsAdministrator()
{
    using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

    // Check if current token has admin rights
    if (IsAdministrator(token))
        return true;

    if (token.GetElevationType() == TokenElevationType.Limited)
    {
        // If limited, check the linked token (elevated token)
        using var linkedToken = token.GetLinkedToken();
        return linkedToken is not null && IsAdministrator(linkedToken);
    }

    return false;

    static bool IsAdministrator(AccessToken accessToken)
    {
        var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
        foreach (var group in accessToken.EnumerateGroups())
        {
            if (group.Attributes.HasFlag(GroupSidAttributes.SE_GROUP_ENABLED) &&
                group.Sid == adminSid)
            {
                return true;
            }
        }

        return false;
    }
}
```
<!-- endref -->

### Working with Security Identifiers

<!-- ref:../../tests/Meziantou.Framework.Win32.AccessToken.Tests/AccessTokenTests.cs;region=AccessToken_SecurityIdentifiers;format=md-fenced-code-block;dedent=true -->
```csharp
// Get SID from well-known type
var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
Assert.Equal("S-1-5-32-544", adminSid.Sid);
Assert.Equal($@"{adminSid.Domain}\{adminSid.Name}", adminSid.FullName); // BUILTIN\Administrators on an English Windows

// Get well-known integrity level SIDs
var lowIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLowLabelSid);
var mediumIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinMediumLabelSid);
var highIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinHighLabelSid);
Assert.Equal("S-1-16-4096", lowIntegrity.Sid);
Assert.Equal("S-1-16-8192", mediumIntegrity.Sid);
Assert.Equal("S-1-16-12288", highIntegrity.Sid);
```
<!-- endref -->

## API Reference

### AccessToken Class

**Methods:**
- `OpenCurrentProcessToken(TokenAccessLevels)` - Opens the access token of the current process
- `OpenProcessToken(Process, TokenAccessLevels)` - Opens the access token of a specific process
- `IsLimitedToken()` - Checks if the current process token is limited
- `GetTokenType()` - Returns the token type (Primary or Impersonation)
- `GetElevationType()` - Returns the elevation type
- `IsElevated()` - Checks if the token is elevated
- `IsRestricted()` - Checks if the token is restricted
- `GetOwner()` - Gets the owner SID
- `GetMandatoryIntegrityLevel()` - Gets the mandatory integrity level
- `GetLinkedToken()` - Gets the linked token (elevated/limited counterpart)
- `EnumerateGroups()` - Enumerates all groups
- `EnumerateRestrictedSid()` - Enumerates restricted SIDs
- `EnumeratePrivileges()` - Enumerates all privileges
- `EnablePrivilege(string)` - Enables a privilege
- `DisablePrivilege(string)` - Disables a privilege
- `RemovePrivilege(string)` - Removes a privilege
- `DisableAllPrivileges()` - Disables all privileges
- `Duplicate(SecurityImpersonationLevel)` - Duplicates the token

### Privileges Class

Contains constants for all Windows privilege names:
- `SE_DEBUG_NAME` - Debug programs
- `SE_BACKUP_NAME` - Back up files and directories
- `SE_RESTORE_NAME` - Restore files and directories
- `SE_SHUTDOWN_NAME` - Shut down the system
- And many more...

## Additional Resources

- [Access Tokens (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/secauthz/access-tokens?WT.mc_id=DT-MVP-5003978)
- [Privileges (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/secauthz/privileges?WT.mc_id=DT-MVP-5003978)
- [Security Identifiers (Microsoft Docs)](https://learn.microsoft.com/en-us/windows/win32/secauthz/security-identifiers?WT.mc_id=DT-MVP-5003978)
