---
title: Access tokens
description: Open the access token of a process, check its elevation and groups, enable privileges and work with security identifiers.
---

An access token describes the security context of a process: the user, the groups, the privileges and the integrity
level. [AccessToken](cref:Meziantou.Framework.Win32.AccessToken) from the `Meziantou.Framework.Win32.AccessToken` package opens and reads it.

```bash
dotnet add package Meziantou.Framework.Win32.AccessToken
```

## Open a token

Open the token of the current process, or of another process, with the access rights that the operations need:

<!-- ref:../../tests/Meziantou.Framework.Win32.AccessToken.Tests/AccessTokenTests.cs;region=AccessToken_Open;format=md-fenced-code-block;dedent=true -->
```csharp
// Open the current process token
using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);

// Open a token for a specific process
using var process = Process.GetCurrentProcess();
using var processToken = AccessToken.OpenProcessToken(process, TokenAccessLevels.Query);
```
<!-- endref -->

- [TokenAccessLevels.Query](cref:Meziantou.Framework.Win32.TokenAccessLevels.Query) is enough to read the token. Adjusting privileges needs [AdjustPrivileges](cref:Meziantou.Framework.Win32.TokenAccessLevels.AdjustPrivileges), and duplicating
  the token needs [Duplicate](cref:Meziantou.Framework.Win32.TokenAccessLevels.Duplicate). Ask only for the rights you use: opening the token of a process that belongs to another
  user, or that runs elevated, can fail with a `Win32Exception` (access denied).
- [AccessToken](cref:Meziantou.Framework.Win32.AccessToken) is disposable: it holds a handle to the token.

## Check whether the process is elevated

When User Account Control (UAC) is enabled, an administrator runs applications with a filtered token: the
Administrators group is disabled until the user elevates the application. The elevation type tells which token the
process has:

| [TokenElevationType](cref:Meziantou.Framework.Win32.TokenElevationType) | Meaning |
| --- | --- |
| [Default](cref:Meziantou.Framework.Win32.TokenElevationType.Default) | The token has no linked token, for instance because the user is not an administrator or UAC is disabled |
| [Full](cref:Meziantou.Framework.Win32.TokenElevationType.Full) | The process is elevated |
| [Limited](cref:Meziantou.Framework.Win32.TokenElevationType.Limited) | An administrator runs the process without elevation |

[AccessToken.IsLimitedToken()](cref:Meziantou.Framework.Win32.AccessToken.IsLimitedToken) checks the last case for the current process:

<!-- ref:../../tests/Meziantou.Framework.Win32.AccessToken.Tests/AccessTokenTests.cs;region=AccessToken_IsLimitedToken;format=md-fenced-code-block;dedent=true -->
```csharp
// true when an administrator runs the process without elevation (User Account Control)
var isLimited = AccessToken.IsLimitedToken();

using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query);
Assert.Equal(token.GetElevationType() == TokenElevationType.Limited, isLimited);
```
<!-- endref -->

[IsElevated()](cref:Meziantou.Framework.Win32.AccessToken.IsElevated) returns `true` when the token is elevated.

## Check whether the user is an administrator

A user is an administrator when the Administrators group is enabled in the token. When the token is limited, the
group is only enabled in the elevated token that is linked to it, which [GetLinkedToken()](cref:Meziantou.Framework.Win32.AccessToken.GetLinkedToken) returns:

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

- [GetLinkedToken()](cref:Meziantou.Framework.Win32.AccessToken.GetLinkedToken) returns `null` when the token has no linked token. In some logon sessions, such as the network
  logon of an SSH connection, Windows cannot provide it and the method throws a `Win32Exception`.
- The linked token is disposable too.

## Read the token

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

| Method | Returns |
| --- | --- |
| [GetTokenType()](cref:Meziantou.Framework.Win32.AccessToken.GetTokenType) | [TokenPrimary](cref:Meziantou.Framework.Win32.TokenType.TokenPrimary) for the token of a process, [TokenImpersonation](cref:Meziantou.Framework.Win32.TokenType.TokenImpersonation) for a token that a thread impersonates |
| [GetElevationType()](cref:Meziantou.Framework.Win32.AccessToken.GetElevationType), [IsElevated()](cref:Meziantou.Framework.Win32.AccessToken.IsElevated) | The elevation of the token, see [above](#check-whether-the-process-is-elevated) |
| [IsRestricted()](cref:Meziantou.Framework.Win32.AccessToken.IsRestricted) | Whether restricted security identifiers limit what the token can access |
| [GetOwner()](cref:Meziantou.Framework.Win32.AccessToken.GetOwner) | The security identifier that owns the objects the process creates |
| [GetMandatoryIntegrityLevel()](cref:Meziantou.Framework.Win32.AccessToken.GetMandatoryIntegrityLevel) | The integrity level; its [Sid](cref:Meziantou.Framework.Win32.SecurityIdentifier.Sid) is `S-1-16-8192` for the medium level, `S-1-16-12288` for the high level |
| [EnumerateGroups()](cref:Meziantou.Framework.Win32.AccessToken.EnumerateGroups) | The groups, with their [GroupSidAttributes](cref:Meziantou.Framework.Win32.GroupSidAttributes) ([SE\_GROUP\_ENABLED](cref:Meziantou.Framework.Win32.GroupSidAttributes.SE_GROUP_ENABLED), [SE\_GROUP\_USE\_FOR\_DENY\_ONLY](cref:Meziantou.Framework.Win32.GroupSidAttributes.SE_GROUP_USE_FOR_DENY_ONLY)…) |
| [EnumerateRestrictedSid()](cref:Meziantou.Framework.Win32.AccessToken.EnumerateRestrictedSid) | The restricted security identifiers |
| [EnumeratePrivileges()](cref:Meziantou.Framework.Win32.AccessToken.EnumeratePrivileges) | The privileges, with their name and [PrivilegeAttribute](cref:Meziantou.Framework.Win32.PrivilegeAttribute) ([Enabled](cref:Meziantou.Framework.Win32.PrivilegeAttribute.Enabled), [EnabledByDefault](cref:Meziantou.Framework.Win32.PrivilegeAttribute.EnabledByDefault), [Removed](cref:Meziantou.Framework.Win32.PrivilegeAttribute.Removed)…) |

## Enable or disable a privilege

A privilege allows an operation that access control does not cover, such as debugging another process or shutting
down the machine. A token can only enable the privileges it holds, and most of them are disabled by default. Open the
token with [AdjustPrivileges](cref:Meziantou.Framework.Win32.TokenAccessLevels.AdjustPrivileges):

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

- [Privileges](cref:Meziantou.Framework.Win32.Privileges) contains the names of the Windows privileges as constants: [SE\_DEBUG\_NAME](cref:Meziantou.Framework.Win32.Privileges.SE_DEBUG_NAME) (`SeDebugPrivilege`),
  [SE\_BACKUP\_NAME](cref:Meziantou.Framework.Win32.Privileges.SE_BACKUP_NAME), [SE\_SHUTDOWN\_NAME](cref:Meziantou.Framework.Win32.Privileges.SE_SHUTDOWN_NAME), and so on.
- The methods throw a `Win32Exception` when the token does not hold the privilege. [SE\_DEBUG\_NAME](cref:Meziantou.Framework.Win32.Privileges.SE_DEBUG_NAME) is only granted to
  administrators, so this example needs an elevated process.
- [RemovePrivilege](cref:Meziantou.Framework.Win32.AccessToken.RemovePrivilege) removes the privilege from the token: it cannot be enabled again in this process.
- The changes apply to the token, so to the whole process, not only to the current thread.

## Work with security identifiers

[SecurityIdentifier](cref:Meziantou.Framework.Win32.SecurityIdentifier) represents a user or a group. Create the identifiers of well-known accounts with
[FromWellKnown](cref:Meziantou.Framework.Win32.SecurityIdentifier.FromWellKnown), and compare them with `==`:

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

- [Sid](cref:Meziantou.Framework.Win32.SecurityIdentifier.Sid) is the string form of the identifier, such as `S-1-5-32-544`. Compare identifiers with it, or with `==`, never
  with their name.
- [Name](cref:Meziantou.Framework.Win32.SecurityIdentifier.Name), [Domain](cref:Meziantou.Framework.Win32.SecurityIdentifier.Domain) and [FullName](cref:Meziantou.Framework.Win32.SecurityIdentifier.FullName) (`Domain\Name`) are looked up the first time they are read. The names are localized
  (the Administrators group has another name on a French Windows), and the lookup may query a domain controller. [Name](cref:Meziantou.Framework.Win32.SecurityIdentifier.Name)
  and [Domain](cref:Meziantou.Framework.Win32.SecurityIdentifier.Domain) are `null` when the account cannot be resolved.
- `ToString()` returns the full name when it is available, and the [Sid](cref:Meziantou.Framework.Win32.SecurityIdentifier.Sid) otherwise.

## Duplicate a token

[Duplicate](cref:Meziantou.Framework.Win32.AccessToken.Duplicate) creates an impersonation token from the token, at the impersonation level you choose:

<!-- ref:../../tests/Meziantou.Framework.Win32.AccessToken.Tests/AccessTokenTests.cs;region=AccessToken_Duplicate;format=md-fenced-code-block;dedent=true -->
```csharp
// Duplicating a token requires the Duplicate access right
using var token = AccessToken.OpenCurrentProcessToken(TokenAccessLevels.Query | TokenAccessLevels.Duplicate);

using var impersonationToken = token.Duplicate(SecurityImpersonationLevel.SecurityImpersonation);
Assert.Equal(TokenType.TokenImpersonation, impersonationToken.GetTokenType());
```
<!-- endref -->
