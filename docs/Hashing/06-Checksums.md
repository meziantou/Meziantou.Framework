---
title: Checksums
description: Compute the CRC-32 of zlib, gzip, ZIP and PNG, and the Adler-32 of zlib, in one call or in several parts.
---

Checksums detect accidental changes, such as a corrupted file or a truncated download. They are fast, but they do not
protect against deliberate changes: anyone can compute the checksum of modified data. Use a
[cryptographic hash](05-CryptographicHashes.md) for that.

## CRC-32

[Crc32](cref:Meziantou.Framework.Hashing.Crc32) computes the CRC-32 of ISO 3309 (CRC-32/ISO-HDLC): the CRC of zlib, gzip, ZIP, PNG and Ethernet.

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

- `Compute(data)` returns the CRC-32 of the data, 0 for empty data.
- `Append(crc, data)` returns the CRC-32 of the data that `crc` covers followed by `data`: `Append(Compute(a), b)` is
  `Compute(a + b)`. Start from 0 when there is no preceding data. Use it for data that arrives in several parts, such
  as the buffers read from a stream.

## Adler-32

[Adler32](cref:Meziantou.Framework.Hashing.Adler32) computes the Adler-32 checksum of RFC 1950: the checksum of zlib streams.

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

[Compute](cref:Meziantou.Framework.Hashing.Adler32.Compute) and [Append](cref:Meziantou.Framework.Hashing.Adler32.Append) work like the ones of [Crc32](cref:Meziantou.Framework.Hashing.Crc32), except that the checksum of empty data is 1: start from 1 when
there is no preceding data.

## Performance

Both types use the instructions of the processor when they are available: the CRC32 instructions of Arm64 processors
and the carry-less multiplication of x64 processors for [Crc32](cref:Meziantou.Framework.Hashing.Crc32), the vector instructions (AdvSimd, AVX2, SSSE3) for
[Adler32](cref:Meziantou.Framework.Hashing.Adler32). Every path computes the same value. Pass large buffers rather than a few bytes at a time to benefit from
them.
