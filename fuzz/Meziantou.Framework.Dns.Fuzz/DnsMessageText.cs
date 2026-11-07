using System.Globalization;
using Meziantou.Framework.Fuzzing;
using ClientProtocol = Meziantou.Framework.DnsClient.Protocol;
using ClientRecords = Meziantou.Framework.DnsClient.Response.Records;
using ClientResponse = Meziantou.Framework.DnsClient.Response;
using ServerProtocol = Meziantou.Framework.DnsServer.Protocol;
using ServerRecords = Meziantou.Framework.DnsServer.Protocol.Records;

namespace Meziantou.Framework.DnsFuzz;

/// <summary>
/// Formats a decoded message of either library in one format, a line per question and per record, so that the results of
/// two decoders (or of a decoder and the message that was encoded) can be compared as text. Domain names are written the
/// way the server represents them: the labels joined with dots, each label decoded as ASCII.
/// </summary>
internal static class DnsMessageText
{
    /// <summary>The longest domain name in text: 255 bytes on the wire, minus the root label and the first length byte.</summary>
    public const int MaxNameTextLength = 253;

    /// <summary>Formats a server message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="normalizeNames">
    /// Compares the names as the server encoder writes them: it compresses names ignoring their case, and drops a trailing
    /// dot. The type bit maps are compared as sets, which is how the encoder writes them.
    /// </param>
    /// <param name="includeResponseCode">Includes the response code.</param>
    public static string Format(ServerProtocol.DnsMessage message, bool normalizeNames, bool includeResponseCode = true)
    {
        Func<string, string> formatName = normalizeNames ? NormalizeServerName : name => name;
        var builder = new StringBuilder();
        AppendHeader(builder, message.Id, message.IsResponse, (int)message.OpCode, message.IsAuthoritative, message.IsTruncated, message.RecursionDesired, message.RecursionAvailable, message.AuthenticatedData, message.CheckingDisabled);
        if (includeResponseCode)
        {
            builder.Append(CultureInfo.InvariantCulture, $"RCODE {(int)message.ResponseCode}\n");
        }

        foreach (var question in message.Questions)
        {
            builder.Append(CultureInfo.InvariantCulture, $"Q {FuzzText.Escape(formatName(question.Name))} {(ushort)question.Type} {(ushort)question.QueryClass}\n");
        }

        AppendServerRecords(builder, "AN", message.Answers, formatName, normalizeNames);
        AppendServerRecords(builder, "NS", message.Authorities, formatName, normalizeNames);
        AppendServerRecords(builder, "AR", message.AdditionalRecords, formatName, normalizeNames);
        if (message.EdnsOptions is { } edns)
        {
            AppendEdns(builder, edns.UdpPayloadSize, edns.Version, edns.DnssecOk, edns.ExtendedRCode);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Formats a client response like <see cref="Format(ServerProtocol.DnsMessage, bool, bool)"/> formats the server's decoding
    /// of the same bytes: the server takes the last OPT record out of the additional section as the EDNS options, and
    /// reads CDS and CDNSKEY records as unknown records.
    /// </summary>
    /// <param name="response">The response, decoded with the raw record data.</param>
    /// <param name="normalizeNames">Lowercases the names (see <see cref="Format(ServerProtocol.DnsMessage, bool, bool)"/>).</param>
    /// <param name="includeResponseCode">
    /// Includes the response code. The decoders take its extended bits from different OPT records when there are several.
    /// </param>
    /// <param name="presentationNames">
    /// Keeps the names in presentation format, so that names whose labels differ only in where the dots are (a label
    /// <c>a.b</c> or the labels <c>a</c> and <c>b</c>), or in the bytes the server decodes as <c>?</c>, are different.
    /// </param>
    /// <param name="parseCdsAsUnknown">Formats the CDS and CDNSKEY records from their raw data, as the server reads them.</param>
    public static string Format(ClientResponse.DnsResponseMessage response, bool normalizeNames, bool includeResponseCode, bool presentationNames = false, bool parseCdsAsUnknown = true)
    {
        Func<string, string> formatName = (normalizeNames, presentationNames) switch
        {
            (true, true) => name => name.ToLowerInvariant(),
            (true, false) => name => NormalizeServerName(ToServerName(name)),
            (false, true) => name => name,
            (false, false) => ToServerName,
        };

        var header = response.Header;
        var builder = new StringBuilder();
        AppendHeader(builder, header.Id, header.IsResponse, (int)header.OpCode, header.IsAuthoritative, header.IsTruncated, header.RecursionDesired, header.RecursionAvailable, header.AuthenticatedData, header.CheckingDisabled);
        if (includeResponseCode)
        {
            builder.Append(CultureInfo.InvariantCulture, $"RCODE {(int)header.ResponseCode}\n");
        }

        foreach (var question in response.Questions)
        {
            builder.Append(CultureInfo.InvariantCulture, $"Q {FuzzText.Escape(formatName(question.Name))} {(ushort)question.Type} {(ushort)question.QueryClass}\n");
        }

        AppendClientRecords(builder, "AN", response.Answers, formatName, normalizeNames, parseCdsAsUnknown);
        AppendClientRecords(builder, "NS", response.Authorities, formatName, normalizeNames, parseCdsAsUnknown);

        var lastOpt = response.AdditionalRecords.LastOrDefault(record => record is ClientRecords.DnsOptRecord) as ClientRecords.DnsOptRecord;
        AppendClientRecords(builder, "AR", [.. response.AdditionalRecords.Where(record => !ReferenceEquals(record, lastOpt))], formatName, normalizeNames, parseCdsAsUnknown);
        if (lastOpt is not null)
        {
            AppendEdns(builder, lastOpt.UdpPayloadSize, lastOpt.EdnsVersion, lastOpt.DnssecOk, lastOpt.ExtendedRCode);
        }

        return builder.ToString();
    }

    /// <summary>Converts a name in presentation format (client) to the text the server decodes from the same wire name.</summary>
    public static string ToServerName(string name)
    {
        var builder = new StringBuilder(name.Length);
        Span<byte> label = stackalloc byte[ClientProtocol.DnsName.MaxLabelLength];
        foreach (var text in ClientProtocol.DnsName.EnumerateLabels(name))
        {
            var length = ClientProtocol.DnsName.DecodeLabel(text, label);
            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(Encoding.ASCII.GetString(label[..length]));
        }

        return builder.ToString();
    }

    /// <summary>Converts a label to presentation format (client).</summary>
    public static string ToPresentationLabel(ReadOnlySpan<byte> label)
    {
        var builder = new StringBuilder();
        ClientProtocol.DnsName.AppendLabel(builder, label);
        return builder.ToString();
    }

    public static string NormalizeServerName(string name)
    {
        if (name.EndsWith(".", StringComparison.Ordinal))
        {
            name = name[..^1];
        }

        return name.ToLowerInvariant();
    }

    private static void AppendHeader(StringBuilder builder, ushort id, bool isResponse, int opCode, bool isAuthoritative, bool isTruncated, bool recursionDesired, bool recursionAvailable, bool authenticatedData, bool checkingDisabled)
    {
        builder.Append(CultureInfo.InvariantCulture, $"ID {id} QR={isResponse} OPCODE={opCode} AA={isAuthoritative} TC={isTruncated} RD={recursionDesired} RA={recursionAvailable} AD={authenticatedData} CD={checkingDisabled}\n");
    }

    private static void AppendEdns(StringBuilder builder, ushort udpPayloadSize, byte version, bool dnssecOk, byte extendedRCode)
    {
        builder.Append(CultureInfo.InvariantCulture, $"EDNS size={udpPayloadSize} version={version} DO={dnssecOk} extended-rcode={extendedRCode}\n");
    }

    private static void AppendServerRecords(StringBuilder builder, string section, IEnumerable<ServerProtocol.DnsResourceRecord> records, Func<string, string> formatName, bool sortTypeBitMaps)
    {
        foreach (var record in records)
        {
            builder.Append(CultureInfo.InvariantCulture, $"{section} {FuzzText.Escape(formatName(record.Name))} {(ushort)record.Type} {(ushort)record.Class} {record.TimeToLive} {DnsRecordFormatter.Format(record.Data, formatName, sortTypeBitMaps)}\n");
        }
    }

    private static void AppendClientRecords(StringBuilder builder, string section, IEnumerable<ClientResponse.DnsRecord> records, Func<string, string> formatName, bool sortTypeBitMaps, bool parseCdsAsUnknown)
    {
        foreach (var record in records)
        {
            // The server does not parse CDS and CDNSKEY (RFC 7344), which have the wire format of DS and DNSKEY
            var data = parseCdsAsUnknown && record.RecordType is DnsClient.Query.DnsQueryType.CDS or DnsClient.Query.DnsQueryType.CDNSKEY
                ? DnsRecordFormatter.Format(new ServerRecords.DnsUnknownRecordData { Data = record.RawData }, formatName, sortTypeBitMaps)
                : DnsRecordFormatter.Format(record, formatName, sortTypeBitMaps);
            builder.Append(CultureInfo.InvariantCulture, $"{section} {FuzzText.Escape(formatName(record.Name))} {(ushort)record.RecordType} {(ushort)record.RecordClass} {record.TimeToLive} {data}\n");
        }
    }
}
