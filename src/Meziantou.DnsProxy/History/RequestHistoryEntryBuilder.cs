using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Records;

namespace Meziantou.DnsProxy.History;

internal sealed class RequestHistoryEntryBuilder
{
    public DateTimeOffset TimestampUtc { get; set; }

    public string Client { get; set; } = "";

    public string Protocol { get; set; } = "";

    public string QuestionName { get; set; } = "";

    public string QuestionType { get; set; } = "";

    public string Result { get; set; } = "";

    public string Upstream { get; set; } = "";

    public long? LatencyMs { get; set; }

    public string ResponseCode { get; set; } = "";

    public RequestHistoryEntry Build(DnsMessage response)
    {
        return new RequestHistoryEntry(
            TimestampUtc,
            Client,
            Protocol,
            QuestionName,
            QuestionType,
            Result,
            Upstream,
            LatencyMs,
            ResponseCode,
            response.Answers.Select(ToDisplayString).ToArray());
    }

    private static string ToDisplayString(DnsResourceRecord record)
    {
        return record.Data switch
        {
            DnsARecordData aData => $"{record.Name} A {aData.Address}",
            DnsAaaaRecordData aaaaData => $"{record.Name} AAAA {aaaaData.Address}",
            DnsCnameRecordData cnameData => $"{record.Name} CNAME {cnameData.CanonicalName}",
            null => $"{record.Name} {record.Type} <empty>",
            _ => $"{record.Name} {record.Type}",
        };
    }
}
