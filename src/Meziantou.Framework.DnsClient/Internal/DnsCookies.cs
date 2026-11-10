using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Response.Records;

namespace Meziantou.Framework.DnsClient.Internal;

/// <summary>DNS cookies (RFC 7873, with the server cookies of RFC 9018).</summary>
internal static class DnsCookies
{
    public const ushort OptionCode = 10;
    public const int ClientCookieLength = 8;
    public const int MinServerCookieLength = 8;
    public const int MaxServerCookieLength = 32;

    /// <summary>
    /// Reads the server cookie of a response. Returns <see langword="null"/> when the response has no COOKIE option, or
    /// only the client cookie.
    /// </summary>
    /// <exception cref="DnsProtocolException">The COOKIE option is malformed, repeated, or does not echo <paramref name="clientCookie"/>.</exception>
    public static byte[]? ReadServerCookie(DnsResponseMessage response, ReadOnlySpan<byte> clientCookie)
    {
        byte[]? serverCookie = null;
        var found = false;
        foreach (var opt in response.AdditionalRecords.OfType<DnsOptRecord>())
        {
            foreach (var option in opt.Options)
            {
                if (option.Code is not OptionCode)
                    continue;

                if (found)
                    throw new DnsProtocolException("The response has more than one COOKIE option.");

                found = true;
                var data = option.Data;
                if (data.Length is not ClientCookieLength and (< ClientCookieLength + MinServerCookieLength or > ClientCookieLength + MaxServerCookieLength))
                    throw new DnsProtocolException($"The COOKIE option of the response has {data.Length} bytes: it must have 8 bytes, or between 16 and 40 (RFC 7873 section 4).");

                // RFC 7873 section 5.3: a response whose client cookie is not ours was not sent to this client
                if (!data.AsSpan(0, ClientCookieLength).SequenceEqual(clientCookie))
                    throw new DnsProtocolException("The client cookie of the response does not match the cookie of the query.");

                serverCookie = data.Length > ClientCookieLength ? data[ClientCookieLength..] : null;
            }
        }

        return serverCookie;
    }
}
