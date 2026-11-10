using System.Buffers.Binary;
using System.Net;
using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;
using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsClient.Tests;

/// <summary>Builds DNS wire messages for tests that drive <see cref="DnsClient"/> through a fake transport.</summary>
internal static class DnsTestMessages
{
    /// <summary>
    /// Builds an answer-less response for <paramref name="query"/>, echoing its identifier and question section so the
    /// client's response-to-query validation accepts it.
    /// </summary>
    public static byte[] CreateEmptyResponse(byte[] query, DnsResponseCode responseCode = DnsResponseCode.NoError)
    {
        var questionEnd = GetQuestionSectionEnd(query);
        var response = query.AsSpan(0, questionEnd).ToArray();

        response[2] = 0x81; // QR + RD
        response[3] = (byte)(0x80 | (byte)responseCode); // RA + RCODE
        response[6] = 0; // ANCOUNT
        response[7] = 0;
        response[8] = 0; // NSCOUNT
        response[9] = 0;
        response[10] = 0; // ARCOUNT
        response[11] = 0;

        return response;
    }

    /// <summary>
    /// Builds a response for <paramref name="query"/> holding one A answer, one SOA authority record and an OPT record,
    /// echoing the query identifier and question so the client's response-to-query validation accepts it.
    /// </summary>
    public static byte[] CreateResponseWithTimeToLives(byte[] query, uint answerTimeToLive, uint authorityTimeToLive, uint optTimeToLive)
    {
        var questionEnd = GetQuestionSectionEnd(query);
        var encodedName = query.AsSpan(12, questionEnd - 12 - 4).ToArray();

        var writer = new DnsWireWriter(512);
        writer.WriteBytes(query.AsSpan(0, questionEnd));
        writer.WriteUInt16At(0x8180, 2); // QR + RD + RA
        writer.WriteUInt16At(1, 6); // ANCOUNT
        writer.WriteUInt16At(1, 8); // NSCOUNT
        writer.WriteUInt16At(1, 10); // ARCOUNT

        writer.WriteBytes(encodedName);
        writer.WriteUInt16((ushort)DnsQueryType.A);
        writer.WriteUInt16((ushort)DnsQueryClass.IN);
        writer.WriteUInt32(answerTimeToLive);
        writer.WriteUInt16(4);
        writer.WriteBytes([1, 2, 3, 4]);

        writer.WriteBytes(encodedName);
        writer.WriteUInt16((ushort)DnsQueryType.SOA);
        writer.WriteUInt16((ushort)DnsQueryClass.IN);
        writer.WriteUInt32(authorityTimeToLive);
        var rdLengthPosition = writer.Position;
        writer.WriteUInt16(0);
        var rdataStart = writer.Position;
        writer.WriteDomainName("ns.example.com");
        writer.WriteDomainName("hostmaster.example.com");
        writer.WriteUInt32(1); // SERIAL
        writer.WriteUInt32(2); // REFRESH
        writer.WriteUInt32(3); // RETRY
        writer.WriteUInt32(4); // EXPIRE
        writer.WriteUInt32(5); // MINIMUM
        writer.WriteUInt16At((ushort)(writer.Position - rdataStart), rdLengthPosition);

        writer.WriteByte(0); // root name
        writer.WriteUInt16((ushort)DnsQueryType.OPT);
        writer.WriteUInt16(1232); // UDP payload size
        writer.WriteUInt32(optTimeToLive);
        writer.WriteUInt16(0); // RDLENGTH

        return writer.ToArray();
    }

    /// <summary>An SOA record of a test zone, whose only meaningful field is the serial.</summary>
    public static TestRecord Soa(string zone, uint serial)
    {
        var writer = new DnsWireWriter(64);
        writer.WriteDomainName("ns." + zone);
        writer.WriteDomainName("hostmaster." + zone);
        writer.WriteUInt32(serial);
        writer.WriteUInt32(3600); // REFRESH
        writer.WriteUInt32(600); // RETRY
        writer.WriteUInt32(86400); // EXPIRE
        writer.WriteUInt32(60); // MINIMUM
        return new TestRecord(zone, DnsQueryType.SOA, writer.ToArray());
    }

    public static TestRecord A(string name, string address) => new(name, DnsQueryType.A, IPAddress.Parse(address).GetAddressBytes());

    /// <summary>
    /// Builds a response to <paramref name="query"/> with its identifier, operation code and question (unless
    /// <paramref name="includeQuestion"/> is false, as in the messages of a zone transfer after the first one).
    /// </summary>
    public static byte[] CreateResponse(byte[] query, IEnumerable<TestRecord> answers, bool includeQuestion = true, DnsResponseCode responseCode = DnsResponseCode.NoError, byte[]? cookie = null)
    {
        var records = answers.ToList();
        var hasOpt = cookie is not null || (ushort)responseCode > 15;
        var questionEnd = GetQuestionSectionEnd(query);

        var writer = new DnsWireWriter(512);
        writer.WriteBytes(query.AsSpan(0, includeQuestion ? questionEnd : 12));
        writer.WriteUInt16At((ushort)(0x8400 | (BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(2)) & 0x7900) | ((ushort)responseCode & 0x0F)), 2); // QR + AA + OPCODE + RD + RCODE
        writer.WriteUInt16At(includeQuestion ? BinaryPrimitives.ReadUInt16BigEndian(query.AsSpan(4)) : (ushort)0, 4);
        writer.WriteUInt16At((ushort)records.Count, 6);
        writer.WriteUInt16At(0, 8);
        writer.WriteUInt16At(hasOpt ? (ushort)1 : (ushort)0, 10);
        foreach (var record in records)
        {
            writer.WriteDomainName(record.Name);
            writer.WriteUInt16((ushort)record.Type);
            writer.WriteUInt16((ushort)DnsQueryClass.IN);
            writer.WriteUInt32(3600);
            writer.WriteUInt16((ushort)record.Data.Length);
            writer.WriteBytes(record.Data);
        }

        if (hasOpt)
        {
            writer.WriteByte(0); // root name
            writer.WriteUInt16((ushort)DnsQueryType.OPT);
            writer.WriteUInt16(1232);
            writer.WriteUInt32((uint)((ushort)responseCode >> 4) << 24); // extended RCODE
            writer.WriteUInt16(cookie is null ? (ushort)0 : (ushort)(cookie.Length + 4));
            if (cookie is not null)
            {
                writer.WriteUInt16(10); // COOKIE
                writer.WriteUInt16((ushort)cookie.Length);
                writer.WriteBytes(cookie);
            }
        }

        return writer.ToArray();
    }

    /// <summary>Gets the COOKIE option of a query, or null.</summary>
    public static byte[]? GetCookie(byte[] query)
    {
        return DnsMessageEncoder.DecodeResponse(query).AdditionalRecords.OfType<DnsOptRecord>().SelectMany(opt => opt.Options).FirstOrDefault(option => option.Code is 10)?.Data;
    }

    /// <summary>
    /// Checks the TSIG signature of a query as a server does (RFC 8945 section 5.2), and returns its MAC, or null when
    /// the query is not signed or its signature is not valid.
    /// </summary>
    public static byte[]? VerifyQuerySignature(byte[] query, DnsTsigKey key)
    {
        var additionalRecords = DnsMessageEncoder.DecodeResponse(query).AdditionalRecords;
        if (additionalRecords.Count is 0 || additionalRecords[^1] is not DnsTsigRecord tsig)
            return null;

        var signedMessage = query.AsSpan(0, tsig.MessageOffset).ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(signedMessage, tsig.OriginalId);
        BinaryPrimitives.WriteUInt16BigEndian(signedMessage.AsSpan(10), (ushort)(BinaryPrimitives.ReadUInt16BigEndian(signedMessage.AsSpan(10)) - 1));
        DnsTsig.Sign(signedMessage, key, tsig.TimeSigned, priorMac: [], unsignedMessages: [], timersOnly: false, DnsResponseCode.NoError, out var mac);
        return mac.AsSpan().SequenceEqual(tsig.Mac) ? mac : null;
    }

    /// <summary>Signs the messages of a response as a server does (RFC 8945 sections 5.3 and 5.3.1), leaving unsigned the ones <paramref name="isSigned"/> rejects.</summary>
    public static List<byte[]> SignResponses(IReadOnlyList<byte[]> messages, DnsTsigKey key, byte[] requestMac, ulong timeSigned, Func<int, bool>? isSigned = null)
    {
        var result = new List<byte[]>(messages.Count);
        var priorMac = requestMac;
        var unsignedMessages = new List<byte>();
        for (var i = 0; i < messages.Count; i++)
        {
            if (isSigned is not null && !isSigned(i))
            {
                result.Add(messages[i]);
                unsignedMessages.AddRange(messages[i]);
                continue;
            }

            result.Add(DnsTsig.Sign(messages[i], key, timeSigned, priorMac, [.. unsignedMessages], timersOnly: i > 0, DnsResponseCode.NoError, out priorMac));
            unsignedMessages.Clear();
        }

        return result;
    }

    /// <summary>Returns the offset just past the question section of a DNS message.</summary>
    public static int GetQuestionSectionEnd(byte[] message)
    {
        var questionCount = (message[4] << 8) | message[5];
        var position = 12;

        for (var i = 0; i < questionCount; i++)
        {
            while (position < message.Length && message[position] != 0)
            {
                position += message[position] + 1;
            }

            position++; // root label
            position += 4; // QTYPE + QCLASS
        }

        return position;
    }

    /// <summary>A resource record of a test message, with its RDATA in wire format.</summary>
    public readonly record struct TestRecord(string Name, DnsQueryType Type, byte[] Data);
}
