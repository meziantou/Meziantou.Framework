# Meziantou.Framework.Win32.Lsa

.NET wrapper to get or set private data stored in Local Security Authority (LSA).

## Usage

The LSA (Local Security Authority) private data storage is a secure Windows storage area that can be used to store sensitive data like credentials, secrets, and other private information. This library provides a simple .NET API to interact with LSA private data.

**Note:** Administrator privileges are required to set or remove values in LSA private data storage.

### Store a value

```csharp
using Meziantou.Framework.Win32;

// Requires administrator privileges
LsaPrivateData.SetValue("MySecretKey", "MySecretValue");
```

### Retrieve a value

```csharp
using Meziantou.Framework.Win32;

string? value = LsaPrivateData.GetValue("MySecretKey");
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
LsaPrivateData.RemoveValue("MySecretKey");
```

## Additional Resources

- [Local Security Authority (LSA) on Microsoft Learn](https://learn.microsoft.com/en-us/windows/win32/secauthn/lsa-authentication?WT.mc_id=DT-MVP-5003978)
