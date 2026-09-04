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
