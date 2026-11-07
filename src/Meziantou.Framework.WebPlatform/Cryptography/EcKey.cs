using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// A key on a NIST prime curve (P-256, P-384, P-521) and the ECDSA and ECDH operations of the Web Cryptography API
/// (https://w3c.github.io/webcrypto/#ecdsa). The point validation, the decompression and the derivation of the public key
/// are computed here; the signatures and the key agreements use the base class library.
/// </summary>
internal sealed class EcKey
{
    private EcKey(EcCurve curve, byte[] x, byte[] y, byte[]? d)
    {
        Curve = curve;
        X = x;
        Y = y;
        D = d;
    }

    public EcCurve Curve { get; }

    /// <summary>Gets the x coordinate of the public point (the length of the field).</summary>
    public byte[] X { get; }

    /// <summary>Gets the y coordinate of the public point (the length of the field).</summary>
    public byte[] Y { get; }

    /// <summary>Gets the private scalar (the length of the order), or null for a public key.</summary>
    public byte[]? D { get; }

    public bool IsPrivate => D is not null;

    public EcKey ToPublicKey() => new(Curve, X, Y, null);

    /// <summary>Restores a key created by this class (e.g. from the handle of a CryptoKey): the point is not validated again.</summary>
    public static EcKey Restore(EcCurve curve, byte[] x, byte[] y, byte[]? d) => new(curve, x, y, d);

    /// <summary>Generates a key pair.</summary>
    public static EcKey Generate(EcCurve curve)
    {
        using var ecdsa = ECDsa.Create(curve.BclCurve);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: true);
        return new EcKey(curve, Pad(parameters.Q.X!, curve.FieldLength), Pad(parameters.Q.Y!, curve.FieldLength), Pad(parameters.D!, curve.OrderLength));
    }

    /// <summary>Creates a public key from its coordinates: the point must be on the curve (a DataError otherwise).</summary>
    public static EcKey CreatePublic(EcCurve curve, byte[] x, byte[] y)
    {
        if (x.Length != curve.FieldLength || y.Length != curve.FieldLength)
            throw WebCryptoException.DataError("The coordinates do not have the length of the field");

        var point = (ToInteger(x), ToInteger(y));
        if (!curve.IsOnCurve(point))
            throw WebCryptoException.DataError("The point is not on the curve");

        return new EcKey(curve, x, y, null);
    }

    /// <summary>
    /// Creates a private key from its scalar (1 to n - 1), and the public point when it is known (it must be the public key
    /// of the scalar), else derives it.
    /// </summary>
    public static EcKey CreatePrivate(EcCurve curve, byte[] d, byte[]? x, byte[]? y)
    {
        var scalar = ToInteger(d);
        if (scalar <= 0 || scalar >= curve.N)
            throw WebCryptoException.DataError("The private key is out of range");

        var publicPoint = curve.Multiply(scalar, curve.G) ?? throw WebCryptoException.DataError("The private key is invalid");
        var publicX = I2Osp(publicPoint.X, curve.FieldLength);
        var publicY = I2Osp(publicPoint.Y, curve.FieldLength);
        if (x is not null && y is not null && (!x.AsSpan().SequenceEqual(publicX) || !y.AsSpan().SequenceEqual(publicY)))
            throw WebCryptoException.DataError("The public key does not match the private key");

        return new EcKey(curve, publicX, publicY, I2Osp(scalar, curve.OrderLength));
    }

    /// <summary>
    /// Decodes a point (SEC 1 section 2.3.4): the uncompressed and the compressed forms are supported, the identity point
    /// and the decoding errors are DataErrors.
    /// </summary>
    public static EcKey DecodePublicPoint(EcCurve curve, ReadOnlySpan<byte> data)
    {
        var length = curve.FieldLength;
        if (data.Length == (2 * length) + 1 && data[0] == 4)
            return CreatePublic(curve, data.Slice(1, length).ToArray(), data.Slice(1 + length).ToArray());

        if (data.Length == length + 1 && data[0] is 2 or 3)
        {
            var x = ToInteger(data[1..]);
            if (x >= curve.P)
                throw WebCryptoException.DataError("The compressed point is invalid");

            var y = curve.DecompressY(x, data[0] == 3) ?? throw WebCryptoException.DataError("The compressed point is not on the curve");
            return new EcKey(curve, I2Osp(x, length), I2Osp(y, length), null);
        }

        throw WebCryptoException.DataError("The point encoding is invalid");
    }

    /// <summary>The uncompressed form of the public point (SEC 1 section 2.3.3).</summary>
    public byte[] EncodePublicPoint()
    {
        var result = new byte[1 + X.Length + Y.Length];
        result[0] = 4;
        X.CopyTo(result, 1);
        Y.CopyTo(result, 1 + X.Length);
        return result;
    }

    /// <summary>Parses an id-ecPublicKey SubjectPublicKeyInfo (RFC 5480): returns null when the curve is not supported.</summary>
    public static EcKey? ParseSubjectPublicKeyInfo(SubjectPublicKeyInfo spki)
    {
        if (spki.Algorithm != KeyEncodings.EcPublicKey)
            throw WebCryptoException.DataError("The algorithm of the key is not id-ecPublicKey");

        var curve = GetNamedCurve(spki.Parameters);
        return curve is null ? null : DecodePublicPoint(curve, spki.PublicKey);
    }

    /// <summary>Parses an id-ecPublicKey PrivateKeyInfo with an ECPrivateKey (RFC 5915 section 3): returns null when the curve is not supported.</summary>
    public static EcKey? ParsePrivateKeyInfo(PrivateKeyInfo privateKeyInfo)
    {
        if (privateKeyInfo.Algorithm != KeyEncodings.EcPublicKey)
            throw WebCryptoException.DataError("The algorithm of the key is not id-ecPublicKey");

        var curve = GetNamedCurve(privateKeyInfo.Parameters);
        if (curve is null)
            return null;

        return KeyEncodings.Parse(privateKeyInfo.PrivateKey, reader =>
        {
            var sequence = reader.ReadSequence();
            if (!sequence.TryReadInt32(out var version) || version != 1)
                throw WebCryptoException.DataError("The version of the ECPrivateKey is not supported");

            var d = sequence.ReadOctetString();
            var parametersTag = new Asn1Tag(TagClass.ContextSpecific, 0);
            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(parametersTag))
            {
                var parameters = sequence.ReadSequence(parametersTag);
                var oid = parameters.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier) ? parameters.ReadObjectIdentifier() : null;
                if (oid is null || oid != curve.Oid)
                    throw WebCryptoException.DataError("The parameters of the ECPrivateKey do not match the algorithm");

                parameters.ThrowIfNotEmpty();
            }

            EcKey? publicKey = null;
            var publicKeyTag = new Asn1Tag(TagClass.ContextSpecific, 1);
            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(publicKeyTag))
            {
                var publicKeySequence = sequence.ReadSequence(publicKeyTag);
                var point = publicKeySequence.ReadBitString(out var unusedBitCount);
                publicKeySequence.ThrowIfNotEmpty();
                if (unusedBitCount != 0)
                    throw WebCryptoException.DataError("The public key of the ECPrivateKey is invalid");

                publicKey = DecodePublicPoint(curve, point);
            }

            sequence.ThrowIfNotEmpty();
            return CreatePrivate(curve, d, publicKey?.X, publicKey?.Y);
        });
    }

    /// <summary>The id-ecPublicKey SubjectPublicKeyInfo of the public key, with the uncompressed point.</summary>
    public byte[] EncodeSubjectPublicKeyInfo() => KeyEncodings.EncodeSubjectPublicKeyInfo(KeyEncodings.EcPublicKey, KeyEncodings.EncodeObjectIdentifier(Curve.Oid), EncodePublicPoint());

    /// <summary>
    /// The id-ecPublicKey PrivateKeyInfo of the private key: an ECPrivateKey with the public key. As in the browsers (and the
    /// WPT round trips), the parameters of the ECPrivateKey are omitted: they are the ones of the PrivateKeyInfo.
    /// </summary>
    public byte[] EncodePrivateKeyInfo()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(1);
            writer.WriteOctetString(D);
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                writer.WriteBitString(EncodePublicPoint());
            }
        }

        return KeyEncodings.EncodePrivateKeyInfo(KeyEncodings.EcPublicKey, KeyEncodings.EncodeObjectIdentifier(Curve.Oid), writer.Encode());
    }

    /// <summary>The ECDSA signature (r || s, each the length of the order) of the digest of the message.</summary>
    public byte[] Sign(string hash, ReadOnlySpan<byte> message)
    {
        using var ecdsa = ECDsa.Create(ToParameters());
        return ecdsa.SignHash(Hashes.Digest(hash, message), DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>Verifies an ECDSA signature: a signature that does not have the length of r || s is not valid.</summary>
    public bool Verify(string hash, ReadOnlySpan<byte> message, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != 2 * Curve.OrderLength)
            return false;

        using var ecdsa = ECDsa.Create(ToParameters());
        try
        {
            return ecdsa.VerifyHash(Hashes.Digest(hash, message), signature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>The ECDH shared secret (RFC 6090 section 4): the x coordinate of d * Q, the length of the field.</summary>
    public byte[] DeriveSharedSecret(EcKey publicKey)
    {
        using var ecdh = ECDiffieHellman.Create(ToParameters());
        using var peer = ECDiffieHellman.Create(publicKey.ToParameters());
        return ecdh.DeriveRawSecretAgreement(peer.PublicKey);
    }

    private ECParameters ToParameters() => new()
    {
        Curve = Curve.BclCurve,
        Q = new ECPoint { X = X, Y = Y },
        D = D,
    };

    /// <summary>The curve of the namedCurve choice of ECParameters (RFC 5480 section 2.1.1), or null when the curve is not supported.</summary>
    private static EcCurve? GetNamedCurve(byte[]? parameters)
    {
        if (parameters is null)
            throw WebCryptoException.DataError("The parameters of the key are missing");

        var oid = KeyEncodings.TryDecodeObjectIdentifier(parameters) ?? throw WebCryptoException.DataError("The parameters of the key are not a named curve");
        return EcCurve.FromOid(oid);
    }

    private static byte[] Pad(byte[] value, int length)
    {
        if (value.Length == length)
            return value;

        var result = new byte[length];
        value.CopyTo(result, length - value.Length);
        return result;
    }

    private static BigInteger ToInteger(ReadOnlySpan<byte> bytes) => new(bytes, isUnsigned: true, isBigEndian: true);

    private static byte[] I2Osp(BigInteger value, int length) => RsaKey.I2Osp(value, length);
}

/// <summary>A NIST prime curve y^2 = x^3 - 3x + b over GF(p) (FIPS 186-5, SP 800-186), with the arithmetic used to validate and derive the points.</summary>
internal sealed class EcCurve
{
    public static readonly EcCurve P256 = new("P-256", KeyEncodings.Secp256r1, ECCurve.NamedCurves.nistP256,
        "ffffffff00000001000000000000000000000000ffffffffffffffffffffffff",
        "5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b",
        "6b17d1f2e12c4247f8bce6e563a440f277037d812deb33a0f4a13945d898c296",
        "4fe342e2fe1a7f9b8ee7eb4a7c0f9e162bce33576b315ececbb6406837bf51f5",
        "ffffffff00000000ffffffffffffffffbce6faada7179e84f3b9cac2fc632551");

    public static readonly EcCurve P384 = new("P-384", KeyEncodings.Secp384r1, ECCurve.NamedCurves.nistP384,
        "fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffeffffffff0000000000000000ffffffff",
        "b3312fa7e23ee7e4988e056be3f82d19181d9c6efe8141120314088f5013875ac656398d8a2ed19d2a85c8edd3ec2aef",
        "aa87ca22be8b05378eb1c71ef320ad746e1d3b628ba79b9859f741e082542a385502f25dbf55296c3a545e3872760ab7",
        "3617de4a96262c6f5d9e98bf9292dc29f8f41dbd289a147ce9da3113b5f0b8c00a60b1ce1d7e819d7a431d7c90ea0e5f",
        "ffffffffffffffffffffffffffffffffffffffffffffffffc7634d81f4372ddf581a0db248b0a77aecec196accc52973");

    public static readonly EcCurve P521 = new("P-521", KeyEncodings.Secp521r1, ECCurve.NamedCurves.nistP521,
        "01ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
        "0051953eb9618e1c9a1f929a21a0b68540eea2da725b99b315f3b8b489918ef109e156193951ec7e937b1652c0bd3bb1bf073573df883d2c34f1ef451fd46b503f00",
        "00c6858e06b70404e9cd9e3ecb662395b4429c648139053fb521f828af606b4d3dbaa14b5e77efe75928fe1dc127a2ffa8de3348b3c1856a429bf97e7e31c2e5bd66",
        "011839296a789a3bc0045c8a5fb42c7d1bd998f54449579b446817afbd17273e662c97ee72995ef42640c550b9013fad0761353c7086a272c24088be94769fd16650",
        "01fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffa51868783bf2f966b7fcc0148f709a5d03bb5c9b8899c47aebb6fb71e91386409");

    private EcCurve(string name, string oid, ECCurve bclCurve, string p, string b, string gx, string gy, string n)
    {
        Name = name;
        Oid = oid;
        BclCurve = bclCurve;
        P = Parse(p);
        B = Parse(b);
        G = (Parse(gx), Parse(gy));
        N = Parse(n);
        FieldLength = (int)((P.GetBitLength() + 7) / 8);
        OrderLength = (int)((N.GetBitLength() + 7) / 8);
    }

    /// <summary>Gets the name of the curve in the Web Cryptography API (the NamedCurve values).</summary>
    public string Name { get; }

    /// <summary>Gets the object identifier of the curve (RFC 5480 section 2.1.1.1).</summary>
    public string Oid { get; }

    public ECCurve BclCurve { get; }

    public BigInteger P { get; }

    public BigInteger B { get; }

    public (BigInteger X, BigInteger Y) G { get; }

    public BigInteger N { get; }

    public int FieldLength { get; }

    public int OrderLength { get; }

    public static EcCurve? FromName(string name) => name switch
    {
        "P-256" => P256,
        "P-384" => P384,
        "P-521" => P521,
        _ => null,
    };

    public static EcCurve? FromOid(string oid) => oid switch
    {
        KeyEncodings.Secp256r1 => P256,
        KeyEncodings.Secp384r1 => P384,
        KeyEncodings.Secp521r1 => P521,
        _ => null,
    };

    /// <summary>Whether a point (with coordinates in [0, p)) is on the curve: y^2 = x^3 - 3x + b.</summary>
    public bool IsOnCurve((BigInteger X, BigInteger Y) point)
    {
        var (x, y) = point;
        if (x.Sign < 0 || x >= P || y.Sign < 0 || y >= P)
            return false;

        return Mod((y * y) - ((x * x * x) - (3 * x) + B)).IsZero;
    }

    /// <summary>The y coordinate of a compressed point, or null when x is not the coordinate of a point of the curve.</summary>
    public BigInteger? DecompressY(BigInteger x, bool odd)
    {
        // p = 3 mod 4 for the three curves: sqrt(a) = a^((p + 1) / 4)
        var alpha = Mod((x * x * x) - (3 * x) + B);
        var beta = BigInteger.ModPow(alpha, (P + 1) / 4, P);
        if (Mod(beta * beta) != alpha)
            return null;

        if (beta.IsEven == odd)
        {
            beta = Mod(-beta);
        }

        if (beta.IsZero && odd)
            return null;

        return beta;
    }

    /// <summary>k * point in affine coordinates, or null for the point at infinity (double-and-add in Jacobian coordinates).</summary>
    public (BigInteger X, BigInteger Y)? Multiply(BigInteger k, (BigInteger X, BigInteger Y) point)
    {
        (BigInteger X, BigInteger Y, BigInteger Z) result = (0, 1, 0);
        var addend = (point.X, point.Y, BigInteger.One);
        for (var i = (int)k.GetBitLength() - 1; i >= 0; i--)
        {
            result = Double(result);
            if (!(k >> i).IsEven)
            {
                result = Add(result, addend);
            }
        }

        return ToAffine(result);
    }

    /// <summary>
    /// d * G for a secret scalar d in [1, n), with additive scalar blinding: d * G = (d - r) * G + R, where (r, R) is a key
    /// pair generated by the base class library (constant time). The variable-time arithmetic of this class only sees the
    /// blinded scalar d - r mod n, which is uniformly random and independent of d.
    /// </summary>
    /// <remarks>
    /// On macOS, the base class library derives the public point of an imported private scalar through the legacy keychain
    /// (about 50 ms for P-256 and 90 ms for P-384), while generating a key pair takes less than a millisecond.
    /// </remarks>
    public (BigInteger X, BigInteger Y) MultiplyGeneratorBlinded(ReadOnlySpan<byte> scalar)
    {
        using var blinding = ECDiffieHellman.Create(BclCurve);
        var blindingParameters = blinding.ExportParameters(includePrivateParameters: true);
        try
        {
            var r = new BigInteger(blindingParameters.D, isUnsigned: true, isBigEndian: true);
            var blindingPoint = (new BigInteger(blindingParameters.Q.X, isUnsigned: true, isBigEndian: true), new BigInteger(blindingParameters.Q.Y, isUnsigned: true, isBigEndian: true));
            var blindedScalar = (new BigInteger(scalar, isUnsigned: true, isBigEndian: true) - r) % N;
            if (blindedScalar.Sign < 0)
            {
                blindedScalar += N;
            }

            // (d - r) * G is the point at infinity when d = r; d * G is never the point at infinity as 0 < d < n
            if (Multiply(blindedScalar, G) is not { } blindedPoint)
                return blindingPoint;

            return Add(blindedPoint, blindingPoint) ?? throw new CryptographicException("The scalar is out of range.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blindingParameters.D);
        }
    }

    /// <summary>p + q in affine coordinates, or null for the point at infinity.</summary>
    public (BigInteger X, BigInteger Y)? Add((BigInteger X, BigInteger Y) p, (BigInteger X, BigInteger Y) q)
    {
        return ToAffine(Add((p.X, p.Y, BigInteger.One), (q.X, q.Y, BigInteger.One)));
    }

    private (BigInteger X, BigInteger Y)? ToAffine((BigInteger X, BigInteger Y, BigInteger Z) point)
    {
        if (point.Z.IsZero)
            return null;

        var zInverse = BigInteger.ModPow(point.Z, P - 2, P);
        var zInverse2 = Mod(zInverse * zInverse);
        return (Mod(point.X * zInverse2), Mod(point.Y * zInverse2 * zInverse));
    }

    // Point doubling in Jacobian coordinates for a = -3
    private (BigInteger X, BigInteger Y, BigInteger Z) Double((BigInteger X, BigInteger Y, BigInteger Z) p)
    {
        if (p.Z.IsZero || p.Y.IsZero)
            return (0, 1, 0);

        var delta = Mod(p.Z * p.Z);
        var gamma = Mod(p.Y * p.Y);
        var beta = Mod(p.X * gamma);
        var alpha = Mod(3 * (p.X - delta) * (p.X + delta));
        var x = Mod((alpha * alpha) - (8 * beta));
        var z = Mod(((p.Y + p.Z) * (p.Y + p.Z)) - gamma - delta);
        var y = Mod((alpha * ((4 * beta) - x)) - (8 * gamma * gamma));
        return (x, y, z);
    }

    // Point addition in Jacobian coordinates
    private (BigInteger X, BigInteger Y, BigInteger Z) Add((BigInteger X, BigInteger Y, BigInteger Z) p, (BigInteger X, BigInteger Y, BigInteger Z) q)
    {
        if (p.Z.IsZero)
            return q;

        if (q.Z.IsZero)
            return p;

        var z1z1 = Mod(p.Z * p.Z);
        var z2z2 = Mod(q.Z * q.Z);
        var u1 = Mod(p.X * z2z2);
        var u2 = Mod(q.X * z1z1);
        var s1 = Mod(p.Y * q.Z * z2z2);
        var s2 = Mod(q.Y * p.Z * z1z1);
        if (u1 == u2)
            return s1 == s2 ? Double(p) : (0, 1, 0);

        var h = Mod(u2 - u1);
        var i = Mod(4 * h * h);
        var j = Mod(h * i);
        var r = Mod(2 * (s2 - s1));
        var v = Mod(u1 * i);
        var x = Mod((r * r) - j - (2 * v));
        var y = Mod((r * (v - x)) - (2 * s1 * j));
        var z = Mod((((p.Z + q.Z) * (p.Z + q.Z)) - z1z1 - z2z2) * h);
        return (x, y, z);
    }

    private BigInteger Mod(BigInteger value)
    {
        var result = value % P;
        return result.Sign < 0 ? result + P : result;
    }

    private static BigInteger Parse(string hex) => new(Convert.FromHexString(hex), isUnsigned: true, isBigEndian: true);
}
