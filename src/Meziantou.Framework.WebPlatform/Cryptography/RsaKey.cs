using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// An RSA key (RFC 8017) and the operations of the Web Cryptography API: RSASSA-PKCS1-v1_5, RSA-PSS (with any salt length)
/// and RSA-OAEP (with a label). The padding schemes are implemented here, on top of the RSA primitives computed with
/// <see cref="BigInteger"/>, because the base class library supports neither the OAEP labels nor the PSS salt lengths other
/// than the length of the hash. The private operations use the Chinese remainder theorem.
/// </summary>
/// <remarks>The modular exponentiation of <see cref="BigInteger"/> is not constant-time.</remarks>
internal sealed class RsaKey
{
    private static readonly BigInteger CommonPublicExponent = 65537;

    // The small primes used to sieve the candidates of the key generation
    private static readonly int[] SmallPrimes = GetSmallPrimes(2000);

    private RsaKey(BigInteger n, BigInteger e, BigInteger? d, BigInteger? p, BigInteger? q, BigInteger? dp, BigInteger? dq, BigInteger? qi)
    {
        N = n;
        E = e;
        D = d;
        P = p;
        Q = q;
        DP = dp;
        DQ = dq;
        QI = qi;
        Length = (int)n.GetBitLength();
        ByteLength = (Length + 7) / 8;
    }

    public BigInteger N { get; }

    public BigInteger E { get; }

    public BigInteger? D { get; }

    public BigInteger? P { get; }

    public BigInteger? Q { get; }

    public BigInteger? DP { get; }

    public BigInteger? DQ { get; }

    public BigInteger? QI { get; }

    /// <summary>Gets a value indicating whether the key is a private key.</summary>
    public bool IsPrivate => D is not null;

    /// <summary>Gets the length of the modulus in bits.</summary>
    public int Length { get; }

    /// <summary>Gets the length of the modulus in bytes (k).</summary>
    public int ByteLength { get; }

    /// <summary>Returns the public key of a private key.</summary>
    public RsaKey ToPublicKey() => new(N, E, null, null, null, null, null, null);

    // ----------------------------------------------------------------------------------------------------------------
    // Creation
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Generates a key pair (https://w3c.github.io/webcrypto/#rsa-keygen-params-validation: a modulus of less than 4 bits, or
    /// a public exponent that is less than 3, even, or not less than 2^modulusLength - 1 is an OperationError). The base class
    /// library generates the keys with the common public exponent 65537, and the other ones are generated here.
    /// </summary>
    public static RsaKey Generate(int modulusLength, BigInteger publicExponent)
    {
        if (modulusLength < 4 || publicExponent < 3 || publicExponent.IsEven || publicExponent >= (BigInteger.One << modulusLength) - 1)
            throw WebCryptoException.OperationError("The RSA key generation parameters are invalid");

        // As the browsers, only the moduli of 256 to 16384 bits, multiples of 8, are supported
        if (modulusLength is < 256 or > 16384 || modulusLength % 8 != 0)
            throw WebCryptoException.OperationError("The RSA modulus length is not supported");

        if (publicExponent == CommonPublicExponent)
        {
            using var rsa = RSA.Create();
            if (rsa.LegalKeySizes.Any(size => modulusLength >= size.MinSize && modulusLength <= size.MaxSize && (size.SkipSize == 0 || (modulusLength - size.MinSize) % size.SkipSize == 0)))
            {
                rsa.KeySize = modulusLength;
                var parameters = rsa.ExportParameters(includePrivateParameters: true);
                return new RsaKey(ToInteger(parameters.Modulus!), ToInteger(parameters.Exponent!), ToInteger(parameters.D!), ToInteger(parameters.P!), ToInteger(parameters.Q!), ToInteger(parameters.DP!), ToInteger(parameters.DQ!), ToInteger(parameters.InverseQ!));
            }
        }

        // FIPS 186-5 appendix A.1.3: the primes are random, the two most significant bits set so that the product has
        // modulusLength bits, and p - 1 is coprime with e
        while (true)
        {
            var p = GeneratePrime(modulusLength - (modulusLength / 2), publicExponent);
            var q = GeneratePrime(modulusLength / 2, publicExponent);
            if (p == q)
                continue;

            if (p < q)
            {
                (p, q) = (q, p);
            }

            var n = p * q;
            if (n.GetBitLength() != modulusLength)
                continue;

            var lambda = BigInteger.Divide((p - 1) * (q - 1), BigInteger.GreatestCommonDivisor(p - 1, q - 1));
            var d = ModInverse(publicExponent, lambda);
            return new RsaKey(n, publicExponent, d, p, q, d % (p - 1), d % (q - 1), ModInverse(q, p));
        }
    }

    /// <summary>
    /// Creates a public key or a private key (with the CRT parameters) from its components, and checks that it is a valid key
    /// (a DataError otherwise).
    /// </summary>
    public static RsaKey Create(BigInteger n, BigInteger e, BigInteger? d, BigInteger? p, BigInteger? q, BigInteger? dp, BigInteger? dq, BigInteger? qi)
    {
        var key = new RsaKey(n, e, d, p, q, dp, dq, qi);
        key.Validate();
        return key;
    }

    /// <summary>Creates a key from the big-endian unsigned representations of its components (e.g. the members of a JSON Web Key).</summary>
    public static RsaKey Create(byte[] n, byte[] e, byte[]? d, byte[]? p, byte[]? q, byte[]? dp, byte[]? dq, byte[]? qi)
    {
        if (d is not null && (p is null || q is null || dp is null || dq is null || qi is null))
            throw WebCryptoException.DataError("The RSA private key must have its CRT parameters");

        return Create(ToInteger(n), ToInteger(e), ToNullableInteger(d), ToNullableInteger(p), ToNullableInteger(q), ToNullableInteger(dp), ToNullableInteger(dq), ToNullableInteger(qi));
    }

    /// <summary>Parses the subjectPublicKey of an rsaEncryption SubjectPublicKeyInfo: an RSAPublicKey (RFC 8017 appendix A.1.1).</summary>
    public static RsaKey ParseRsaPublicKey(byte[] data)
    {
        return KeyEncodings.Parse(data, reader =>
        {
            var sequence = reader.ReadSequence();
            var n = KeyEncodings.ReadUnsignedInteger(sequence);
            var e = KeyEncodings.ReadUnsignedInteger(sequence);
            sequence.ThrowIfNotEmpty();
            return Create(n, e, null, null, null, null, null, null);
        });
    }

    /// <summary>Parses the privateKey of an rsaEncryption PrivateKeyInfo: an RSAPrivateKey with two primes (RFC 8017 appendix A.1.2).</summary>
    public static RsaKey ParseRsaPrivateKey(byte[] data)
    {
        return KeyEncodings.Parse(data, reader =>
        {
            var sequence = reader.ReadSequence();
            if (!sequence.TryReadInt32(out var version) || version != 0)
                throw WebCryptoException.DataError("Only the RSA private keys with two primes are supported");

            var n = KeyEncodings.ReadUnsignedInteger(sequence);
            var e = KeyEncodings.ReadUnsignedInteger(sequence);
            var d = KeyEncodings.ReadUnsignedInteger(sequence);
            var p = KeyEncodings.ReadUnsignedInteger(sequence);
            var q = KeyEncodings.ReadUnsignedInteger(sequence);
            var dp = KeyEncodings.ReadUnsignedInteger(sequence);
            var dq = KeyEncodings.ReadUnsignedInteger(sequence);
            var qi = KeyEncodings.ReadUnsignedInteger(sequence);
            sequence.ThrowIfNotEmpty();
            return Create(n, e, d, p, q, dp, dq, qi);
        });
    }

    /// <summary>The DER encoding of the RSAPublicKey of the key.</summary>
    public byte[] EncodeRsaPublicKey()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(N);
            writer.WriteInteger(E);
        }

        return writer.Encode();
    }

    /// <summary>The DER encoding of the RSAPrivateKey of a private key.</summary>
    public byte[] EncodeRsaPrivateKey()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(0);
            writer.WriteInteger(N);
            writer.WriteInteger(E);
            writer.WriteInteger(D!.Value);
            writer.WriteInteger(P!.Value);
            writer.WriteInteger(Q!.Value);
            writer.WriteInteger(DP!.Value);
            writer.WriteInteger(DQ!.Value);
            writer.WriteInteger(QI!.Value);
        }

        return writer.Encode();
    }

    // Checks that the key is a valid RSA key (RFC 8017 section 3): the modulus and the exponent are odd, and the private
    // components are consistent
    private void Validate()
    {
        if (N <= 1 || N.IsEven || E <= 1 || E.IsEven || E >= N)
            throw WebCryptoException.DataError("The RSA public key is invalid");

        if (D is not { } d)
            return;

        if (P is not { } p || Q is not { } q || DP is not { } dp || DQ is not { } dq || QI is not { } qi)
            throw WebCryptoException.DataError("The RSA private key must have its CRT parameters");

        if (p <= 1 || q <= 1 || p * q != N || d <= 0 || d >= N || dp != d % (p - 1) || dq != d % (q - 1) || qi <= 0 || qi >= p || (qi * q) % p != 1 || (E * dp) % (p - 1) != 1 || (E * dq) % (q - 1) != 1)
            throw WebCryptoException.DataError("The RSA private key is invalid");
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Signature and encryption schemes
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>RSASSA-PKCS1-V1_5-SIGN (RFC 8017 section 8.2.1).</summary>
    public byte[] SignPkcs1(string hash, ReadOnlySpan<byte> message)
    {
        var encoded = EncodePkcs1(hash, message, ByteLength);
        return I2Osp(Decrypt(Os2Ip(encoded)), ByteLength);
    }

    /// <summary>RSASSA-PKCS1-V1_5-VERIFY (RFC 8017 section 8.2.2).</summary>
    public bool VerifyPkcs1(string hash, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != ByteLength)
            return false;

        var s = Os2Ip(signature);
        if (s >= N)
            return false;

        var encoded = I2Osp(Encrypt(s), ByteLength);
        byte[] expected;
        try
        {
            expected = EncodePkcs1(hash, message, ByteLength);
        }
        catch (WebCryptoException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(encoded, expected);
    }

    /// <summary>RSASSA-PSS-SIGN (RFC 8017 section 8.1.1), with MGF1 and the hash function.</summary>
    public byte[] SignPss(string hash, ReadOnlySpan<byte> message, int saltLength)
    {
        var encoded = EncodePss(hash, message, Length - 1, saltLength);
        return I2Osp(Decrypt(Os2Ip(encoded)), ByteLength);
    }

    /// <summary>RSASSA-PSS-VERIFY (RFC 8017 section 8.1.2).</summary>
    public bool VerifyPss(string hash, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature, int saltLength)
    {
        if (signature.Length != ByteLength)
            return false;

        var s = Os2Ip(signature);
        if (s >= N)
            return false;

        var emBits = Length - 1;
        var emLength = (emBits + 7) / 8;
        var m = Encrypt(s);
        if (m.GetBitLength() > emLength * 8)
            return false;

        return VerifyPssEncoding(hash, message, I2Osp(m, emLength), emBits, saltLength);
    }

    /// <summary>RSAES-OAEP-ENCRYPT (RFC 8017 section 7.1.1), with MGF1 and the hash function.</summary>
    public byte[] EncryptOaep(string hash, ReadOnlySpan<byte> label, ReadOnlySpan<byte> message)
    {
        var hashLength = Hashes.Digest(hash, []).Length;
        var k = ByteLength;
        if (message.Length > k - (2 * hashLength) - 2)
            throw WebCryptoException.OperationError("The message is too long for the RSA key");

        // EM = 0x00 || maskedSeed || maskedDB, DB = lHash || PS || 0x01 || M
        var db = new byte[k - hashLength - 1];
        Hashes.Digest(hash, label).CopyTo(db, 0);
        db[db.Length - message.Length - 1] = 1;
        message.CopyTo(db.AsSpan(db.Length - message.Length));
        var seed = RandomNumberGenerator.GetBytes(hashLength);
        XorInPlace(db, Mgf1(hash, seed, db.Length));
        XorInPlace(seed, Mgf1(hash, db, hashLength));

        var encoded = new byte[k];
        seed.CopyTo(encoded, 1);
        db.CopyTo(encoded, 1 + hashLength);
        return I2Osp(Encrypt(Os2Ip(encoded)), k);
    }

    /// <summary>RSAES-OAEP-DECRYPT (RFC 8017 section 7.1.2): a decryption error is an OperationError.</summary>
    public byte[] DecryptOaep(string hash, ReadOnlySpan<byte> label, ReadOnlySpan<byte> ciphertext)
    {
        var hashLength = Hashes.Digest(hash, []).Length;
        var k = ByteLength;
        if (ciphertext.Length != k || k < (2 * hashLength) + 2)
            throw WebCryptoException.OperationError("The ciphertext cannot be decrypted");

        var c = Os2Ip(ciphertext);
        if (c >= N)
            throw WebCryptoException.OperationError("The ciphertext cannot be decrypted");

        var encoded = I2Osp(Decrypt(c), k);
        var seed = encoded.AsSpan(1, hashLength).ToArray();
        var db = encoded.AsSpan(1 + hashLength).ToArray();
        XorInPlace(seed, Mgf1(hash, db, hashLength));
        XorInPlace(db, Mgf1(hash, seed, db.Length));

        // The checks are combined so that the error does not tell which one failed
        var invalid = encoded[0] != 0 ? 1 : 0;
        invalid |= CryptographicOperations.FixedTimeEquals(db.AsSpan(0, hashLength), Hashes.Digest(hash, label)) ? 0 : 1;
        var separator = -1;
        for (var i = hashLength; i < db.Length; i++)
        {
            if (separator < 0)
            {
                if (db[i] == 1)
                {
                    separator = i;
                }
                else if (db[i] != 0)
                {
                    invalid = 1;
                }
            }
        }

        if (invalid != 0 || separator < 0)
            throw WebCryptoException.OperationError("The ciphertext cannot be decrypted");

        return db.AsSpan(separator + 1).ToArray();
    }

    // EMSA-PKCS1-v1_5-ENCODE (RFC 8017 section 9.2): EM = 0x00 || 0x01 || PS || 0x00 || DigestInfo
    private static byte[] EncodePkcs1(string hash, ReadOnlySpan<byte> message, int length)
    {
        var digest = Hashes.Digest(hash, message);
        var prefix = hash switch
        {
            "SHA-1" => Convert.FromHexString("3021300906052b0e03021a05000414"),
            "SHA-256" => Convert.FromHexString("3031300d060960864801650304020105000420"),
            "SHA-384" => Convert.FromHexString("3041300d060960864801650304020205000430"),
            "SHA-512" => Convert.FromHexString("3051300d060960864801650304020305000440"),
            "SHA3-256" => Convert.FromHexString("3031300d060960864801650304020805000420"),
            "SHA3-384" => Convert.FromHexString("3041300d060960864801650304020905000430"),
            "SHA3-512" => Convert.FromHexString("3051300d060960864801650304020a05000440"),
            _ => throw new NotSupportedException($"The hash function {hash} is not supported"),
        };

        var tLength = prefix.Length + digest.Length;
        if (length < tLength + 11)
            throw WebCryptoException.OperationError("The RSA key is too short for the hash function");

        var encoded = new byte[length];
        encoded[1] = 1;
        encoded.AsSpan(2, length - tLength - 3).Fill(0xFF);
        prefix.CopyTo(encoded, length - tLength);
        digest.CopyTo(encoded, length - digest.Length);
        return encoded;
    }

    // EMSA-PSS-ENCODE (RFC 8017 section 9.1.1)
    private static byte[] EncodePss(string hash, ReadOnlySpan<byte> message, int emBits, int saltLength)
    {
        var mHash = Hashes.Digest(hash, message);
        var hashLength = mHash.Length;
        var emLength = (emBits + 7) / 8;
        if (saltLength < 0 || emLength < hashLength + saltLength + 2)
            throw WebCryptoException.OperationError("The salt is too long for the RSA key");

        var salt = RandomNumberGenerator.GetBytes(saltLength);
        var mPrime = new byte[8 + hashLength + saltLength];
        mHash.CopyTo(mPrime, 8);
        salt.CopyTo(mPrime, 8 + hashLength);
        var h = Hashes.Digest(hash, mPrime);

        // DB = PS || 0x01 || salt
        var db = new byte[emLength - hashLength - 1];
        db[db.Length - saltLength - 1] = 1;
        salt.CopyTo(db, db.Length - saltLength);
        XorInPlace(db, Mgf1(hash, h, db.Length));
        db[0] &= (byte)(0xFF >> ((8 * emLength) - emBits));

        var encoded = new byte[emLength];
        db.CopyTo(encoded, 0);
        h.CopyTo(encoded, db.Length);
        encoded[^1] = 0xBC;
        return encoded;
    }

    // EMSA-PSS-VERIFY (RFC 8017 section 9.1.2)
    private static bool VerifyPssEncoding(string hash, ReadOnlySpan<byte> message, byte[] encoded, int emBits, int saltLength)
    {
        var mHash = Hashes.Digest(hash, message);
        var hashLength = mHash.Length;
        var emLength = encoded.Length;
        if (saltLength < 0 || emLength < hashLength + saltLength + 2 || encoded[^1] != 0xBC)
            return false;

        var db = encoded.AsSpan(0, emLength - hashLength - 1).ToArray();
        var h = encoded.AsSpan(emLength - hashLength - 1, hashLength);
        var topMask = (byte)(0xFF << (8 - ((8 * emLength) - emBits)));
        if ((8 * emLength) - emBits > 0 && (db[0] & topMask) != 0)
            return false;

        XorInPlace(db, Mgf1(hash, h, db.Length));
        db[0] &= (byte)(0xFF >> ((8 * emLength) - emBits));
        var psLength = emLength - hashLength - saltLength - 2;
        for (var i = 0; i < psLength; i++)
        {
            if (db[i] != 0)
                return false;
        }

        if (db[psLength] != 1)
            return false;

        var mPrime = new byte[8 + hashLength + saltLength];
        mHash.CopyTo(mPrime, 8);
        db.AsSpan(db.Length - saltLength).CopyTo(mPrime.AsSpan(8 + hashLength));
        return CryptographicOperations.FixedTimeEquals(Hashes.Digest(hash, mPrime), h);
    }

    // MGF1 (RFC 8017 appendix B.2.1)
    private static byte[] Mgf1(string hash, ReadOnlySpan<byte> seed, int length)
    {
        var output = new byte[length];
        var input = new byte[seed.Length + 4];
        seed.CopyTo(input);
        var offset = 0;
        for (uint counter = 0; offset < length; counter++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(input.AsSpan(seed.Length), counter);
            var digest = Hashes.Digest(hash, input);
            var count = Math.Min(digest.Length, length - offset);
            digest.AsSpan(0, count).CopyTo(output.AsSpan(offset));
            offset += count;
        }

        return output;
    }

    private static void XorInPlace(Span<byte> target, ReadOnlySpan<byte> mask)
    {
        for (var i = 0; i < target.Length; i++)
        {
            target[i] ^= mask[i];
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Primitives
    // ----------------------------------------------------------------------------------------------------------------

    // RSAEP / RSAVP1 (RFC 8017 sections 5.1.1 and 5.2.2)
    private BigInteger Encrypt(BigInteger m) => BigInteger.ModPow(m, E, N);

    // RSADP / RSASP1 (RFC 8017 sections 5.1.2 and 5.2.1), with the Chinese remainder theorem
    private BigInteger Decrypt(BigInteger c)
    {
        if (D is null)
            throw WebCryptoException.OperationError("The key is not a private key");

        if (c >= N)
            throw WebCryptoException.OperationError("The message representative is out of range");

        var p = P!.Value;
        var q = Q!.Value;
        var m1 = BigInteger.ModPow(c % p, DP!.Value, p);
        var m2 = BigInteger.ModPow(c % q, DQ!.Value, q);
        var h = (QI!.Value * (m1 - m2)) % p;
        if (h.Sign < 0)
        {
            h += p;
        }

        return m2 + (h * q);
    }

    /// <summary>OS2IP (RFC 8017 section 4.2): a big-endian unsigned integer.</summary>
    public static BigInteger Os2Ip(ReadOnlySpan<byte> bytes) => new(bytes, isUnsigned: true, isBigEndian: true);

    /// <summary>I2OSP (RFC 8017 section 4.1): the big-endian representation of an integer with the given length.</summary>
    public static byte[] I2Osp(BigInteger value, int length)
    {
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: true);
        if (value.IsZero)
        {
            bytes = [];
        }

        if (bytes.Length > length)
            throw WebCryptoException.OperationError("The integer is too large");

        var result = new byte[length];
        bytes.CopyTo(result, length - bytes.Length);
        return result;
    }

    /// <summary>The minimal big-endian unsigned representation of an integer (one byte for 0), as the BigInteger typedef and base64urlUInt.</summary>
    public static byte[] ToMinimalBytes(BigInteger value) => value.IsZero ? [0] : value.ToByteArray(isUnsigned: true, isBigEndian: true);

    private static BigInteger ToInteger(byte[] bytes) => new(bytes, isUnsigned: true, isBigEndian: true);

    private static BigInteger? ToNullableInteger(byte[]? bytes) => bytes is null ? null : ToInteger(bytes);

    // The modular inverse with the extended Euclidean algorithm
    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        BigInteger oldR = a % m, r = m;
        BigInteger oldS = 1, s = 0;
        while (!r.IsZero)
        {
            var quotient = oldR / r;
            (oldR, r) = (r, oldR - (quotient * r));
            (oldS, s) = (s, oldS - (quotient * s));
        }

        if (oldR != 1)
            throw WebCryptoException.OperationError("The value is not invertible");

        return ((oldS % m) + m) % m;
    }

    private static BigInteger GeneratePrime(int bits, BigInteger publicExponent)
    {
        var bytes = new byte[(bits + 7) / 8];
        while (true)
        {
            RandomNumberGenerator.Fill(bytes);
            var excessBits = (bytes.Length * 8) - bits;
            bytes[0] &= (byte)(0xFF >> excessBits);
            bytes[0] |= (byte)(0xC0 >> excessBits);
            if (excessBits == 7)
            {
                // The two most significant bits span two bytes
                bytes[1] |= 0x80;
            }

            bytes[^1] |= 1;
            var candidate = ToInteger(bytes);
            if (!IsProbablePrime(candidate))
                continue;

            if (BigInteger.GreatestCommonDivisor(candidate - 1, publicExponent) == 1)
                return candidate;
        }
    }

    // Trial division by the small primes, then the Miller-Rabin test (FIPS 186-5 appendix B.3)
    private static bool IsProbablePrime(BigInteger n)
    {
        foreach (var prime in SmallPrimes)
        {
            if (n == prime)
                return true;

            if ((n % prime).IsZero)
                return false;
        }

        var d = n - 1;
        var r = 0;
        while (d.IsEven)
        {
            d >>= 1;
            r++;
        }

        var rounds = n.GetBitLength() >= 1024 ? 8 : 16;
        var bytes = new byte[n.GetByteCount(isUnsigned: true)];
        for (var i = 0; i < rounds; i++)
        {
            BigInteger a;
            do
            {
                RandomNumberGenerator.Fill(bytes);
                a = ToInteger(bytes) % n;
            }
            while (a < 2 || a > n - 2);

            var x = BigInteger.ModPow(a, d, n);
            if (x.IsOne || x == n - 1)
                continue;

            var composite = true;
            for (var j = 1; j < r; j++)
            {
                x = BigInteger.ModPow(x, 2, n);
                if (x == n - 1)
                {
                    composite = false;
                    break;
                }
            }

            if (composite)
                return false;
        }

        return true;
    }

    private static int[] GetSmallPrimes(int limit)
    {
        var sieve = new bool[limit];
        var primes = new List<int>();
        for (var i = 2; i < limit; i++)
        {
            if (sieve[i])
                continue;

            primes.Add(i);
            for (var j = i * i; j < limit; j += i)
            {
                sieve[j] = true;
            }
        }

        return [.. primes];
    }
}
