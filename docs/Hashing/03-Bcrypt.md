---
title: BCrypt
description: Hash and verify passwords with BCrypt, choose the work factor, and upgrade existing hashes.
---

BCrypt hashes passwords so that they can be stored: checking a password is fast enough for a sign-in, but slow enough to
make guessing passwords from a stolen database expensive. A BCrypt hash is a string such as `$2b$12$...` that contains
everything needed to verify a password: the revision (`2b`), the work factor (`12`), the salt and the hash.

## Hash and verify a password

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/BcryptTests.cs;region=Bcrypt_HashPassword;format=md-fenced-code-block;dedent=true -->
```csharp
var hash = Bcrypt.HashPassword("my password");
var ok = Bcrypt.Verify("my password", hash);
Assert.True(ok);
```
<!-- endref -->

- [HashPassword](cref:Meziantou.Framework.Hashing.Bcrypt.HashPassword) generates a random salt for each call: hashing the same password twice gives two different strings.
- [Verify](cref:Meziantou.Framework.Hashing.Bcrypt.Verify) reads the revision, the work factor and the salt from the hash, so it needs no other parameter.
- Both methods also take the password as a `ReadOnlySpan<char>`.

## Choose the work factor

The work factor (the cost) is a power of two: each increment doubles the time it takes to hash and to verify a
password. It ranges from [Bcrypt.MinWorkFactor](cref:Meziantou.Framework.Hashing.Bcrypt.MinWorkFactor) (4) to [Bcrypt.MaxWorkFactor](cref:Meziantou.Framework.Hashing.Bcrypt.MaxWorkFactor) (31), and the default is
[Bcrypt.DefaultWorkFactor](cref:Meziantou.Framework.Hashing.Bcrypt.DefaultWorkFactor) (11). Choose the highest value your servers can afford for each sign-in, and raise it as
hardware gets faster.

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/BcryptTests.cs;region=Bcrypt_RevisionAndWorkFactor;format=md-fenced-code-block;dedent=true -->
```csharp
var hash = Bcrypt.HashPassword(
    password: "my password",
    workFactor: 12,
    version: BcryptVersion.Revision2B);

var shouldRehash = Bcrypt.NeedsRehash(hash, workFactor: 13, version: BcryptVersion.Revision2B);
Assert.True(shouldRehash);
```
<!-- endref -->

[NeedsRehash](cref:Meziantou.Framework.Hashing.Bcrypt.NeedsRehash) returns `true` when a hash was created with another work factor or another revision than the ones you
pass.

## Upgrade the stored hashes

A hash cannot be converted to a new work factor: the password is needed. The only moment the application knows the
password is when the user signs in, so upgrade the hash then:

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/BcryptTests.cs;region=Bcrypt_RehashOnLogin;format=md-fenced-code-block;dedent=true -->
```csharp
// The hash stored for the user, created with a work factor of 10
var storedHash = Bcrypt.HashPassword("my password", workFactor: 10);

if (Bcrypt.Verify("my password", storedHash))
{
    // The password is correct: this is the only moment the application knows it,
    // so replace a hash created with another revision or work factor now
    if (Bcrypt.NeedsRehash(storedHash, workFactor: 12))
    {
        storedHash = Bcrypt.HashPassword("my password", workFactor: 12);
    }
}

Assert.Equal(12, Bcrypt.ParseHash(storedHash).WorkFactor);
```
<!-- endref -->

To find the hashes that use an old revision or a low work factor, read them with [ParseHash](cref:Meziantou.Framework.Hashing.Bcrypt.ParseHash), or [TryParseHash](cref:Meziantou.Framework.Hashing.Bcrypt.TryParseHash) for
values that may not be BCrypt hashes:

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/BcryptTests.cs;region=Bcrypt_ParseHash;format=md-fenced-code-block;dedent=true -->
```csharp
var hash = Bcrypt.HashPassword("my password", workFactor: 10, version: BcryptVersion.Revision2A);

// Read the revision and the work factor of a stored hash
var info = Bcrypt.ParseHash(hash);
Assert.Equal(BcryptVersion.Revision2A, info.Version);
Assert.Equal(10, info.WorkFactor);

// TryParseHash returns false for a value that is not a BCrypt hash
Assert.False(Bcrypt.TryParseHash("not a hash", out _));
```
<!-- endref -->

[ParseHash](cref:Meziantou.Framework.Hashing.Bcrypt.ParseHash) throws a `FormatException` when the value is not a BCrypt hash.

## Revisions

| [BcryptVersion](cref:Meziantou.Framework.Hashing.BcryptVersion) | Prefix | Support |
| --- | --- | --- |
| [Revision2](cref:Meziantou.Framework.Hashing.BcryptVersion.Revision2) | `$2$` | Verified only: salts cannot be generated |
| [Revision2A](cref:Meziantou.Framework.Hashing.BcryptVersion.Revision2A) | `$2a$` | Hashed and verified |
| [Revision2B](cref:Meziantou.Framework.Hashing.BcryptVersion.Revision2B) | `$2b$` | Hashed and verified; the default |
| [Revision2Y](cref:Meziantou.Framework.Hashing.BcryptVersion.Revision2Y) | `$2y$` | Hashed and verified; identical to `$2b$` |
| [Revision2X](cref:Meziantou.Framework.Hashing.BcryptVersion.Revision2X) | `$2x$` | Read by [ParseHash](cref:Meziantou.Framework.Hashing.Bcrypt.ParseHash) only |

`$2x$` exists only to reproduce a sign-extension bug of an old implementation, which this library does not emulate.
Hashing or verifying a `$2x$` hash throws a `NotSupportedException` rather than silently mis-verifying the passwords that
contain non-ASCII characters. Use [ParseHash](cref:Meziantou.Framework.Hashing.Bcrypt.ParseHash) to find those hashes, and ask the users to reset their password.

## Generate the salt yourself

`HashPassword(password, salt)` hashes with a salt you provide, which makes the result reproducible. Generate the salt
with [GenerateSalt](cref:Meziantou.Framework.Hashing.Bcrypt.GenerateSalt): it contains the revision and the work factor.

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/BcryptTests.cs;region=Bcrypt_GenerateSalt;format=md-fenced-code-block;dedent=true -->
```csharp
// The salt includes the revision and the work factor
var salt = Bcrypt.GenerateSalt(workFactor: 10);
Assert.StartsWith("$2b$10$", salt);

// The same password and salt always give the same hash
var hash = Bcrypt.HashPassword("my password", salt);
Assert.Equal(hash, Bcrypt.HashPassword("my password", salt));
Assert.True(Bcrypt.Verify("my password", hash));
```
<!-- endref -->

[HashPassword](cref:Meziantou.Framework.Hashing.Bcrypt.HashPassword) throws a `FormatException` when the salt is not valid.

## Password length

BCrypt processes the password as UTF-8 and only uses its first 72 bytes ([Bcrypt.MaxPasswordLengthInBytes](cref:Meziantou.Framework.Hashing.Bcrypt.MaxPasswordLengthInBytes)). Two
passwords that share their first 72 bytes verify against the same hash. A character outside ASCII takes 2 to 4 bytes,
so the limit can be reached with fewer than 72 characters.

A password that contains an unpaired surrogate cannot be encoded as UTF-8: [HashPassword](cref:Meziantou.Framework.Hashing.Bcrypt.HashPassword) throws an
`ArgumentException`, and [Verify](cref:Meziantou.Framework.Hashing.Bcrypt.Verify) returns `false`.
