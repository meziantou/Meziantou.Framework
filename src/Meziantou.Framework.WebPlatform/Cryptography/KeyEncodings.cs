using System.Formats.Asn1;
using System.Numerics;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>A SubjectPublicKeyInfo structure (RFC 5280 section 4.1.2.7): the algorithm and the public key.</summary>
/// <param name="Algorithm">The object identifier of the algorithm.</param>
/// <param name="Parameters">The DER encoding of the parameters of the algorithm, or null when they are absent.</param>
/// <param name="PublicKey">The bits of the public key.</param>
internal sealed record SubjectPublicKeyInfo(string Algorithm, byte[]? Parameters, byte[] PublicKey);

/// <summary>A PrivateKeyInfo structure (RFC 5208 section 5, or OneAsymmetricKey of RFC 5958): the algorithm and the private key.</summary>
/// <param name="Algorithm">The object identifier of the algorithm.</param>
/// <param name="Parameters">The DER encoding of the parameters of the algorithm, or null when they are absent.</param>
/// <param name="PrivateKey">The content of the privateKey octet string.</param>
internal sealed record PrivateKeyInfo(string Algorithm, byte[]? Parameters, byte[] PrivateKey);

/// <summary>
/// The DER encoding of the key structures of the Web Cryptography API (https://w3c.github.io/webcrypto/#terminology: the
/// structures are parsed with the Distinguished Encoding Rules, and all the bytes of the data must be consumed). The parse
/// errors are <c>DataError</c>s.
/// </summary>
internal static class KeyEncodings
{
    public const string RsaEncryption = "1.2.840.113549.1.1.1";
    public const string EcPublicKey = "1.2.840.10045.2.1";
    public const string Secp256r1 = "1.2.840.10045.3.1.7";
    public const string Secp384r1 = "1.3.132.0.34";
    public const string Secp521r1 = "1.3.132.0.35";
    public const string X25519 = "1.3.101.110";
    public const string X448 = "1.3.101.111";
    public const string Ed25519 = "1.3.101.112";
    public const string Ed448 = "1.3.101.113";

    /// <summary>Parses a SubjectPublicKeyInfo structure (https://w3c.github.io/webcrypto/#concept-parse-an-spki).</summary>
    public static SubjectPublicKeyInfo ParseSubjectPublicKeyInfo(byte[] data)
    {
        return Parse(data, reader =>
        {
            var sequence = reader.ReadSequence();
            var (algorithm, parameters) = ReadAlgorithmIdentifier(sequence);
            var publicKey = sequence.ReadBitString(out var unusedBitCount);
            if (unusedBitCount != 0)
                throw WebCryptoException.DataError("The public key is not a whole number of bytes");

            sequence.ThrowIfNotEmpty();
            return new SubjectPublicKeyInfo(algorithm, parameters, publicKey);
        });
    }

    /// <summary>Parses a PrivateKeyInfo structure (https://w3c.github.io/webcrypto/#concept-parse-a-privateKeyInfo).</summary>
    public static PrivateKeyInfo ParsePrivateKeyInfo(byte[] data)
    {
        return Parse(data, reader =>
        {
            var sequence = reader.ReadSequence();
            if (!sequence.TryReadInt32(out var version) || version is not (0 or 1))
                throw WebCryptoException.DataError("The version of the PrivateKeyInfo is not supported");

            var (algorithm, parameters) = ReadAlgorithmIdentifier(sequence);
            var privateKey = sequence.ReadOctetString();

            // [0] attributes and [1] publicKey (OneAsymmetricKey) are ignored
            if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                sequence.ReadEncodedValue();
            }

            if (version == 1 && sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
            {
                sequence.ReadEncodedValue();
            }

            sequence.ThrowIfNotEmpty();
            return new PrivateKeyInfo(algorithm, parameters, privateKey);
        });
    }

    /// <summary>The DER encoding of a SubjectPublicKeyInfo structure.</summary>
    public static byte[] EncodeSubjectPublicKeyInfo(string algorithm, byte[]? parameters, ReadOnlySpan<byte> publicKey)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            WriteAlgorithmIdentifier(writer, algorithm, parameters);
            writer.WriteBitString(publicKey);
        }

        return writer.Encode();
    }

    /// <summary>The DER encoding of a PrivateKeyInfo structure (version 0).</summary>
    public static byte[] EncodePrivateKeyInfo(string algorithm, byte[]? parameters, ReadOnlySpan<byte> privateKey)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            writer.WriteInteger(0);
            WriteAlgorithmIdentifier(writer, algorithm, parameters);
            writer.WriteOctetString(privateKey);
        }

        return writer.Encode();
    }

    /// <summary>The DER encoding of the NULL type (the parameters of rsaEncryption).</summary>
    public static byte[] EncodeNull()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteNull();
        return writer.Encode();
    }

    /// <summary>The DER encoding of an object identifier (e.g. the namedCurve choice of ECParameters).</summary>
    public static byte[] EncodeObjectIdentifier(string oid)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteObjectIdentifier(oid);
        return writer.Encode();
    }

    /// <summary>Returns the object identifier encoded by DER data, or null when the data is another value.</summary>
    public static string? TryDecodeObjectIdentifier(byte[] data)
    {
        try
        {
            var reader = new AsnReader(data, AsnEncodingRules.DER);
            if (!reader.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier))
                return null;

            var oid = reader.ReadObjectIdentifier();
            return reader.HasData ? null : oid;
        }
        catch (AsnContentException)
        {
            return null;
        }
    }

    /// <summary>Parses the content of an octet string (e.g. CurvePrivateKey of RFC 8410 section 7).</summary>
    public static byte[] ParseOctetString(byte[] data) => Parse(data, reader => reader.ReadOctetString());

    /// <summary>The DER encoding of an octet string.</summary>
    public static byte[] EncodeOctetString(ReadOnlySpan<byte> value)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        writer.WriteOctetString(value);
        return writer.Encode();
    }

    /// <summary>
    /// Parses DER data with exactData set to true (https://w3c.github.io/webcrypto/#concept-parse-an-asn1-structure): the
    /// data must be a single value, and the decoding errors are <c>DataError</c>s.
    /// </summary>
    public static T Parse<T>(byte[] data, Func<AsnReader, T> parse)
    {
        try
        {
            var reader = new AsnReader(data, AsnEncodingRules.DER);
            var result = parse(reader);
            reader.ThrowIfNotEmpty();
            return result;
        }
        catch (AsnContentException ex)
        {
            throw new WebCryptoException("DataError", "The key data is not a valid DER structure: " + ex.Message);
        }
    }

    /// <summary>Reads an INTEGER as an unsigned integer: negative values are DataErrors.</summary>
    public static BigInteger ReadUnsignedInteger(AsnReader reader)
    {
        var value = reader.ReadInteger();
        if (value.Sign < 0)
            throw WebCryptoException.DataError("The integer is negative");

        return value;
    }

    private static (string Algorithm, byte[]? Parameters) ReadAlgorithmIdentifier(AsnReader reader)
    {
        var sequence = reader.ReadSequence();
        var algorithm = sequence.ReadObjectIdentifier();
        byte[]? parameters = null;
        if (sequence.HasData)
        {
            parameters = sequence.ReadEncodedValue().ToArray();
        }

        sequence.ThrowIfNotEmpty();
        return (algorithm, parameters);
    }

    private static void WriteAlgorithmIdentifier(AsnWriter writer, string algorithm, byte[]? parameters)
    {
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(algorithm);
            if (parameters is not null)
            {
                writer.WriteEncodedValue(parameters);
            }
        }
    }
}
