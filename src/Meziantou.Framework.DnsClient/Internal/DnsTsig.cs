using System.Buffers.Binary;
using System.Security.Cryptography;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsClient.Internal;

/// <summary>Signs messages with TSIG (RFC 8945).</summary>
internal static class DnsTsig
{
    public const int HeaderSize = 12;

    public static string GetAlgorithmName(DnsTsigAlgorithm algorithm)
    {
        return algorithm switch
        {
            DnsTsigAlgorithm.HmacSha256 => "hmac-sha256",
            DnsTsigAlgorithm.HmacSha384 => "hmac-sha384",
            DnsTsigAlgorithm.HmacSha512 => "hmac-sha512",
            DnsTsigAlgorithm.HmacSha1 => "hmac-sha1",
            DnsTsigAlgorithm.HmacMd5 => "hmac-md5.sig-alg.reg.int",
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported TSIG algorithm."),
        };
    }

    public static IncrementalHash CreateHmac(DnsTsigKey key)
    {
        var hashAlgorithm = key.Algorithm switch
        {
            DnsTsigAlgorithm.HmacSha256 => HashAlgorithmName.SHA256,
            DnsTsigAlgorithm.HmacSha384 => HashAlgorithmName.SHA384,
            DnsTsigAlgorithm.HmacSha512 => HashAlgorithmName.SHA512,
            DnsTsigAlgorithm.HmacSha1 => HashAlgorithmName.SHA1,
            DnsTsigAlgorithm.HmacMd5 => HashAlgorithmName.MD5,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key.Algorithm, "Unsupported TSIG algorithm."),
        };

        return IncrementalHash.CreateHMAC(hashAlgorithm, key.Secret);
    }

    /// <summary>
    /// Appends a TSIG record to <paramref name="message"/>. A query has no <paramref name="priorMac"/>. The first response
    /// of a query has the MAC of the query, and the next messages of a transfer the MAC of the previous signed message, with
    /// the unsigned messages received since then (<paramref name="unsignedMessages"/>) and only the timers
    /// (<paramref name="timersOnly"/>, RFC 8945 section 5.3.1).
    /// </summary>
    public static byte[] Sign(ReadOnlySpan<byte> message, DnsTsigKey key, ulong timeSigned, ReadOnlySpan<byte> priorMac, ReadOnlySpan<byte> unsignedMessages, bool timersOnly, DnsResponseCode error, out byte[] mac)
    {
        if (message.Length < HeaderSize)
            throw new ArgumentException("The message is shorter than a DNS header.", nameof(message));

        var fudge = (ushort)key.Fudge.TotalSeconds;
        using (var hmac = CreateHmac(key))
        {
            if (!priorMac.IsEmpty)
            {
                AppendMac(hmac, priorMac);
            }

            hmac.AppendData(unsignedMessages);
            hmac.AppendData(message);
            AppendVariables(hmac, key, timeSigned, fudge, error, otherData: [], timersOnly);
            mac = hmac.GetHashAndReset();
        }

        var writer = new DnsWireWriter(message.Length + 128);
        writer.WriteBytes(message);
        writer.WriteUInt16At((ushort)(BinaryPrimitives.ReadUInt16BigEndian(message[10..]) + 1), 10); // ARCOUNT
        writer.WriteDomainName(key.CanonicalName);
        writer.WriteUInt16((ushort)DnsQueryType.TSIG);
        writer.WriteUInt16((ushort)DnsQueryClass.ANY);
        writer.WriteUInt32(0); // TTL
        var rdLengthPosition = writer.Position;
        writer.WriteUInt16(0);
        var rdataStart = writer.Position;
        writer.WriteDomainName(GetAlgorithmName(key.Algorithm));
        WriteUInt48(ref writer, timeSigned);
        writer.WriteUInt16(fudge);
        writer.WriteUInt16((ushort)mac.Length);
        writer.WriteBytes(mac);
        writer.WriteBytes(message[..2]); // Original ID
        writer.WriteUInt16((ushort)error);
        writer.WriteUInt16(0); // Other Len
        writer.WriteUInt16At((ushort)(writer.Position - rdataStart), rdLengthPosition);
        return writer.ToArray();
    }

    /// <summary>Appends a MAC with its 2-byte length, as the request MAC and the prior MAC are digested.</summary>
    public static void AppendMac(IncrementalHash hmac, ReadOnlySpan<byte> mac)
    {
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)mac.Length);
        hmac.AppendData(length);
        hmac.AppendData(mac);
    }

    /// <summary>Appends the TSIG variables (RFC 8945 section 4.3.3), or only the timers for the next messages of a transfer.</summary>
    public static void AppendVariables(IncrementalHash hmac, DnsTsigKey key, ulong timeSigned, ushort fudge, DnsResponseCode error, ReadOnlySpan<byte> otherData, bool timersOnly)
    {
        var writer = new DnsWireWriter(128);
        if (!timersOnly)
        {
            writer.WriteDomainName(key.CanonicalName);
            writer.WriteUInt16((ushort)DnsQueryClass.ANY);
            writer.WriteUInt32(0); // TTL
            writer.WriteDomainName(GetAlgorithmName(key.Algorithm));
        }

        WriteUInt48(ref writer, timeSigned);
        writer.WriteUInt16(fudge);
        if (!timersOnly)
        {
            writer.WriteUInt16((ushort)error);
            writer.WriteUInt16((ushort)otherData.Length);
            writer.WriteBytes(otherData);
        }

        hmac.AppendData(writer.WrittenSpan);
    }

    private static void WriteUInt48(ref DnsWireWriter writer, ulong value)
    {
        writer.WriteUInt16((ushort)(value >> 32));
        writer.WriteUInt32((uint)value);
    }
}
