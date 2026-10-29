---
title: Getting started
description: Install the package, hash a password, compute a SHA-3 digest and a CRC-32.
---

## Install the package

Add the `Meziantou.Framework.Hashing` package to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.Hashing
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Hashing;`, and
`using System.Security.Cryptography;` for `RandomNumberGenerator` and `CryptographicOperations`.

## Hash a password

`Bcrypt.HashPassword` returns a string that contains the algorithm, its cost, a random salt and the hash. Store it, and
give it back to `Bcrypt.Verify` when the user signs in:

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/BcryptTests.cs;region=Bcrypt_HashPassword;format=md-fenced-code-block;dedent=true -->
```csharp
var hash = Bcrypt.HashPassword("my password");
var ok = Bcrypt.Verify("my password", hash);
Assert.True(ok);
```
<!-- endref -->

See [BCrypt](03-Bcrypt.md) to choose the cost, and [Argon2](04-Argon2.md) for the alternative.

## Hash data

The hash functions take a `ReadOnlySpan<byte>` and return a new array. `Sha3.HashData` takes the size of the digest in
bits:

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/KeccakTests.cs;region=Sha3_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var digest = Sha3.HashData(256, "abc"u8);
Assert.Equal("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532", Convert.ToHexStringLower(digest));
```
<!-- endref -->

Every `HashData` method also has an overload that writes into a `Span<byte>` you provide, so you can hash without
allocating an array. See [Cryptographic hashes](05-CryptographicHashes.md).

## Compute a checksum

`Crc32.Compute` returns the CRC-32 as a `uint`. `Crc32.Append` continues it with the next part of the data:

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/ChecksumTests.cs;region=Crc32_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var crc = Crc32.Compute("123456789"u8);
Assert.Equal(0xCBF43926u, crc);

// The CRC of data received in several parts
var running = Crc32.Compute("1234"u8);
running = Crc32.Append(running, "56789"u8);
Assert.Equal(crc, running);
```
<!-- endref -->

See [Checksums](06-Checksums.md).

## Next steps

- [BCrypt](03-Bcrypt.md): the cost, the revisions, and how to upgrade existing hashes.
- [Cryptographic hashes](05-CryptographicHashes.md): SHA-3, SHAKE, KMAC and BLAKE2b.
