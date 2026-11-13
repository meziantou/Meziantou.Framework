using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Hashing.Fuzz;

/// <summary>
/// Every algorithm of Meziantou.Framework.Hashing is compared with an oracle: a reference implementation written from the
/// specification (<see cref="ReferenceImplementations"/>), the base class library when the platform supports the algorithm
/// (SHA-3, SHAKE and KMAC through OpenSSL on Linux), or an identity between two public APIs (one-shot and incremental
/// hashing, array and span overloads, a shorter output that is a prefix of a longer one).
/// </summary>
/// <remarks>
/// Most targets start with option bytes, then length-prefixed fields (<see cref="ReadField"/>), then the message
/// (<see cref="ReadMessage"/>): two bytes give its length, and the rest of the input is repeated to that length, so that
/// short inputs reach the paths of long messages (the 8 KiB chunks of KangarooTwelve, the 5552-byte interval of Adler-32,
/// the vector loops of the checksums).
/// </remarks>
public static partial class HashingFuzzTargets
{
    // Verifying a bcrypt hash costs 2^cost rounds of the key schedule, and the cost comes from the hash
    private const int MaxBcryptWorkFactor = 5;

    // The lengths at which the targets split a message or an output: around the block sizes (64, 128), the rates of the
    // sponges (72 to 168 bytes) and the chunks of KangarooTwelve (8192 bytes)
    private static readonly int[] ChunkLengths = [1, 2, 3, 7, 8, 16, 31, 63, 64, 65, 71, 72, 104, 127, 128, 129, 135, 136, 143, 144, 167, 168, 169, 1000, 8191, 8192, 8193];

    private static readonly BcryptVersion[] BcryptVersions = [BcryptVersion.Revision2, BcryptVersion.Revision2A, BcryptVersion.Revision2B, BcryptVersion.Revision2X, BcryptVersion.Revision2Y];

    private static readonly string[] BcryptTokens = ["$", "$2$", "$2a$", "$2b$", "$2x$", "$2y$", "$04$", "$05$", "$31$", "$32$", "$03$", ".", "/", "O", "e", "u", "C", "\0"];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("hashing-checksums", Checksums) { Seeds = GetChecksumSeeds },
        new("hashing-sha3", Sha3Digest) { Seeds = GetMessageSeeds },
        new("hashing-shake", ShakeOutput) { Seeds = GetShakeSeeds },
        new("hashing-kmac", KmacOutput) { Seeds = GetKmacSeeds, Dictionary = ["KMAC"] },
        new("hashing-kangarootwelve", KangarooTwelveOutput) { Seeds = GetKangarooTwelveSeeds },
        new("hashing-blake2b", Blake2bDigest) { Seeds = GetBlake2bSeeds },
        new("hashing-argon2", Argon2Tag) { Seeds = GetArgon2Seeds },
        new("hashing-bcrypt-parse", BcryptParse) { Seeds = GetBcryptParseSeeds, Dictionary = BcryptTokens },
        new("hashing-bcrypt-hash", BcryptHash) { Seeds = GetBcryptHashSeeds },
    ];

    /// <summary>
    /// A byte selects where the message is split, then comes the message. CRC-32 and Adler-32 match the bit-at-a-time
    /// references, Append over the two parts gives the checksum of the whole, and an unaligned copy gives the same result.
    /// </summary>
    private static void Checksums(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var message = ReadMessage(ref data);

        var crc = Crc32.Compute(message);
        FuzzAssert.Equal(ReferenceImplementations.Crc32(message), crc, $"CRC-32 of {message.Length} bytes");

        var adler = Adler32.Compute(message);
        FuzzAssert.Equal(ReferenceImplementations.Adler32(message), adler, $"Adler-32 of {message.Length} bytes");

        var split = (int)((long)message.Length * options / 255);
        FuzzAssert.Equal(crc, Crc32.Append(Crc32.Compute(message.AsSpan(0, split)), message.AsSpan(split)), $"CRC-32 appended at {split}");
        FuzzAssert.Equal(adler, Adler32.Append(Adler32.Compute(message.AsSpan(0, split)), message.AsSpan(split)), $"Adler-32 appended at {split}");

        // The vector paths load 16 to 256 bytes at a time: a copy that is not aligned on them gives the same checksums
        var offset = (options & 0x0F) + 1;
        var unaligned = new byte[message.Length + offset];
        message.CopyTo(unaligned, offset);
        FuzzAssert.Equal(crc, Crc32.Compute(unaligned.AsSpan(offset)), $"CRC-32 at offset {offset}");
        FuzzAssert.Equal(adler, Adler32.Compute(unaligned.AsSpan(offset)), $"Adler-32 at offset {offset}");
    }

    /// <summary>
    /// A byte selects the size of the digest, then comes the message. The array and span overloads agree, a destination of
    /// another length is rejected, and the digest matches the base class library when the platform supports SHA-3.
    /// </summary>
    private static void Sha3Digest(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var message = ReadMessage(ref data);
        int[] sizes = [224, 256, 384, 512];
        var size = sizes[options & 0x03];

        var digest = Sha3.HashData(size, message);
        FuzzAssert.Equal(size / 8, digest.Length, "Length of the SHA-3 digest");

        var destination = new byte[size / 8];
        Sha3.HashData(size, message, destination);
        AssertEqual(digest, destination, $"SHA3-{size}: HashData into a span");

        var wrongLength = (options & 0x04) is 0 ? (size / 8) - 1 : (size / 8) + 1;
        try
        {
            Sha3.HashData(size, message, new byte[wrongLength]);
            FuzzAssert.Fail($"SHA3-{size} accepted a destination of {wrongLength} bytes");
        }
        catch (ArgumentException ex) when (ex is not ArgumentOutOfRangeException)
        {
        }

        byte[]? expected = size switch
        {
            256 when SHA3_256.IsSupported => SHA3_256.HashData(message),
            384 when SHA3_384.IsSupported => SHA3_384.HashData(message),
            512 when SHA3_512.IsSupported => SHA3_512.HashData(message),
            _ => null,
        };

        if (expected is not null)
        {
            AssertEqual(expected, digest, $"SHA3-{size} differs from the base class library");
        }
    }

    /// <summary>
    /// The options select the security strength, the length of the output and how the input and the output are split,
    /// then come the function name N, the customization string S and the message. Appending the message in parts and
    /// reading the output in parts give the one-shot output, a shorter output is a prefix of it, Reset starts over, and
    /// SHAKE (empty N and S) matches the base class library when the platform supports it.
    /// </summary>
    private static void ShakeOutput(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var outputLength = FuzzInput.ReadByte(ref data) | ((options & 0x30) << 4);
        var chunking = FuzzInput.ReadByte(ref data);
        var functionName = ReadField(ref data);
        var customization = ReadField(ref data);
        var message = ReadMessage(ref data);
        var securityStrength = (options & 0x01) is 0 ? 128 : 256;
        var name = $"{((functionName.IsEmpty && customization.IsEmpty) ? "SHAKE" : "cSHAKE")}{securityStrength}";

        var output = Shake.HashData(securityStrength, message, outputLength, functionName, customization);
        FuzzAssert.Equal(outputLength, output.Length, $"Length of the {name} output");

        var destination = new byte[outputLength];
        Shake.HashData(securityStrength, message, destination, functionName, customization);
        AssertEqual(output, destination, $"{name}: HashData into a span");

        var prefixLength = outputLength * (chunking & 0x0F) / 16;
        AssertEqual(output.AsSpan(0, prefixLength), Shake.HashData(securityStrength, message, prefixLength, functionName, customization), $"{name}: the output of {prefixLength} bytes is not a prefix of the output of {outputLength} bytes");

        var shake = new Shake(securityStrength, functionName, customization);
        FuzzAssert.Equal(securityStrength, shake.SecurityStrength, "SecurityStrength");
        for (var round = 0; round < 2; round++)
        {
            AppendInChunks(shake, message, chunking, (options & 0x02) is not 0);
            var streamed = ReadInChunks(shake, outputLength, chunking >> 4, (options & 0x04) is not 0);
            AssertEqual(output, streamed, $"{name}: incremental hashing (round {round})");

            // The first read ends the input, even an empty one
            shake.Read([]);
            try
            {
                shake.AppendData((byte)0);
                FuzzAssert.Fail($"{name} accepted data after the output was read");
            }
            catch (InvalidOperationException)
            {
            }

            shake.Reset();
        }

        if (functionName.IsEmpty && customization.IsEmpty)
        {
            var expected = new byte[outputLength];
            if (securityStrength is 128 && Shake128.IsSupported)
            {
                Shake128.HashData(message, expected);
                AssertEqual(expected, output, "SHAKE128 differs from the base class library");
            }
            else if (securityStrength is 256 && Shake256.IsSupported)
            {
                Shake256.HashData(message, expected);
                AssertEqual(expected, output, "SHAKE256 differs from the base class library");
            }
        }
    }

    /// <summary>
    /// The options select the security strength and the length of the code, then come the key, the customization string
    /// and the message. The array and span overloads agree, and the code matches the base class library when the platform
    /// supports KMAC and OpenSSL accepts the lengths (a key of 4 to 512 bytes, a customization string of at most 512).
    /// </summary>
    private static void KmacOutput(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var outputLength = FuzzInput.ReadByte(ref data) | ((options & 0x30) << 4);
        var key = ReadField(ref data);
        var customization = ReadField(ref data);
        var message = ReadMessage(ref data);
        var securityStrength = (options & 0x01) is 0 ? 128 : 256;

        var output = Kmac.HashData(securityStrength, key, message, outputLength, customization);
        FuzzAssert.Equal(outputLength, output.Length, $"Length of the KMAC{securityStrength} output");

        var destination = new byte[outputLength];
        Kmac.HashData(securityStrength, key, message, destination, customization);
        AssertEqual(output, destination, $"KMAC{securityStrength}: HashData into a span");

        if (outputLength is 0 || key.Length < 4)
            return;

        var expected = new byte[outputLength];
        if (securityStrength is 128 && Kmac128.IsSupported)
        {
            Kmac128.HashData(key, message, expected, customization);
            AssertEqual(expected, output, "KMAC128 differs from the base class library");
        }
        else if (securityStrength is 256 && Kmac256.IsSupported)
        {
            Kmac256.HashData(key, message, expected, customization);
            AssertEqual(expected, output, "KMAC256 differs from the base class library");
        }
    }

    /// <summary>
    /// The options select the security strength, the length of the output and the domain separation byte of TurboSHAKE,
    /// then come the customization string and the message. KangarooTwelve matches the tree assembled from the public
    /// TurboSHAKE; TurboSHAKE rejects a domain separation byte outside 0x01 to 0x7F, its overloads agree, and a shorter
    /// output is a prefix of a longer one.
    /// </summary>
    private static void KangarooTwelveOutput(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var outputLength = FuzzInput.ReadByte(ref data) | ((options & 0x30) << 4);
        var domainSeparation = FuzzInput.ReadByte(ref data);
        var customization = ReadField(ref data);
        var message = ReadMessage(ref data);
        var securityStrength = (options & 0x01) is 0 ? 128 : 256;

        var output = KangarooTwelve.HashData(securityStrength, message, outputLength, customization);
        FuzzAssert.Equal(outputLength, output.Length, $"Length of the KT{securityStrength} output");
        AssertEqual(ReferenceImplementations.KangarooTwelve(securityStrength, message, customization, outputLength), output, $"KT{securityStrength} of {message.Length} bytes with {customization.Length} bytes of customization");

        var destination = new byte[outputLength];
        KangarooTwelve.HashData(securityStrength, message, destination, customization);
        AssertEqual(output, destination, $"KT{securityStrength}: HashData into a span");

        if (domainSeparation is 0 or > 0x7F)
        {
            try
            {
                TurboShake.HashData(securityStrength, message, outputLength, domainSeparation);
                FuzzAssert.Fail($"TurboSHAKE accepted the domain separation byte 0x{domainSeparation:X2}");
            }
            catch (ArgumentOutOfRangeException)
            {
            }

            return;
        }

        var turboShake = TurboShake.HashData(securityStrength, message, outputLength, domainSeparation);
        TurboShake.HashData(securityStrength, message, destination, domainSeparation);
        AssertEqual(turboShake, destination, $"TurboSHAKE{securityStrength}: HashData into a span");

        var prefixLength = outputLength * (options >> 6) / 4;
        AssertEqual(turboShake.AsSpan(0, prefixLength), TurboShake.HashData(securityStrength, message, prefixLength, domainSeparation), $"TurboSHAKE{securityStrength}: the output of {prefixLength} bytes is not a prefix of the output of {outputLength} bytes");
    }

    /// <summary>
    /// A byte selects the size of the digest (0 and 65 are out of range), another how the message is split, then comes
    /// the message. The digest matches the reference implementation, whether it is computed in one call, into a span, or
    /// appended in parts to an instance that is reused after GetHashAndReset.
    /// </summary>
    private static void Blake2bDigest(ReadOnlySpan<byte> data)
    {
        var hashSize = FuzzInput.ReadByte(ref data) % (Blake2b.MaxHashSize + 2);
        var chunking = FuzzInput.ReadByte(ref data);
        var message = ReadMessage(ref data);

        if (hashSize is < 1 or > Blake2b.MaxHashSize)
        {
            AssertThrows<ArgumentOutOfRangeException>(() => Blake2b.HashData(message, hashSize), $"BLAKE2b accepted a digest of {hashSize} bytes");
            AssertThrows<ArgumentOutOfRangeException>(() => _ = new Blake2b(hashSize), $"BLAKE2b accepted a digest of {hashSize} bytes");
            AssertThrows<ArgumentOutOfRangeException>(() => Blake2b.HashData(message, new byte[hashSize]), $"BLAKE2b accepted a destination of {hashSize} bytes");
            return;
        }

        var expected = ReferenceImplementations.Blake2b(message, hashSize);
        AssertEqual(expected, Blake2b.HashData(message, hashSize), $"BLAKE2b-{hashSize * 8} of {message.Length} bytes");

        var destination = new byte[hashSize];
        Blake2b.HashData(message, destination);
        AssertEqual(expected, destination, $"BLAKE2b-{hashSize * 8}: HashData into a span");

        var blake2b = new Blake2b(hashSize);
        FuzzAssert.Equal(hashSize, blake2b.HashSize, "HashSize");
        for (var round = 0; round < 2; round++)
        {
            var offset = 0;
            for (var i = 0; offset < message.Length; i++)
            {
                var length = Math.Min(GetChunkLength(chunking, i), message.Length - offset);
                blake2b.AppendData(message.AsSpan(offset, length));
                offset += length;
            }

            if (round is 0)
            {
                AssertEqual(expected, blake2b.GetHashAndReset(), $"BLAKE2b-{hashSize * 8}: incremental hashing");
            }
            else
            {
                blake2b.GetHashAndReset(destination);
                AssertEqual(expected, destination, $"BLAKE2b-{hashSize * 8}: incremental hashing after GetHashAndReset");
            }
        }

        if (hashSize < Blake2b.MaxHashSize)
        {
            AssertThrows<ArgumentException>(() => blake2b.GetHashAndReset(new byte[hashSize + 1]), "GetHashAndReset accepted a destination of another length");
        }
    }

    /// <summary>
    /// The options select the type, the version, the degree of parallelism, the memory size, the number of passes and the
    /// length of the tag, some of them out of range, then come the password, the salt, the secret and the associated
    /// data. A parameter out of the range of RFC 9106 throws ArgumentOutOfRangeException from both overloads; otherwise
    /// both overloads give the same tag, twice.
    /// </summary>
    private static void Argon2Tag(ReadOnlySpan<byte> data)
    {
        var typeAndVersion = FuzzInput.ReadByte(ref data);
        var parallelism = FuzzInput.ReadByte(ref data) % 5;
        var memorySizeInKiB = FuzzInput.ReadByte(ref data);
        var passes = FuzzInput.ReadByte(ref data) % 4;
        var outputLength = FuzzInput.ReadByte(ref data);
        var password = ReadField(ref data);
        var salt = ReadField(ref data);
        var secret = ReadField(ref data);
        var associatedData = data;

        var type = (Argon2Type)(typeAndVersion & 0x03);
        var version = ((typeAndVersion >> 2) & 0x03) switch
        {
            0 => Argon2Version.Version10,
            1 => Argon2Version.Version13,
            _ => (Argon2Version)(typeAndVersion >> 4),
        };

        var isValid = Enum.IsDefined(type)
            && Enum.IsDefined(version)
            && parallelism >= 1
            && passes >= 1
            && outputLength >= Argon2.MinOutputLength
            && memorySizeInKiB >= 8 * parallelism;
        var description = $"Argon2 type={(int)type} version=0x{(int)version:X2} p={parallelism} m={memorySizeInKiB} t={passes} T={outputLength}";

        byte[]? tag = null;
        try
        {
            tag = Argon2.DeriveBytes(type, password, salt, parallelism, memorySizeInKiB, passes, outputLength, secret, associatedData, version);
        }
        catch (ArgumentOutOfRangeException) when (!isValid)
        {
        }

        FuzzAssert.Equal(isValid, tag is not null, $"{description}: accepted by DeriveBytes");

        var destination = new byte[outputLength];
        try
        {
            Argon2.DeriveBytes(type, password, salt, parallelism, memorySizeInKiB, passes, destination, secret, associatedData, version);
        }
        catch (ArgumentOutOfRangeException) when (!isValid)
        {
            return;
        }

        FuzzAssert.True(isValid && tag is not null, $"{description} is accepted by DeriveBytes into a span");
        FuzzAssert.Equal(outputLength, tag.Length, $"{description}: length of the tag");
        AssertEqual(tag, destination, $"{description}: DeriveBytes into a span");
        AssertEqual(tag, Argon2.DeriveBytes(type, password, salt, parallelism, memorySizeInKiB, passes, outputLength, secret, associatedData, version), $"{description}: a second call gives another tag");
    }

    /// <summary>
    /// The first byte selects the work factor and the revision given to NeedsRehash, then come a password up to a NUL byte
    /// and a hash. Every overload of TryParseHash and ParseHash agrees with a reference parser of the format; NeedsRehash
    /// compares the parsed revision and work factor; Verify never throws, except NotSupportedException for "$2x$"; and
    /// hashing the password with the hash as the salt keeps its revision, work factor and salt.
    /// </summary>
    private static void BcryptParse(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        var password = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
        var hash = FuzzText.Decode(data);

        var isValid = ReferenceImplementations.TryParseBcryptHash(hash, out var expectedVersion, out var expectedWorkFactor);
        var parsed = Bcrypt.TryParseHash(hash, out var info);
        FuzzAssert.Equal(isValid, parsed, $"TryParseHash({FuzzAssert.Format(hash)})");
        FuzzAssert.Equal(parsed, Bcrypt.TryParseHash(hash.AsSpan(), out var spanInfo), $"TryParseHash(string) and TryParseHash(ReadOnlySpan<char>) disagree on {FuzzAssert.Format(hash)}");
        FuzzAssert.Equal(info, spanInfo, "TryParseHash(string) and TryParseHash(ReadOnlySpan<char>) give different results");

        var rehashWorkFactor = Bcrypt.MinWorkFactor + (options & 0x1F);
        var rehashVersion = BcryptVersions[(options >> 5) % BcryptVersions.Length];
        if (!parsed)
        {
            AssertThrows<FormatException>(() => Bcrypt.ParseHash(hash), $"ParseHash accepted {FuzzAssert.Format(hash)}");
            AssertThrows<FormatException>(() => Bcrypt.ParseHash(hash.AsSpan()), $"ParseHash(ReadOnlySpan<char>) accepted {FuzzAssert.Format(hash)}");
            if (rehashWorkFactor <= Bcrypt.MaxWorkFactor)
            {
                AssertThrows<FormatException>(() => Bcrypt.NeedsRehash(hash, rehashWorkFactor, rehashVersion), $"NeedsRehash accepted {FuzzAssert.Format(hash)}");
            }

            FuzzAssert.False(Bcrypt.Verify(password, hash), $"Verify accepted the invalid hash {FuzzAssert.Format(hash)}");
            FuzzAssert.False(Bcrypt.Verify(password.AsSpan(), hash.AsSpan()), $"Verify(ReadOnlySpan<char>) accepted the invalid hash {FuzzAssert.Format(hash)}");
            return;
        }

        FuzzAssert.Equal(expectedVersion, info.Version, $"Revision of {FuzzAssert.Format(hash)}");
        FuzzAssert.Equal(expectedWorkFactor, info.WorkFactor, $"Work factor of {FuzzAssert.Format(hash)}");
        FuzzAssert.Equal(info, Bcrypt.ParseHash(hash), "ParseHash and TryParseHash disagree");
        FuzzAssert.Equal(info, Bcrypt.ParseHash(hash.AsSpan()), "ParseHash(ReadOnlySpan<char>) and TryParseHash disagree");

        if (rehashWorkFactor > Bcrypt.MaxWorkFactor)
        {
            AssertThrows<ArgumentOutOfRangeException>(() => Bcrypt.NeedsRehash(hash, rehashWorkFactor, rehashVersion), $"NeedsRehash accepted the work factor {rehashWorkFactor}");
        }
        else
        {
            var expected = info.WorkFactor != rehashWorkFactor || info.Version != rehashVersion;
            FuzzAssert.Equal(expected, Bcrypt.NeedsRehash(hash, rehashWorkFactor, rehashVersion), $"NeedsRehash({FuzzAssert.Format(hash)}, {rehashWorkFactor}, {rehashVersion})");
            FuzzAssert.Equal(expected, Bcrypt.NeedsRehash(hash.AsSpan(), rehashWorkFactor, rehashVersion), $"NeedsRehash(ReadOnlySpan<char>) on {FuzzAssert.Format(hash)}");
        }

        if (info.WorkFactor > MaxBcryptWorkFactor)
            return;

        if (info.Version is BcryptVersion.Revision2X)
        {
            AssertThrows<NotSupportedException>(() => Bcrypt.Verify(password, hash), "Verify accepted a $2x$ hash");
            AssertThrows<NotSupportedException>(() => Bcrypt.HashPassword(password, hash), "HashPassword accepted a $2x$ salt");
            return;
        }

        var verified = Bcrypt.Verify(password, hash);
        FuzzAssert.Equal(verified, Bcrypt.Verify(password.AsSpan(), hash.AsSpan()), "Verify(string) and Verify(ReadOnlySpan<char>) disagree");
        if (!IsValidUtf16(password))
        {
            FuzzAssert.False(verified, "Verify accepted a password that UTF-8 cannot encode");
            return;
        }

        // The prefix "$2y$05$" and the 22 characters of salt
        var rehashed = Bcrypt.HashPassword(password, hash);
        var saltLength = hash.Length - 31;
        FuzzAssert.Equal(hash[..saltLength], rehashed[..saltLength], $"Hashing {FuzzAssert.Format(password)} with the salt of {FuzzAssert.Format(hash)} changes the salt");
        FuzzAssert.Equal(verified, string.Equals(hash, rehashed, StringComparison.Ordinal), $"Verify({FuzzAssert.Format(password)}, {FuzzAssert.Format(hash)}) disagrees with HashPassword, which gives {FuzzAssert.Format(rehashed)}");
        FuzzAssert.True(Bcrypt.Verify(password, rehashed), $"Verify rejects the hash {FuzzAssert.Format(rehashed)} of {FuzzAssert.Format(password)}");
    }

    /// <summary>
    /// The first byte selects the revision and the work factor, then come 16 bytes of salt and the password. The hash
    /// keeps the salt, parses back, verifies, does not need a rehash, has the same digest in the revisions 2a, 2b and 2y,
    /// and only depends on the first 72 bytes of the password. "$2x$" is rejected, and so is a password that UTF-8
    /// cannot encode, which Verify rejects too.
    /// </summary>
    private static void BcryptHash(ReadOnlySpan<byte> data)
    {
        var options = FuzzInput.ReadByte(ref data);
        Span<byte> saltBytes = stackalloc byte[16];
        var salt = data[..Math.Min(16, data.Length)];
        salt.CopyTo(saltBytes);
        data = data[salt.Length..];
        var password = FuzzText.Decode(data);

        var version = BcryptVersions[options % BcryptVersions.Length];
        var workFactor = Bcrypt.MinWorkFactor + ((options >> 3) & 0x01);
        var revision = version switch
        {
            BcryptVersion.Revision2 => "2",
            BcryptVersion.Revision2A => "2a",
            BcryptVersion.Revision2B => "2b",
            BcryptVersion.Revision2X => "2x",
            _ => "2y",
        };
        var saltPrefix = $"${revision}${workFactor:D2}$";
        var saltText = saltPrefix + ReferenceImplementations.EncodeBcryptSalt(saltBytes);

        if (version is BcryptVersion.Revision2X)
        {
            AssertThrows<NotSupportedException>(() => Bcrypt.HashPassword(password, saltText), "HashPassword accepted a $2x$ salt");
            AssertThrows<NotSupportedException>(() => Bcrypt.HashPassword(password, workFactor, version), "HashPassword accepted the $2x$ revision");
            return;
        }

        if (!IsValidUtf16(password))
        {
            AssertThrows<ArgumentException>(() => Bcrypt.HashPassword(password, saltText), $"HashPassword accepted the password {FuzzAssert.Format(password)}, which UTF-8 cannot encode");
            return;
        }

        var hash = Bcrypt.HashPassword(password, saltText);
        FuzzAssert.True(hash.StartsWith(saltText, StringComparison.Ordinal), $"The hash {FuzzAssert.Format(hash)} does not start with its salt {FuzzAssert.Format(saltText)}");
        FuzzAssert.Equal(hash, Bcrypt.HashPassword(password.AsSpan(), saltText.AsSpan()), "HashPassword(string) and HashPassword(ReadOnlySpan<char>) disagree");
        FuzzAssert.True(Bcrypt.TryParseHash(hash, out var info), $"TryParseHash rejects the hash {FuzzAssert.Format(hash)}");
        FuzzAssert.Equal(new BcryptHashInfo(version, workFactor), info, $"Parsed information of {FuzzAssert.Format(hash)}");
        FuzzAssert.False(Bcrypt.NeedsRehash(hash, workFactor, version), $"NeedsRehash({FuzzAssert.Format(hash)}) with its own settings");
        FuzzAssert.True(Bcrypt.Verify(password, hash), $"Verify rejects the hash {FuzzAssert.Format(hash)} of {FuzzAssert.Format(password)}");
        FuzzAssert.True(Bcrypt.Verify(password.AsSpan(), hash.AsSpan()), $"Verify(ReadOnlySpan<char>) rejects the hash {FuzzAssert.Format(hash)}");

        // The revisions 2a, 2b and 2y append a NUL byte to the password, then use its first 72 bytes: they only differ by
        // their name. The revision 2 uses the password as it is, repeated to fill the key.
        var passwordLength = Encoding.UTF8.GetByteCount(password);
        if (version is not BcryptVersion.Revision2)
        {
            var digest = hash[saltText.Length..];
            foreach (var other in (string[])["2a", "2b", "2y"])
            {
                var otherHash = Bcrypt.HashPassword(password, $"${other}{saltText[(revision.Length + 1)..]}");
                FuzzAssert.Equal(digest, otherHash[saltText.Length..], $"The digests of {FuzzAssert.Format(password)} in the revisions {revision} and {other} differ");
            }

            FuzzAssert.Equal(passwordLength >= Bcrypt.MaxPasswordLengthInBytes, Bcrypt.Verify(password + "x", hash), $"Verifying {FuzzAssert.Format(password + "x")} against the hash of {FuzzAssert.Format(password)}");
        }
        else if (passwordLength >= Bcrypt.MaxPasswordLengthInBytes)
        {
            FuzzAssert.True(Bcrypt.Verify(password + "x", hash), "The revision 2 uses more than 72 bytes of the password");
        }
    }

    private static void AppendInChunks(Shake shake, byte[] message, byte chunking, bool appendBytes)
    {
        var offset = 0;
        for (var i = 0; offset < message.Length; i++)
        {
            var length = Math.Min(GetChunkLength(chunking, i), message.Length - offset);
            if (appendBytes && length is 1)
            {
                shake.AppendData(message[offset]);
            }
            else
            {
                shake.AppendData(message.AsSpan(offset, length));
            }

            offset += length;
        }
    }

    private static byte[] ReadInChunks(Shake shake, int outputLength, int chunking, bool readBytes)
    {
        var output = new byte[outputLength];
        var offset = 0;
        for (var i = 0; offset < outputLength; i++)
        {
            var length = Math.Min(GetChunkLength((byte)chunking, i), outputLength - offset);
            if (readBytes && length is 1)
            {
                output[offset] = shake.ReadByte();
            }
            else
            {
                shake.Read(output.AsSpan(offset, length));
            }

            offset += length;
        }

        return output;
    }

    /// <summary>Gets the length of the part <paramref name="index"/> of a split: the parts cycle through <see cref="ChunkLengths"/> from a position the input selects.</summary>
    private static int GetChunkLength(byte chunking, int index) => ChunkLengths[(chunking + (index * ((chunking >> 4) | 1))) % ChunkLengths.Length];

    /// <summary>Reads a length (1 byte) and that many bytes, or what remains.</summary>
    private static ReadOnlySpan<byte> ReadField(ref ReadOnlySpan<byte> data)
    {
        var length = Math.Min(FuzzInput.ReadByte(ref data), data.Length);
        var field = data[..length];
        data = data[length..];
        return field;
    }

    /// <summary>
    /// Reads the length of the message (2 bytes, big-endian), then the message: the rest of the input repeated to that
    /// length (zeros when the rest is empty), or the rest as it is when the length is 0.
    /// </summary>
    private static byte[] ReadMessage(ref ReadOnlySpan<byte> data)
    {
        var length = (FuzzInput.ReadByte(ref data) << 8) | FuzzInput.ReadByte(ref data);
        var pattern = data;
        data = default;
        if (length is 0)
            return pattern.ToArray();

        var message = new byte[length];
        if (!pattern.IsEmpty)
        {
            for (var offset = 0; offset < length; offset += pattern.Length)
            {
                pattern[..Math.Min(pattern.Length, length - offset)].CopyTo(message.AsSpan(offset));
            }
        }

        return message;
    }

    private static void AssertEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual, string message)
    {
        if (!expected.SequenceEqual(actual))
        {
            FuzzAssert.Fail($"{message}{Environment.NewLine}Expected: {Convert.ToHexString(expected)}{Environment.NewLine}Actual:   {Convert.ToHexString(actual)}");
        }
    }

    private static void AssertThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        FuzzAssert.Fail(message);
    }

    private static bool IsValidUtf16(string value)
    {
        return System.Text.Unicode.Utf8.FromUtf16(value, new byte[Encoding.UTF8.GetMaxByteCount(value.Length)], out _, out _, replaceInvalidSequences: false) is System.Buffers.OperationStatus.Done;
    }

    /// <summary>Builds a seed of the targets that read option bytes, length-prefixed fields, then a message.</summary>
    private static FuzzSeed Seed(string name, ReadOnlySpan<byte> options, ReadOnlySpan<byte[]> fields, ReadOnlySpan<byte> message, ushort repeatTo = 0)
    {
        var bytes = new List<byte>(options.Length + message.Length + 2);
        bytes.AddRange(options);
        foreach (var field in fields)
        {
            bytes.Add((byte)field.Length);
            bytes.AddRange(field);
        }

        bytes.Add((byte)(repeatTo >> 8));
        bytes.Add((byte)repeatTo);
        bytes.AddRange(message);
        return new FuzzSeed(name, bytes.ToArray());
    }

    private static IEnumerable<FuzzSeed> GetChecksumSeeds()
    {
        yield return Seed("empty", [0], [], []);
        yield return Seed("text", [0x80], [], "The quick brown fox jumps over the lazy dog"u8);
        yield return Seed("vector-blocks", [0x47], [], "0123456789abcdef"u8, 200);
        yield return Seed("wide-vector-blocks", [0x9B], [], "The quick brown fox jumps over the lazy dog"u8, 1000);
        yield return Seed("ones-adler-interval", [0x13], [], [0xFF], 5553);
        yield return Seed("ones-long", [0xFE], [], [0xFF], 65535);
        yield return Seed("zeros", [0x31], [], [], 4096);
    }

    private static IEnumerable<FuzzSeed> GetMessageSeeds()
    {
        for (byte size = 0; size < 4; size++)
        {
            yield return Seed(string.Create(CultureInfo.InvariantCulture, $"empty-{size}"), [size], [], []);
            yield return Seed(string.Create(CultureInfo.InvariantCulture, $"abc-{size}"), [(byte)(size | 0x04)], [], "abc"u8);
            yield return Seed(string.Create(CultureInfo.InvariantCulture, $"long-{size}"), [size], [], [0xA3], 200);
        }
    }

    private static IEnumerable<FuzzSeed> GetShakeSeeds()
    {
        yield return Seed("shake128-empty", [0x00, 32, 0x00], [[], []], []);
        yield return Seed("shake256-abc", [0x07, 64, 0x35], [[], []], "abc"u8);
        yield return Seed("shake128-long-output", [0x30, 0xFF, 0xA7], [[], []], [0xA3], 200);
        yield return Seed("cshake128-email", [0x02, 32, 0x12], [[], "Email Signature"u8.ToArray()], [0x00, 0x01, 0x02, 0x03]);
        yield return Seed("cshake256-function", [0x15, 64, 0x6E], ["KMAC"u8.ToArray(), "My Tagged Application"u8.ToArray()], [0xA3], 1600);
    }

    private static IEnumerable<FuzzSeed> GetKmacSeeds()
    {
        var key = Enumerable.Range(0x40, 32).Select(value => (byte)value).ToArray();
        yield return Seed("kmac128", [0x00, 32], [key, []], [0x00, 0x01, 0x02, 0x03]);
        yield return Seed("kmac128-customization", [0x00, 32], [key, "My Tagged Application"u8.ToArray()], [0x00, 0x01, 0x02, 0x03]);
        yield return Seed("kmac256-long", [0x01, 64], [key, "My Tagged Application"u8.ToArray()], [0xA3], 200);
        yield return Seed("kmac256-empty-key", [0x11, 7], [[], []], "abc"u8);
    }

    private static IEnumerable<FuzzSeed> GetKangarooTwelveSeeds()
    {
        yield return Seed("kt128-empty", [0x00, 32, 0x1F], [[]], []);
        yield return Seed("kt256-customization", [0x01, 64, 0x01], ["customization"u8.ToArray()], "abc"u8);
        yield return Seed("kt128-one-chunk", [0x40, 32, 0x7F], [[]], [0xFB], 8191);
        yield return Seed("kt128-two-chunks", [0x80, 32, 0x0B], [[0x01]], Enumerable.Range(0, 251).Select(value => (byte)value).ToArray(), 8192);
        yield return Seed("kt256-chunks", [0xF1, 0xFF, 0x80], ["C"u8.ToArray()], Enumerable.Range(0, 251).Select(value => (byte)value).ToArray(), 8192 * 3 + 7);
        yield return Seed("turboshake-zero-domain", [0x00, 16, 0x00], [[]], "abc"u8);
    }

    private static IEnumerable<FuzzSeed> GetBlake2bSeeds()
    {
        yield return Seed("empty", [64, 0], [], []);
        yield return Seed("abc", [64, 0x11], [], "abc"u8);
        yield return Seed("blake2b-256", [32, 0x22], [], "The quick brown fox jumps over the lazy dog"u8);
        yield return Seed("one-block", [20, 0x03], [], [0x61], 128);
        yield return Seed("blocks", [1, 0x5C], [], [0x61], 1025);
        yield return Seed("invalid-size", [0, 0], [], "abc"u8);
        yield return Seed("too-large-size", [65, 0], [], "abc"u8);
    }

    private static IEnumerable<FuzzSeed> GetArgon2Seeds()
    {
        // type | version << 2, p, m, t, T, then password, salt, secret and associated data
        var password = Enumerable.Repeat((byte)0x01, 32).ToArray();
        var salt = Enumerable.Repeat((byte)0x02, 16).ToArray();
        var secret = Enumerable.Repeat((byte)0x03, 8).ToArray();
        byte[] associatedData = [0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04];
        foreach (var type in (byte[])[0, 1, 2])
        {
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"rfc9106-{type}"), (byte[])[(byte)(type | 0x04), 4, 32, 3, 32, 32, .. password, 16, .. salt, 8, .. secret, .. associatedData]);
        }

        yield return new FuzzSeed("version10", (byte[])[0x02, 1, 8, 1, 4, 8, .. "password"u8, 8, .. "somesalt"u8, 0]);
        yield return new FuzzSeed("long-tag", (byte[])[0x06, 2, 64, 2, 200, 8, .. "password"u8, 8, .. "somesalt"u8, 0]);
        yield return new FuzzSeed("invalid-memory", (byte[])[0x06, 4, 31, 1, 32, 0, 0, 0]);
        yield return new FuzzSeed("invalid-type", (byte[])[0x07, 1, 8, 1, 32, 0, 0, 0]);
        yield return new FuzzSeed("invalid-version", (byte[])[0xF8, 1, 8, 1, 32, 0, 0, 0]);
        yield return new FuzzSeed("invalid-parallelism", (byte[])[0x06, 0, 8, 1, 32, 0, 0, 0]);
        yield return new FuzzSeed("invalid-passes", (byte[])[0x06, 1, 8, 0, 32, 0, 0, 0]);
        yield return new FuzzSeed("invalid-tag", (byte[])[0x06, 1, 8, 1, 3, 0, 0, 0]);
    }

    private static IEnumerable<FuzzSeed> GetBcryptParseSeeds()
    {
        var index = 0;
        foreach (var hash in GetTestHashes())
        {
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"test-{index}"), [0x07], "password\0" + hash);
            index++;
        }

        var valid = Bcrypt.HashPassword("password", "$2b$04$abcdefghijklmnopqrstuu");
        yield return FuzzSeed.FromText("valid", [0x00], "password\0" + valid);
        yield return FuzzSeed.FromText("wrong-password", [0x40], "drowssap\0" + valid);
        yield return FuzzSeed.FromText("revision-2", [0x00], "password\0" + Bcrypt.HashPassword("password", "$2$05$abcdefghijklmnopqrstuu"));
        yield return FuzzSeed.FromText("revision-2x", [0x60], "password\0$2x" + valid[3..]);
        yield return FuzzSeed.FromText("high-work-factor", [0x1F], "password\0$2b$31" + valid[6..]);
        yield return FuzzSeed.FromText("work-factor-out-of-range", [0x00], "password\0$2b$32" + valid[6..]);
        yield return FuzzSeed.FromText("truncated", [0x00], "password\0" + valid[..^1]);
        yield return FuzzSeed.FromText("unpaired-surrogate", [0x00], "pass\uD800word\0" + valid);
    }

    private static IEnumerable<FuzzSeed> GetBcryptHashSeeds()
    {
        var salt = Enumerable.Range(0, 16).Select(value => (byte)(value * 17)).ToArray();
        for (byte version = 0; version < 5; version++)
        {
            yield return new FuzzSeed(string.Create(CultureInfo.InvariantCulture, $"version-{version}"), (byte[])[version, .. salt, .. FuzzText.Encode("password")]);
        }

        yield return new FuzzSeed("empty-password", (byte[])[0x02, .. salt]);
        yield return new FuzzSeed("work-factor-5", (byte[])[0x0A, .. salt, .. FuzzText.Encode("p@ss w0rd")]);
        yield return new FuzzSeed("unicode", (byte[])[0x04, .. salt, .. FuzzText.Encode("mot de passe 🔒 é")]);
        yield return new FuzzSeed("72-bytes", (byte[])[0x01, .. salt, .. FuzzText.Encode(new string('a', 72))]);
        yield return new FuzzSeed("71-bytes", (byte[])[0x02, .. salt, .. FuzzText.Encode(new string('a', 71))]);
        yield return new FuzzSeed("long-revision-2", (byte[])[0x00, .. salt, .. FuzzText.Encode(new string('b', 100))]);
        yield return new FuzzSeed("unpaired-surrogate", (byte[])[0x02, .. salt, .. FuzzText.Encode("pass\uD800word")]);
    }

    /// <summary>Gets the bcrypt hashes of the unit tests: the string literals that start with "$2".</summary>
    private static IEnumerable<string> GetTestHashes()
    {
        var source = FuzzResources.ReadText(typeof(HashingFuzzTargets).Assembly, "BcryptTests.cs");
        return BcryptHashLiteralRegex().Matches(source).Select(match => match.Groups["value"].Value).Distinct(StringComparer.Ordinal);
    }

    [GeneratedRegex("""(?<!@)"(?<value>\$2[^"\\\r\n]*)"(?!")""", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex BcryptHashLiteralRegex();
}
