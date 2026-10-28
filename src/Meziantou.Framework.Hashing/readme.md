# Meziantou.Framework.Hashing

Fully managed hash algorithms that the base class library does not provide, or does not provide on every platform:

| Category | Algorithms | Types |
|---|---|---|
| Password hashing and key derivation | BCrypt, Argon2d, Argon2i, Argon2id (RFC 9106) | `Bcrypt`, `Argon2` |
| Cryptographic hashes | SHA3-224/256/384/512, SHAKE128/256 (FIPS 202), cSHAKE128/256, KMAC128/256 (NIST SP 800-185), TurboSHAKE128/256, KT128/KT256 (KangarooTwelve, RFC 9861), BLAKE2b (RFC 7693) | `Sha3`, `Shake`, `Kmac`, `TurboShake`, `KangarooTwelve`, `Blake2b` |
| Checksums | CRC-32 (zlib, gzip, ZIP, PNG), the CRC-32 of Ogg pages, Adler-32 (RFC 1950) | `Crc32`, `OggCrc32`, `Adler32` |

The types are in the `Meziantou.Framework.Hashing` namespace. The SHA-3 types of `System.Security.Cryptography` use the
cryptographic library of the operating system, which does not support them on every platform (e.g. macOS): the types of
this package work everywhere.

This package replaces `Meziantou.Framework.Bcrypt`: the BCrypt types have the same names, in the
`Meziantou.Framework.Hashing` namespace.

The implementations are tested against the test vectors of their specifications, but they have not been audited.

## BCrypt

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/BcryptTests.cs;region=Bcrypt_HashPassword;format=md-fenced-code-block;dedent=true -->
```csharp
var hash = Bcrypt.HashPassword("my password");
var ok = Bcrypt.Verify("my password", hash);
Assert.True(ok);
```
<!-- endref -->

You can select the BCrypt revision and cost (work factor):

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

Supported revisions are:

- `2` (`$2$`) - verified only; salts cannot be generated
- `2a` (`$2a$`)
- `2b` (`$2b$`) - the default
- `2y` (`$2y$`) - identical to `$2b$`

`$2x$` is **not** supported. It exists only to reproduce the crypt_blowfish sign-extension bug, which
this library does not emulate, so hashing or verifying a `$2x$` hash throws `NotSupportedException`
rather than silently mis-verifying passwords that contain non-ASCII characters. `ParseHash` still
reads `$2x$` hashes so you can find them in an existing database and re-hash those passwords.

BCrypt processes passwords as UTF-8 and uses at most 72 bytes.

## Argon2

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

## SHA-3 and SHAKE

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/KeccakTests.cs;region=Sha3_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var digest = Sha3.HashData(256, "abc"u8);
Assert.Equal("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532", Convert.ToHexStringLower(digest));
```
<!-- endref -->

SHAKE is an extendable-output function: the output can have any length. Pass a function name or a customization string
to compute cSHAKE.

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/KeccakTests.cs;region=Shake_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// SHAKE256 of "abc", 32 bytes of output
var output = Shake.HashData(256, "abc"u8, outputLength: 32);

// The same output, with the data appended and the output read in several parts
var shake = new Shake(256);
shake.AppendData("a"u8);
shake.AppendData("bc"u8);
var first = new byte[16];
var second = new byte[16];
shake.Read(first);
shake.Read(second);
Assert.Equal(output, first.Concat(second).ToArray());
```
<!-- endref -->

`Kmac`, `TurboShake` and `KangarooTwelve` have the same shape: a `HashData` method that returns an array of the requested
length, and an overload that fills a buffer.

## BLAKE2b

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/Blake2bTests.cs;region=Blake2b_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// A digest of 32 bytes (BLAKE2b-256); the default size is 64 bytes (BLAKE2b-512)
var digest = Blake2b.HashData("abc"u8, hashSize: 32);

// The same digest, with the data appended in several parts
var blake2b = new Blake2b(hashSize: 32);
blake2b.AppendData("a"u8);
blake2b.AppendData("bc"u8);
Assert.Equal(digest, blake2b.GetHashAndReset());
```
<!-- endref -->

## Checksums

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

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/ChecksumTests.cs;region=Adler32_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var checksum = Adler32.Compute("Wikipedia"u8);
Assert.Equal(0x11E60398u, checksum);

// The checksum of data received in several parts
var running = Adler32.Compute("Wiki"u8);
running = Adler32.Append(running, "pedia"u8);
Assert.Equal(checksum, running);
```
<!-- endref -->

`OggCrc32` works the same way. `Crc32` and `Adler32` use the vector and CRC instructions of the processor when they are
available.
