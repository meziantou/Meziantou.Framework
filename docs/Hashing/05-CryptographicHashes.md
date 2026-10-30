---
title: Cryptographic hashes
description: Compute SHA-3, SHAKE and cSHAKE, authenticate messages with KMAC, and use TurboSHAKE, KangarooTwelve and BLAKE2b.
---

The cryptographic hash functions of the package come in two shapes:

- Static `HashData` methods hash data in one call. Each one has an overload that returns a new array, and one that
  writes into a `Span<byte>` you provide.
- [Shake](cref:Meziantou.Framework.Hashing.Shake) and [Blake2b](cref:Meziantou.Framework.Hashing.Blake2b) can also be instantiated, to append the data in several calls, for instance when it comes from a
  stream. Instances are not thread-safe.

## SHA-3

[Sha3.HashData](cref:Meziantou.Framework.Hashing.Sha3.HashData) computes SHA3-224, SHA3-256, SHA3-384 or SHA3-512, selected by the size of the digest in bits:

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/KeccakTests.cs;region=Sha3_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var digest = Sha3.HashData(256, "abc"u8);
Assert.Equal("3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532", Convert.ToHexStringLower(digest));
```
<!-- endref -->

Another size throws an `ArgumentOutOfRangeException`. The overload that writes into a span requires a span of exactly
the size of the digest.

## SHAKE: an output of any length

SHAKE128 and SHAKE256 are extendable-output functions: you choose the length of the output, and a shorter output is a
prefix of a longer one. The first parameter is the security strength in bits, 128 or 256.

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

With an instance, [AppendData](cref:Meziantou.Framework.Hashing.Shake.AppendData) adds data and [Read](cref:Meziantou.Framework.Hashing.Shake.Read) (or [ReadByte](cref:Meziantou.Framework.Hashing.Shake.ReadByte)) reads the next bytes of the output. The first read
ends the input: [AppendData](cref:Meziantou.Framework.Hashing.Shake.AppendData) then throws an `InvalidOperationException` until you call [Reset](cref:Meziantou.Framework.Hashing.Shake.Reset), which discards the
input and the output.

## cSHAKE: separate the uses of a function

Pass a customization string to compute cSHAKE: two parts of an application that hash the same data with different
customization strings get unrelated outputs.

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/KeccakTests.cs;region=Shake_Customization;format=md-fenced-code-block;dedent=true -->
```csharp
// cSHAKE: the customization string separates the outputs of two uses of the same function
var signing = Shake.HashData(256, "abc"u8, outputLength: 32, customization: "Email Signature"u8);
var plain = Shake.HashData(256, "abc"u8, outputLength: 32);
Assert.NotEqual(plain, signing);

// A shorter output is a prefix of a longer one
var longer = Shake.HashData(256, "abc"u8, outputLength: 64, customization: "Email Signature"u8);
Assert.Equal(signing, longer.AsSpan(0, 32).ToArray());
```
<!-- endref -->

The `functionName` parameter is reserved for the functions defined by NIST, and is usually left empty. cSHAKE with an
empty function name and an empty customization string is SHAKE.

## KMAC: authenticate a message

KMAC128 and KMAC256 compute a message authentication code from a key: only the holders of the key can compute the code
of a message, so a valid code proves that the message was not changed.

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/KeccakTests.cs;region=Kmac_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var key = RandomNumberGenerator.GetBytes(32);
var message = "the message to authenticate"u8;

// KMAC256 with a code of 32 bytes and a customization string
var code = Kmac.HashData(256, key, message, outputLength: 32, customization: "My Application"u8);

// To verify a message, compute the code again and compare in constant time
var expected = Kmac.HashData(256, key, message, outputLength: 32, customization: "My Application"u8);
Assert.True(CryptographicOperations.FixedTimeEquals(code, expected));

// The length is part of the input: a shorter code is not a prefix of a longer one
var shorter = Kmac.HashData(256, key, message, outputLength: 16, customization: "My Application"u8);
Assert.NotEqual(code.AsSpan(0, 16).ToArray(), shorter);
```
<!-- endref -->

Compare codes with `CryptographicOperations.FixedTimeEquals`, which takes the same time whatever the position of the
first difference. The length of the code is part of the input of KMAC: unlike SHAKE, a shorter code is not a prefix of
a longer one.

## TurboSHAKE and KangarooTwelve

TurboSHAKE128 and TurboSHAKE256 (RFC 9861) are SHAKE with 12 rounds of the permutation instead of 24, which makes them
faster. KT128 and KT256 (KangarooTwelve) build a tree hash over TurboSHAKE: the data is split in chunks of 8 KiB, whose
hashes are combined. Both are extendable-output functions.

<!-- ref:../../tests/Meziantou.Framework.Hashing.Tests/KeccakTests.cs;region=TurboShake_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// TurboSHAKE128, 32 bytes of output
var turboShake = TurboShake.HashData(128, "abc"u8, outputLength: 32);
Assert.HasCount(32, turboShake);

// A domain separation byte (0x01 to 0x7F) gives another, independent function
var otherDomain = TurboShake.HashData(128, "abc"u8, outputLength: 32, domainSeparation: 0x0B);
Assert.NotEqual(turboShake, otherDomain);

// KT128 (KangarooTwelve), with an optional customization string
var kt128 = KangarooTwelve.HashData(128, "abc"u8, outputLength: 32, customization: "My Application"u8);
Assert.HasCount(32, kt128);
```
<!-- endref -->

The domain separation byte of TurboSHAKE ranges from `0x01` to `0x7F`, and defaults to
[TurboShake.DefaultDomainSeparation](cref:Meziantou.Framework.Hashing.TurboShake.DefaultDomainSeparation) (`0x1F`). KangarooTwelve takes a customization string instead.

## BLAKE2b

[Blake2b](cref:Meziantou.Framework.Hashing.Blake2b) computes BLAKE2b (RFC 7693), with a digest of 1 to 64 bytes ([Blake2b.MaxHashSize](cref:Meziantou.Framework.Hashing.Blake2b.MaxHashSize), the default):

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

[GetHashAndReset](cref:Meziantou.Framework.Hashing.Blake2b.GetHashAndReset) returns the digest of the appended data and restarts the computation, so the instance can hash other
data. Keyed hashing (BLAKE2b used as a message authentication code) is not supported: use [KMAC](#kmac-authenticate-a-message)
instead.
