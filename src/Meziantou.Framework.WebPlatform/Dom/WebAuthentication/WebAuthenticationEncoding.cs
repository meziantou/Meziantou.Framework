using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The encodings of Web Authentication (https://w3c.github.io/webauthn/): the base64url encoding of the binary values of the
/// JSON types, the JSON-compatible serialization of the client data, and the canonical CBOR of the authenticator data.
/// </summary>
internal static class WebAuthenticationEncoding
{
    /// <summary>
    /// https://w3c.github.io/webauthn/#base64url-encoding: the URL and filename safe alphabet of RFC 4648 (section 5),
    /// without padding.
    /// </summary>
    public static string Base64UrlEncode(ReadOnlySpan<byte> data) => System.Buffers.Text.Base64Url.EncodeToString(data);

    /// <summary>
    /// Decodes the base64url encoding of the values of the JSON types (https://w3c.github.io/webauthn/#base64url-encoding):
    /// the characters of the URL and filename safe alphabet without padding, nor white space. Returns null when the value is
    /// not a valid encoding.
    /// </summary>
    public static byte[]? Base64UrlDecode(string value) => Base64UrlDecode(value, allowPadding: false);

    /// <summary>
    /// Decodes a base64url string. With <paramref name="allowPadding"/>, the trailing <c>=</c> padding and the characters of
    /// the standard alphabet (<c>+</c> and <c>/</c>) are accepted too, as the values of the WebDriver commands created with
    /// <c>btoa()</c>.
    /// </summary>
    public static byte[]? Base64UrlDecode(string value, bool allowPadding)
    {
        ArgumentNullException.ThrowIfNull(value);
        var length = value.Length;
        if (allowPadding && length % 4 == 0)
        {
            // At most two padding characters, which complete the last group
            var padding = 0;
            while (padding < 2 && length > 0 && value[length - 1] == '=')
            {
                length--;
                padding++;
            }
        }

        if (length % 4 == 1)
            return null;

        var result = new byte[length / 4 * 3 + (length % 4 == 0 ? 0 : length % 4 - 1)];
        var accumulator = 0;
        var bits = 0;
        var index = 0;
        for (var i = 0; i < length; i++)
        {
            var sextet = value[i] switch
            {
                >= 'A' and <= 'Z' => value[i] - 'A',
                >= 'a' and <= 'z' => value[i] - 'a' + 26,
                >= '0' and <= '9' => value[i] - '0' + 52,
                '-' => 62,
                '_' => 63,
                '+' when allowPadding => 62,
                '/' when allowPadding => 63,
                _ => -1,
            };
            if (sextet < 0)
                return null;

            accumulator = (accumulator << 6) | sextet;
            bits += 6;
            if (bits >= 8)
            {
                bits -= 8;
                result[index++] = (byte)(accumulator >> bits);
                accumulator &= (1 << bits) - 1;
            }
        }

        return result;
    }

    /// <summary>
    /// https://w3c.github.io/webauthn/#clientdatajson-serialization: the JSON-compatible serialization of the client data,
    /// whose members are in a fixed order (<c>type</c>, <c>challenge</c>, <c>origin</c>, <c>crossOrigin</c>, then
    /// <c>topOrigin</c> when present).
    /// </summary>
    public static byte[] SerializeClientData(string type, ReadOnlySpan<byte> challenge, string origin, bool crossOrigin, string? topOrigin)
    {
        var result = new StringBuilder();
        result.Append("{\"type\":");
        AppendCcdString(result, type);
        result.Append(",\"challenge\":");
        AppendCcdString(result, Base64UrlEncode(challenge));
        result.Append(",\"origin\":");
        AppendCcdString(result, origin);
        result.Append(",\"crossOrigin\":");
        result.Append(crossOrigin ? "true" : "false");
        if (topOrigin is not null)
        {
            result.Append(",\"topOrigin\":");
            AppendCcdString(result, topOrigin);
        }

        result.Append('}');
        return Encoding.UTF8.GetBytes(result.ToString());
    }

    /// <summary>
    /// https://w3c.github.io/webauthn/#ccdtostring: a JSON string whose control characters (and the lone surrogates, which
    /// UTF-8 cannot encode) are escaped as <c>\uXXXX</c>.
    /// </summary>
    internal static void AppendCcdString(StringBuilder result, string value)
    {
        result.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                result.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }

            switch (c)
            {
                case '"':
                    result.Append("\\\"");
                    break;
                case '\\':
                    result.Append("\\\\");
                    break;
                case < ' ' or (>= '\uD800' and <= '\uDFFF'):
                    result.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                default:
                    result.Append(c);
                    break;
            }
        }

        result.Append('"');
    }

    /// <summary>
    /// https://w3c.github.io/webauthn/#sctn-authenticator-data: the RP ID hash, the flags, the signature counter (big-endian),
    /// then the attested credential data (the AAGUID, the length of the credential ID, the credential ID and the public key).
    /// </summary>
    public static byte[] EncodeAuthenticatorData(ReadOnlySpan<byte> rpIdHash, byte flags, uint signCount, ReadOnlySpan<byte> credentialId, ReadOnlySpan<byte> credentialPublicKey)
    {
        var hasAttestedCredentialData = (flags & AuthenticatorDataFlags.AttestedCredentialData) != 0;
        var length = 37 + (hasAttestedCredentialData ? 16 + 2 + credentialId.Length + credentialPublicKey.Length : 0);
        var result = new byte[length];
        rpIdHash.CopyTo(result);
        result[32] = flags;
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(33), signCount);
        if (hasAttestedCredentialData)
        {
            // The AAGUID of the virtual authenticators is 16 zero bytes (self attestation)
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(37 + 16), (ushort)credentialId.Length);
            credentialId.CopyTo(result.AsSpan(37 + 18));
            credentialPublicKey.CopyTo(result.AsSpan(37 + 18 + credentialId.Length));
        }

        return result;
    }

    /// <summary>
    /// https://w3c.github.io/webauthn/#sctn-generating-an-attestation-object: the CBOR map whose keys are <c>fmt</c>,
    /// <c>attStmt</c> and <c>authData</c>, in the order of the CTAP2 canonical CBOR encoding.
    /// </summary>
    public static byte[] EncodeAttestationObject(string format, int? algorithm, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> authenticatorData)
    {
        var writer = new CborWriter();
        writer.WriteMapHeader(3);
        writer.WriteText("fmt");
        writer.WriteText(format);
        writer.WriteText("attStmt");
        if (algorithm is { } alg)
        {
            // https://w3c.github.io/webauthn/#sctn-packed-attestation: self attestation, without x5c
            writer.WriteMapHeader(2);
            writer.WriteText("alg");
            writer.WriteInteger(alg);
            writer.WriteText("sig");
            writer.WriteBytes(signature);
        }
        else
        {
            writer.WriteMapHeader(0);
        }

        writer.WriteText("authData");
        writer.WriteBytes(authenticatorData);
        return writer.ToArray();
    }
}

/// <summary>The flags of the authenticator data (https://w3c.github.io/webauthn/#authdata-flags).</summary>
internal static class AuthenticatorDataFlags
{
    public const byte UserPresent = 0x01;
    public const byte UserVerified = 0x04;
    public const byte BackupEligibility = 0x08;
    public const byte BackupState = 0x10;
    public const byte AttestedCredentialData = 0x40;
    public const byte ExtensionData = 0x80;
}

/// <summary>
/// A writer of the CTAP2 canonical CBOR encoding (https://fidoalliance.org/specs/fido-v2.1-ps-20210615/fido-client-to-authenticator-protocol-v2.1-ps-20210615.html#ctap2-canonical-cbor-encoding-form):
/// the integers and the lengths use their shortest form, and the lengths are definite. The callers write the keys of the
/// maps in the canonical order.
/// </summary>
internal sealed class CborWriter
{
    private readonly List<byte> _buffer = [];

    public void WriteInteger(long value)
    {
        if (value >= 0)
        {
            WriteHead(0, (ulong)value);
        }
        else
        {
            WriteHead(1, (ulong)(-1 - value));
        }
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteHead(2, (ulong)value.Length);
        foreach (var b in value)
        {
            _buffer.Add(b);
        }
    }

    public void WriteText(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteHead(3, (ulong)bytes.Length);
        _buffer.AddRange(bytes);
    }

    public void WriteArrayHeader(int count) => WriteHead(4, (ulong)count);

    public void WriteMapHeader(int count) => WriteHead(5, (ulong)count);

    public void WriteBoolean(bool value) => _buffer.Add(value ? (byte)0xF5 : (byte)0xF4);

    public byte[] ToArray() => [.. _buffer];

    private void WriteHead(int majorType, ulong value)
    {
        var major = (byte)(majorType << 5);
        switch (value)
        {
            case < 24:
                _buffer.Add((byte)(major | (byte)value));
                break;
            case <= byte.MaxValue:
                _buffer.Add((byte)(major | 24));
                _buffer.Add((byte)value);
                break;
            case <= ushort.MaxValue:
                _buffer.Add((byte)(major | 25));
                _buffer.Add((byte)(value >> 8));
                _buffer.Add((byte)value);
                break;
            case <= uint.MaxValue:
                _buffer.Add((byte)(major | 26));
                for (var shift = 24; shift >= 0; shift -= 8)
                {
                    _buffer.Add((byte)(value >> shift));
                }

                break;
            default:
                _buffer.Add((byte)(major | 27));
                for (var shift = 56; shift >= 0; shift -= 8)
                {
                    _buffer.Add((byte)(value >> shift));
                }

                break;
        }
    }
}
