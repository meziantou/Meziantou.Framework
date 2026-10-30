---
title: Credential Manager
description: Store, read, list and delete passwords and tokens in the Windows Credential Manager, and ask the user for credentials.
---

The Windows Credential Manager stores the credentials of the current user, protected by Windows. Applications use it to
remember a password or an API token without writing it to a file. [CredentialManager](cref:Meziantou.Framework.Win32.CredentialManager) from the
`Meziantou.Framework.Win32.CredentialManager` package reads and writes it.

```bash
dotnet add package Meziantou.Framework.Win32.CredentialManager
```

The examples of this page read or change the credentials of the current user, or show a prompt: they are compiled,
not run, by the build of the repository.

## Store, read and delete a credential

<!-- ref:../../samples/Meziantou.Framework.Win32.CredentialManagerConsole/ReadmeExamples.cs;region=CredentialManager_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Save the credential to the credential manager
CredentialManager.WriteCredential(
    applicationName: "CredentialManagerTests",
    userName: "meziantou",
    secret: "Pa$$w0rd",
    comment: "Test",
    persistence: CredentialPersistence.LocalMachine);

// Get a credential from the credential manager.
// ReadCredential returns null when no such credential exists.
var cred = CredentialManager.ReadCredential(applicationName: "CredentialManagerTests");
if (cred is not null)
{
    Console.WriteLine(cred.UserName);
    Console.WriteLine(cred.Password);
}

// Delete a credential from the credential manager.
// DeleteCredential throws Win32Exception when no such credential exists.
CredentialManager.DeleteCredential(applicationName: "CredentialManagerTests");
```
<!-- endref -->

- The application name identifies the credential: it is the "Internet or network address" shown by the Credential
  Manager of the Control Panel. Prefix it with the name of your application, such as `MyApp:https://api.example.com`,
  so it does not collide with the credentials of other applications.
- [WriteCredential](cref:Meziantou.Framework.Win32.CredentialManager.WriteCredential) replaces the credential that has the same name and type.
- [ReadCredential](cref:Meziantou.Framework.Win32.CredentialManager.ReadCredential) returns `null` when the credential does not exist.
- [DeleteCredential](cref:Meziantou.Framework.Win32.CredentialManager.DeleteCredential) throws a `Win32Exception` when the credential does not exist. [TryDeleteCredential](cref:Meziantou.Framework.Win32.CredentialManager.TryDeleteCredential) returns `false`
  instead:

<!-- ref:../../samples/Meziantou.Framework.Win32.CredentialManagerConsole/CredentialManagerExamples.cs;region=CredentialManager_TryDelete;format=md-fenced-code-block;dedent=true -->
```csharp
// Returns false instead of throwing when the credential does not exist
if (!CredentialManager.TryDeleteCredential("MyApp:https://api.example.com"))
{
    Console.WriteLine("The credential does not exist");
}
```
<!-- endref -->

### Persistence

The `persistence` argument tells how long Windows keeps the credential:

| [CredentialPersistence](cref:Meziantou.Framework.Win32.CredentialPersistence) | The credential is kept |
| --- | --- |
| [Session](cref:Meziantou.Framework.Win32.CredentialPersistence.Session) | Until the user logs off |
| [LocalMachine](cref:Meziantou.Framework.Win32.CredentialPersistence.LocalMachine) | On this computer, across the logon sessions of the user |
| [Enterprise](cref:Meziantou.Framework.Win32.CredentialPersistence.Enterprise) | Across the logon sessions, and roams with the profile of the user when the domain uses roaming profiles |

### Limits

[WriteCredential](cref:Meziantou.Framework.Win32.CredentialManager.WriteCredential) throws an `ArgumentOutOfRangeException` when:

- the secret is longer than 2,560 bytes, that is 1,280 characters (it is stored as UTF-16),
- the comment is longer than 255 characters,
- the type is not [CredentialType.Generic](cref:Meziantou.Framework.Win32.CredentialType.Generic) (the default) or [CredentialType.DomainPassword](cref:Meziantou.Framework.Win32.CredentialType.DomainPassword).

## Credential types

Each overload has a variant with a [CredentialType](cref:Meziantou.Framework.Win32.CredentialType). [Generic](cref:Meziantou.Framework.Win32.CredentialType.Generic) credentials are the ones an application stores for its
own use. [DomainPassword](cref:Meziantou.Framework.Win32.CredentialType.DomainPassword) credentials are used by Windows itself to connect to the servers of the network. The
credentials of different types are distinct: reading, writing or deleting a credential uses the [Generic](cref:Meziantou.Framework.Win32.CredentialType.Generic) type unless
you pass another one.

[Credential.Password](cref:Meziantou.Framework.Win32.Credential.Password) is the secret decoded as a UTF-16 string. A credential written by another application can contain
arbitrary bytes: [Credential.Secret](cref:Meziantou.Framework.Win32.Credential.Secret) returns them exactly as Windows stores them.

## List the credentials

[EnumerateCredentials](cref:Meziantou.Framework.Win32.CredentialManager.EnumerateCredentials) returns the credentials of the user whose name matches a filter, which supports the `*`
wildcard. Without a filter, or with `"*"`, it returns them all:

<!-- ref:../../samples/Meziantou.Framework.Win32.CredentialManagerConsole/CredentialManagerExamples.cs;region=CredentialManager_Enumerate;format=md-fenced-code-block;dedent=true -->
```csharp
// The filter supports wildcards: list the credentials whose name starts with "MyApp:"
foreach (var credential in CredentialManager.EnumerateCredentials("MyApp:*"))
{
    Console.WriteLine($"{credential.ApplicationName}: {credential.UserName} ({credential.CredentialType})");
}
```
<!-- endref -->

It returns an empty list when no credential matches.

## Ask the user for credentials

[PromptForCredentials](cref:Meziantou.Framework.Win32.CredentialManager.PromptForCredentials) shows the Windows credential dialog, and returns what the user typed:

<!-- ref:../../samples/Meziantou.Framework.Win32.CredentialManagerConsole/CredentialManagerExamples.cs;region=CredentialManager_Prompt;format=md-fenced-code-block;dedent=true -->
```csharp
// Show the Windows credential dialog
var result = CredentialManager.PromptForCredentials(
    captionText: "MyApp",
    messageText: "Enter your credentials for https://api.example.com",
    userName: Environment.UserName,
    saveCredential: CredentialSaveOption.Unselected);

// null when the user cancels the dialog
if (result is not null)
{
    if (result.CredentialSaved is CredentialSaveOption.Selected)
    {
        // The user checked "Remember my credentials": storing them is up to the application
        CredentialManager.WriteCredential("MyApp:https://api.example.com", result.UserName, result.Password, CredentialPersistence.LocalMachine);
    }
}
```
<!-- endref -->

- The method returns `null` when the user cancels the dialog.
- `owner` is the handle of the parent window. `userName` and `password` fill the fields of the dialog.
- `saveCredential` shows the "Remember my credentials" checkbox unchecked ([Unselected](cref:Meziantou.Framework.Win32.CredentialSaveOption.Unselected)), checked ([Selected](cref:Meziantou.Framework.Win32.CredentialSaveOption.Selected)), or hides
  it ([Hidden](cref:Meziantou.Framework.Win32.CredentialSaveOption.Hidden)). [CredentialResult.CredentialSaved](cref:Meziantou.Framework.Win32.CredentialResult.CredentialSaved) tells whether the user checked it. The prompt does not store
  anything: write the credential yourself.
- `error: CredentialErrorCode.LogonFailure` shows the dialog with a message saying that the previous credentials were
  wrong, so the user can try again.
- [CredentialResult](cref:Meziantou.Framework.Win32.CredentialResult) gives the [UserName](cref:Meziantou.Framework.Win32.CredentialResult.UserName), the [Password](cref:Meziantou.Framework.Win32.CredentialResult.Password), and the [Domain](cref:Meziantou.Framework.Win32.CredentialResult.Domain) when the user typed a name such as
  `DOMAIN\user`.

A console application can ask in the console instead:

<!-- ref:../../samples/Meziantou.Framework.Win32.CredentialManagerConsole/CredentialManagerExamples.cs;region=CredentialManager_PromptConsole;format=md-fenced-code-block;dedent=true -->
```csharp
// Ask for the credentials in the console, without the checkbox to save them
var result = CredentialManager.PromptForCredentialsConsole(
    target: "https://api.example.com",
    saveCredential: CredentialSaveOption.Hidden);

if (result is not null)
{
    Console.WriteLine($"Hello {result.UserName}");
}
```
<!-- endref -->

## Security

- The secret is returned as a `string`: it cannot be cleared from memory once you are done with it, and it stays
  readable in a memory dump until the garbage collector reuses its memory.
- `ToString()` of [Credential](cref:Meziantou.Framework.Win32.Credential) and [CredentialResult](cref:Meziantou.Framework.Win32.CredentialResult) masks the password, so logging these objects does not leak it.
- Any process that runs as the same user can read the credentials of this user.
