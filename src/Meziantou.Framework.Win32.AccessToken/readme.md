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

### Opening an Access Token

```csharp
using Meziantou.Framework.Win32;

// Open the current process token
using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

// Open a token for a specific process
using var process = Process.GetCurrentProcess();
using var processToken = AccessToken.OpenProcessToken(process, TokenAccessLevels.Query);
```

### Querying Token Information

```csharp
using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

// Get token type (Primary or Impersonation)
var tokenType = token.GetTokenType();

// Check if token is elevated
bool isElevated = token.IsElevated();

// Get elevation type (Unknown, Default, Full, or Limited)
var elevationType = token.GetElevationType();

// Check if token is restricted
bool isRestricted = token.IsRestricted();

// Get token owner
var owner = token.GetOwner();
Console.WriteLine($"Owner: {owner.FullName} ({owner.Sid})");

// Get mandatory integrity level
var integrityLevel = token.GetMandatoryIntegrityLevel();
Console.WriteLine($"Integrity Level: {integrityLevel?.Sid}");

// Enumerate all groups
foreach (var group in token.EnumerateGroups())
{
    Console.WriteLine($"Group: {group.Sid.FullName}");
    Console.WriteLine($"  SID: {group.Sid.Sid}");
    Console.WriteLine($"  Attributes: {group.Attributes}");
}

// Enumerate restricted SIDs
foreach (var group in token.EnumerateRestrictedSid())
{
    Console.WriteLine($"Restricted SID: {group.Sid.FullName}");
}

// Enumerate all privileges
foreach (var privilege in token.EnumeratePrivileges())
{
    Console.WriteLine($"Privilege: {privilege.Name}");
    Console.WriteLine($"  Attributes: {privilege.Attributes}");
}
```

### Managing Privileges

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

### Checking for Administrator Privileges

```csharp
bool IsAdministrator()
{
    using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

    // Check if current token has admin rights
    if (!IsAdministrator(token) && token.GetElevationType() == TokenElevationType.Limited)
    {
        // If limited, check the linked token (elevated token)
        using var linkedToken = token.GetLinkedToken();
        return IsAdministrator(linkedToken);
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

### Working with Security Identifiers

```csharp
// Get SID from well-known type
var adminSid = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinBuiltinAdministratorsSid);
Console.WriteLine($"Admin SID: {adminSid.Sid}");
Console.WriteLine($"Admin Name: {adminSid.FullName}");

// Get well-known integrity level SIDs
var lowIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinLowLabelSid);
var mediumIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinMediumLabelSid);
var highIntegrity = SecurityIdentifier.FromWellKnown(WellKnownSidType.WinHighLabelSid);
```

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
