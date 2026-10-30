---
title: Introduction
description: Fully managed hash algorithms that work on every platform, from password hashing to checksums.
---

`Meziantou.Framework.Hashing` provides hash algorithms that the base class library does not provide, or does not provide
on every platform. Every algorithm is implemented in managed code: it gives the same result on Windows, Linux, macOS and
every other platform .NET runs on, without depending on the cryptographic library of the operating system.

For instance, the SHA-3 types of `System.Security.Cryptography` use the cryptographic library of the operating system,
which does not support them everywhere (macOS among others). The `Sha3` type of this package works everywhere.

## What you can do with it

| Task | Algorithms | Types | Page |
| --- | --- | --- | --- |
| Store passwords | BCrypt | `Bcrypt` | [BCrypt](03-Bcrypt.md) |
| Store passwords, derive keys from passwords | Argon2d, Argon2i, Argon2id (RFC 9106) | `Argon2` | [Argon2](04-Argon2.md) |
| Hash data, authenticate messages | SHA3-224/256/384/512, SHAKE128/256, cSHAKE128/256, KMAC128/256, TurboSHAKE128/256, KT128/KT256, BLAKE2b | `Sha3`, `Shake`, `Kmac`, `TurboShake`, `KangarooTwelve`, `Blake2b` | [Cryptographic hashes](05-CryptographicHashes.md) |
| Detect accidental changes | CRC-32 (zlib, gzip, ZIP, PNG), Adler-32 (zlib) | `Crc32`, `Adler32` | [Checksums](06-Checksums.md) |

## Things to know

- The implementations are tested against the test vectors of their specifications, but they have not been audited.
- The package replaces `Meziantou.Framework.Bcrypt`: the BCrypt types keep their names, in the
  [Meziantou.Framework.Hashing](cref:Meziantou.Framework.Hashing) namespace.
- `Crc32` and `Adler32` use the vector and CRC instructions of the processor when they are available.

## Namespace

Every type is in the [Meziantou.Framework.Hashing](cref:Meziantou.Framework.Hashing) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and compute your first hashes.
- [BCrypt](03-Bcrypt.md): hash and verify passwords.
