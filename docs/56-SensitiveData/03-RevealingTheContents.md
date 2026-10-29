---
title: Revealing the contents
description: Get the secret as a string, an array or a span, use it without keeping a copy, clone it and dispose it.
---

The contents of a `SensitiveData<T>` are protected while they are not in use. Each `Reveal` method lifts the
protection, copies or hands out the contents, and applies the protection again before it returns.

## Choose a reveal method

<!-- ref:../../tests/Meziantou.Framework.SensitiveData.Tests/SensitiveDataTests.cs;region=SensitiveDataOfT_Reveal;format=md-fenced-code-block;dedent=true -->
```csharp
// Create and use sensitive data
using var secret = SensitiveData.Create("my-secret-password");

// Get the length without revealing the data
int length = secret.GetLength();
Assert.Equal(18, length);

// Reveal data into an existing buffer
char[] buffer = new char[length];
secret.RevealInto(buffer);
Assert.Equal("my-secret-password", new string(buffer));

// Or reveal to a new array
char[] revealed = secret.RevealToArray();
Assert.Equal("my-secret-password", new string(revealed));

// Use the data with a callback to avoid keeping it in memory
secret.RevealAndUse(arg: length, static (span, expectedLength) =>
{
    // Process the sensitive data here
    Assert.HasCount(expectedLength, span);
});
```
<!-- endref -->

| Method | Gives | The copy |
| --- | --- | --- |
| `GetLength()` | The number of elements | None: the contents are not revealed |
| `RevealInto(destination)` | The contents, copied to a buffer you provide | Lives in your buffer: you can clear it |
| `RevealToArray()` | A new array | Lives until the garbage collector reclaims it, unless you clear it |
| `RevealToString()` | A new string, for a `SensitiveData<char>` | Lives until the garbage collector reclaims it, and cannot be cleared |
| `RevealAndUse(arg, callback)` | A `ReadOnlySpan<T>` over the contents, for the duration of the callback | None |

Prefer the methods at the end of the list that make no copy, or a copy you control:

- `RevealAndUse` passes the contents to the callback without copying them. Pass the state the callback needs in `arg`,
  so the callback can be a `static` lambda that captures nothing. Do not keep the span after the callback returns: the
  contents are protected again.
- `RevealInto` copies the contents to a buffer, such as a `stackalloc` span or an array rented from a pool, which you
  clear when you are done (`CryptographicOperations.ZeroMemory`). The buffer must hold at least `GetLength()`
  elements, or the method throws an `ArgumentException`. It returns the number of elements written.
- `RevealToString` and `RevealToArray` are convenient for APIs that only take a `string` or an array, such as a
  connection string builder or an HTTP header. The copy then lives as long as the API keeps it.

An exception thrown by the `RevealAndUse` callback still lets the contents be protected again, and the instance stays
usable.

## Clone and dispose

<!-- ref:../../tests/Meziantou.Framework.SensitiveData.Tests/SensitiveDataTests.cs;region=SensitiveData_CloneAndDispose;format=md-fenced-code-block;dedent=true -->
```csharp
var secret = SensitiveData.Create("my-password");

// A clone holds its own copy of the contents
using var copy = secret.Clone();

// Disposing clears the contents; the instance can no longer be revealed
secret.Dispose();
Assert.Throws<ObjectDisposedException>(() => secret.RevealToString());
Assert.Equal("my-password", copy.RevealToString());
```
<!-- endref -->

- `Clone` creates an independent `SensitiveData<T>` with its own copy of the contents: disposing one does not affect the
  other.
- `Dispose` clears the memory that holds the contents and releases it. Every method then throws an
  `ObjectDisposedException`. Calling `Dispose` more than once is allowed.
- An instance that is never disposed is cleared when the garbage collector finalizes it, at a time you do not control.
  Dispose instances as soon as you no longer need them.

## Use an instance from several threads

The methods of `SensitiveData<T>` can be called from several threads at the same time: reveals are serialized, so the
contents are never protected again while another thread is still reading them. `Dispose` waits for a reveal running on
another thread to finish before it clears the contents.

A `RevealAndUse` callback can call another method of the same instance, such as `RevealInto` or `Clone`: the contents
stay readable until the outermost reveal ends.
