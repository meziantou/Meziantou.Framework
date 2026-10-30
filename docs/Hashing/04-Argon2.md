---
title: Argon2
description: Hash passwords and derive keys with Argon2id, choose the memory, the passes and the parallelism, and store the parameters.
---

Argon2 (RFC 9106) hashes a password, or derives a key from it. Its cost is set by an amount of memory as well as a
number of passes, which makes guessing passwords on dedicated hardware expensive. RFC 9106 recommends the Argon2id
variant.

## Hash a password

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/Argon2Tests.cs;region=Argon2_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// A random salt of 16 bytes, stored next to the hash
var salt = RandomNumberGenerator.GetBytes(16);

// RFC 9106 section 4: Argon2id with 64 MiB of memory, 3 passes and 4 lanes
var hash = Argon2.DeriveBytes(Argon2Type.Argon2id, "my password"u8, salt, parallelism: 4, memorySizeInKiB: 64 * 1024, passes: 3, outputLength: 32);

// To verify a password, compute the hash with the same parameters and compare in constant time
var other = Argon2.DeriveBytes(Argon2Type.Argon2id, "my password"u8, salt, parallelism: 4, memorySizeInKiB: 64 * 1024, passes: 3, outputLength: 32);
Assert.True(CryptographicOperations.FixedTimeEquals(hash, other));
```
<!-- endref -->

[Argon2.DeriveBytes](cref:Meziantou.Framework.Hashing.Argon2.DeriveBytes) returns the tag (the hash) as a new array of `outputLength` bytes. An overload writes it into a
`Span<byte>` instead: the length of the span is the length of the tag.

Unlike [BCrypt](03-Bcrypt.md), the result is only the tag: the package has no string format that also contains the
parameters. Store the salt, the variant, the parameters and the tag together, so a password can be verified after you
change the parameters for new hashes. To verify a password, compute the tag again with the stored values and compare
the two tags with `CryptographicOperations.FixedTimeEquals`, which takes the same time whatever the position of the
first difference.

## Parameters

| Parameter | Meaning | Allowed values |
| --- | --- | --- |
| `type` | The variant: [Argon2Type.Argon2id](cref:Meziantou.Framework.Hashing.Argon2Type.Argon2id), [Argon2i](cref:Meziantou.Framework.Hashing.Argon2Type.Argon2i) or [Argon2d](cref:Meziantou.Framework.Hashing.Argon2Type.Argon2d) | Use [Argon2id](cref:Meziantou.Framework.Hashing.Argon2Type.Argon2id) unless you have a reason not to |
| `password` | The password, as bytes | Any length |
| `salt` | Random data, different for each password | 16 random bytes are recommended |
| `parallelism` | The number of lanes | 1 to [Argon2.MaxParallelism](cref:Meziantou.Framework.Hashing.Argon2.MaxParallelism) |
| `memorySizeInKiB` | The memory to use, in kibibytes | At least 8 times `parallelism` |
| `passes` | The number of passes over the memory | At least 1 |
| `outputLength` | The length of the tag, in bytes | At least [Argon2.MinOutputLength](cref:Meziantou.Framework.Hashing.Argon2.MinOutputLength) (4) |
| `secret` | A secret value (a pepper), kept outside the database | Usually empty |
| `associatedData` | Data bound to the tag | Usually empty |
| `version` | [Argon2Version.Version13](cref:Meziantou.Framework.Hashing.Argon2Version.Version13) (RFC 9106, the default) or [Version10](cref:Meziantou.Framework.Hashing.Argon2Version.Version10) | Keep the default |

A parameter outside its range throws an `ArgumentOutOfRangeException`.

RFC 9106 (section 4) gives recommended settings, such as 2 GiB of memory with 1 pass, or 64 MiB of memory with 3
passes, both with 4 lanes. Choose the memory size from what your servers can afford for each concurrent sign-in: each
call allocates the whole memory, and throws an `OutOfMemoryException` when it cannot.

## Parallelism

The lanes are computed one after the other, on the calling thread: `parallelism` changes the result, not the number of
threads used. Use the value your other systems use, or the one RFC 9106 recommends, and keep it to verify the existing
hashes.

## Derive a key

The tag can be used as a key, for instance to encrypt data with a password: pass the length of the key as
`outputLength`, and store the salt and the parameters with the encrypted data so the key can be derived again.
