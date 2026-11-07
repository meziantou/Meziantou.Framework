---
title: LSA private data
description: Store, read and remove machine secrets in the private data of the Windows Local Security Authority, and choose who can read them.
---

The Local Security Authority (LSA) of Windows can store private data: secrets encrypted on disk, which only their
creator and the administrators can read. Services use it to keep a secret for the whole machine. [LsaPrivateData](cref:Meziantou.Framework.Win32.LsaPrivateData) from
the `Meziantou.Framework.Win32.Lsa` package reads and writes it.

<InstallNuGetPackage Id="Meziantou.Framework.Win32.Lsa" />

Before using it, consider the data protection API (DPAPI), exposed by `System.Security.Cryptography.ProtectedData`:
Microsoft recommends it over the LSA private data, unless you specifically need to manipulate LSA secrets. To store the
credentials of a user, see [Credential Manager](06-CredentialManager.md).

The examples of this page change the secrets of the machine and require administrator rights: they are compiled, not
run, by the build of the repository.

## Store, read and remove a secret

Store a value under a key:

<!-- ref:../../samples/Meziantou.Framework.Win32.Lsa.Samples/Program.cs;region=LsaPrivateData_SetValue;format=md-fenced-code-block;dedent=true -->
```csharp
// Requires administrator privileges
LsaPrivateData.SetValue("L$MySecretKey", "MySecretValue");
```
<!-- endref -->

Read it back. [GetValue](cref:Meziantou.Framework.Win32.LsaPrivateData.GetValue) returns `null` when the key does not exist:

<!-- ref:../../samples/Meziantou.Framework.Win32.Lsa.Samples/Program.cs;region=LsaPrivateData_GetValue;format=md-fenced-code-block;dedent=true -->
```csharp
var value = LsaPrivateData.GetValue("L$MySecretKey");
if (value is not null)
{
    Console.WriteLine($"Retrieved value: {value}");
}
else
{
    Console.WriteLine("Key not found");
}
```
<!-- endref -->

Remove it. Removing a key that does not exist does nothing, and [SetValue](cref:Meziantou.Framework.Win32.LsaPrivateData.SetValue) with a `null` value removes the key too:

<!-- ref:../../samples/Meziantou.Framework.Win32.Lsa.Samples/Program.cs;region=LsaPrivateData_RemoveValue;format=md-fenced-code-block;dedent=true -->
```csharp
// Requires administrator privileges
LsaPrivateData.RemoveValue("L$MySecretKey");
```
<!-- endref -->

- [SetValue](cref:Meziantou.Framework.Win32.LsaPrivateData.SetValue) and [RemoveValue](cref:Meziantou.Framework.Win32.LsaPrivateData.RemoveValue) require administrator rights. [GetValue](cref:Meziantou.Framework.Win32.LsaPrivateData.GetValue) requires the rights that the secret grants:
  its creator and the administrators.
- The methods throw a `Win32Exception` when Windows rejects the operation, for instance with error `5` (access denied)
  when the process is not elevated.
- The key cannot be `null` or empty.

## Choose the key

The prefix of the key decides the kind of secret, and who can reach it:

| Prefix | Kind | Reach |
| --- | --- | --- |
| `L$` | Local | Cannot be read remotely |
| `G$` | Global | Can be read remotely |
| `M$` | Machine | Only the operating system can read it back |
| None | Global | **Can be read remotely** |

A key without a prefix can be read remotely. Use the `L$` prefix, as in the examples, unless you need otherwise.

## Security

- The value is encrypted on disk and protected by an access control list, but LSA secrets are a well-known target for
  anyone who already has administrator or SYSTEM access to the machine. They are not absolutely protected.
- [GetValue](cref:Meziantou.Framework.Win32.LsaPrivateData.GetValue) returns a `string`: it cannot be cleared from memory once you are done with it, and it stays readable in a
  memory dump until the garbage collector reuses its memory. The library clears the native copy of the secret.
