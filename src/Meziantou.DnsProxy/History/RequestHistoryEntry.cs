namespace Meziantou.DnsProxy.History;

internal sealed record RequestHistoryEntry(
    DateTimeOffset TimestampUtc,
    string Client,
    string Protocol,
    string QuestionName,
    string QuestionType,
    string Result,
    string Upstream,
    long? LatencyMs,
    string ResponseCode,
    IReadOnlyList<string> Answers);
