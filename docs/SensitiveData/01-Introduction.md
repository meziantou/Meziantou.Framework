---
title: Introduction
description: A container for passwords, keys and other secrets that does not leak them into logs, serialized data or memory by accident.
---

`Meziantou.Framework.SensitiveData` provides the `SensitiveData<T>` type: a container for a secret, such as a password,
an API key or a cryptographic key. The secret is stored outside the managed heap, protected while it is not in use, and
only comes out when your code asks for it explicitly.

A secret held in a `string` or a `byte[]` leaks easily: a log message that formats an options object, an exception
that includes a property value, a JSON response that serializes one property too many, or a memory dump of the process.
`SensitiveData<T>` makes these accidents hard: formatting it gives its type name, serializing it gives an empty
object, and its memory is protected by the operating system while the contents are not revealed.

It does not try to stop *intentional* disclosure: code that runs in the process can reveal the contents, and so can a
debugger or a memory dump taken while the contents are revealed.

## What you can do with it

- **Store a secret** from a string, an array or a span of any unmanaged type (`char`, `byte`, ...). See
  [Getting started](02-GettingStarted.md).
- **Use the secret** only where you need it: copy it to a buffer, get a string or an array, or work on it in a callback
  without keeping a copy. See [Revealing the contents](03-RevealingTheContents.md).
- **Read secrets from the configuration**: a configuration value binds to a `SensitiveData<char>` property. See
  [Configuration](04-Configuration.md).
- **Know what is protected**: the protection depends on the platform. See [Protection](05-Protection.md).

## Namespace

The types are in the [Meziantou.Framework](cref:Meziantou.Framework.SensitiveData!N:Meziantou.Framework) namespace:

| Type | Contains |
| --- | --- |
| `SensitiveData` | The `Create` factory methods and the `RevealToString` extension method |
| `SensitiveData<T>` | The secret, its reveal methods and `Dispose` |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and store your first secret.
- [Protection](05-Protection.md): what the type protects against, and what it does not.
