using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;

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
}
