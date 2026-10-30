---
title: Getting started
description: Install the package, store a secret, reveal it when you need it and dispose it.
---

## Install the package

Add the `Meziantou.Framework.SensitiveData` package to a project:

```bash
dotnet add package Meziantou.Framework.SensitiveData
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework;`.

## Store and reveal a secret

`SensitiveData.Create` copies a value into a new `SensitiveData<T>`. The contents stay protected until a `Reveal`
method is called:

<!-- ref:../../tests/Meziantou.Framework.SensitiveData.Tests/SensitiveDataTests.cs;region=SensitiveData_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Create sensitive data from a string
using var secret = SensitiveData.Create("secret");

// Reveal the data
string str = secret.RevealToString();
char[] chars = secret.RevealToArray();
Assert.Equal("secret", str);
Assert.Equal("secret".ToCharArray(), chars);

var buffer = new char[secret.GetLength()];
secret.RevealInto(buffer);

// Or use it without keeping a copy of your own
var lengths = new List<int>();
secret.RevealAndUse(arg: lengths, static (span, list) => list.Add(span.Length));
Assert.Equal([6], lengths);

// Create sensitive data from a buffer of any unmanaged type
using var key = SensitiveData.Create(new byte[] { 1, 2, 3, 4, 5 });
byte[] revealedKey = key.RevealToArray();
Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, revealedKey);
```
<!-- endref -->

- `SensitiveData.Create("secret")` creates a `SensitiveData<char>`. `Create` also accepts an array or a
  `ReadOnlySpan<T>` of any unmanaged type, such as the bytes of a key.
- `RevealToString`, `RevealToArray` and `RevealInto` copy the contents out. `RevealAndUse` gives the contents to a
  callback without making a copy. See [Revealing the contents](03-RevealingTheContents.md).
- `SensitiveData<T>` is `IDisposable`: declare it with `using`, so the memory that holds the secret is cleared and
  released as soon as you are done with it.

## Keep secrets out of logs

Once the secret is in a `SensitiveData<T>`, the usual ways a value escapes do not show it:

<!-- ref:../../tests/Meziantou.Framework.SensitiveData.Tests/SensitiveDataTests.cs;region=SensitiveData_NoAccidentalDisclosure;format=md-fenced-code-block;dedent=true -->
```csharp
using var secret = SensitiveData.Create("my-password");

// Formatting the instance gives its type name, not its contents
var message = $"Connecting with {secret}";
Assert.DoesNotContain("my-password", message, StringComparison.Ordinal);

// Serializing it gives an empty object
var json = JsonSerializer.Serialize(secret);
Assert.Equal("{}", json);
```
<!-- endref -->

The same goes for a logger that formats the value, or an object that holds a `SensitiveData<T>` property and is
serialized as a whole. To show or send the secret, reveal it explicitly.

## The value you pass stays in memory

`Create` copies the value: the string or the array you pass is not changed and stays in memory until the garbage
collector reclaims it. Create the `SensitiveData<T>` as close as possible to where the secret enters the application,
and clear the arrays you own once the copy is made, for instance with `CryptographicOperations.ZeroMemory`. A string
cannot be cleared.

## Next steps

- [Revealing the contents](03-RevealingTheContents.md): the ways to use the secret, and how to limit the copies.
- [Configuration](04-Configuration.md): bind secrets from `appsettings.json`, environment variables or a vault.
