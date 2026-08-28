# Meziantou.Framework.Win32.Lsa

.NET wrapper to get or set private data stored in Local Security Authority (LSA).

## Usage

The LSA (Local Security Authority) private data storage is a Windows storage area for sensitive data like credentials, secrets, and other private information. Values are encrypted before being stored, under a DACL that allows only the creator and administrators to read them. This library provides a simple .NET API to interact with LSA private data.

Before using it, read [Security considerations](#security-considerations) below.

**Note:** Administrator privileges are required to set or remove values in LSA private data storage.

### Store a value

```csharp
using Meziantou.Framework.Win32;

// Requires administrator privileges
LsaPrivateData.SetValue("L$MySecretKey", "MySecretValue");
```

### Retrieve a value

```csharp
using Meziantou.Framework.Win32;

string? value = LsaPrivateData.GetValue("L$MySecretKey");
if (value != null)
{
    Console.WriteLine($"Retrieved value: {value}");
}
else
{
    Console.WriteLine("Key not found");
}
```

### Remove a value

```csharp
using Meziantou.Framework.Win32;

// Requires administrator privileges
LsaPrivateData.RemoveValue("L$MySecretKey");
```

## Security considerations

- **Consider DPAPI first.** Microsoft's own documentation for `LsaStorePrivateData` recommends [`CryptProtectData`](https://learn.microsoft.com/en-us/windows/desktop/api/dpapi/nf-dpapi-cryptprotectdata) and `CryptUnprotectData` instead, and says to use the LSA private data functions only when you need to manipulate LSA secrets specifically.
- **The key name decides who can read the secret.** A prefix on the key name selects the object type:

  | Prefix | Object type | Reach |
  | --- | --- | --- |
  | `L$` | local | Cannot be accessed remotely |
  | `G$` | global | Can be accessed remotely |
  | `M$` | machine | Can be read back only by the operating system |
  | *(none)* | global | **Can be accessed remotely** |

  A key with no prefix is remotely accessible. Prefer `L$` unless you specifically need otherwise — that is why the examples above use it.
- **The data is not absolutely protected.** Microsoft states this directly: the value is encrypted at rest and the key is protected by a DACL, but LSA secrets are a well-known credential-theft target for anyone who already has administrator or SYSTEM access to the machine.
- **The retrieved value is a `string`.** It cannot be cleared once you are done with it, and it stays readable in a process dump for as long as the garbage collector keeps it alive.

## Additional Resources

- [Local Security Authority (LSA) on Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/secauthn/lsa-authentication?WT.mc_id=DT-MVP-5003978)
