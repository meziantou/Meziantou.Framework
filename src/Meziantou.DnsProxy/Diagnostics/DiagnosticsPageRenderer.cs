using Meziantou.DnsProxy.Filtering;
using Meziantou.DnsProxy.History;
using Meziantou.DnsProxy.Forwarding;

namespace Meziantou.DnsProxy.Diagnostics;

internal static class DiagnosticsPageRenderer
{
    public static string Render(DnsProxyOptions options, FilterEngineProvider filters, FilteringPauseState filteringPauseState, IReadOnlyList<UpstreamDnsClientInfo> upstreams, IReadOnlyList<RequestHistoryEntry> historyEntries)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append("""
            <!doctype html>
            <html>
            <head>
              <meta charset="utf-8" />
              <title>Meziantou.DnsProxy</title>
              <style>
                body { font-family: system-ui, sans-serif; margin: 1rem; }
                table { border-collapse: collapse; width: 100%; }
                th, td { border: 1px solid #ddd; padding: 0.4rem; font-size: 0.9rem; vertical-align: top; }
                th { background: #f7f7f7; position: sticky; top: 0; }
                .small { color: #666; font-size: 0.85rem; }
                .mono { font-family: ui-monospace, Consolas, monospace; }
                button { padding: 0.45rem 0.7rem; }
              </style>
            </head>
            <body>
            """);
        stringBuilder.Append("<h1>Meziantou.DnsProxy</h1>");
        stringBuilder.Append("<p class='small'>DNS proxy sample (client -> filter -> forward to remotes -> response to client).</p>");
        stringBuilder.Append("<h2>Configuration</h2>");
        stringBuilder.Append("<ul>");
        stringBuilder.Append("<li><span class='mono'>DnsPort</span>: ").Append(HtmlEncode(options.DnsPort.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>HttpPort</span>: ").Append(HtmlEncode(options.HttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>DnsOverHttpsPort</span>: ").Append(HtmlEncode(options.DnsOverHttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>DnsOverHttpsPath</span>: ").Append(HtmlEncode(string.IsNullOrWhiteSpace(options.DnsOverHttpsPath) ? "/dns-query" : options.DnsOverHttpsPath)).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>DnsOverTlsPort</span>: ").Append(HtmlEncode(options.DnsOverTlsPort.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>DnsOverQuicPort</span>: ").Append(HtmlEncode(options.DnsOverQuicPort.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>CertificatePath</span>: ").Append(HtmlEncode(options.CertificatePath ?? string.Empty)).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>FilterRefreshInterval</span>: ").Append(HtmlEncode(options.FilterRefreshInterval.ToString())).Append("</li>");
        var blockListCacheFolderPath = string.IsNullOrWhiteSpace(options.BlockListCacheFolderPath) ? DnsProxyOptions.GetDefaultBlockListCacheFolderPath() : options.BlockListCacheFolderPath;
        stringBuilder.Append("<li><span class='mono'>BlockListCacheFolderPath</span>: ").Append(HtmlEncode(blockListCacheFolderPath)).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>PositiveCacheDuration</span>: ").Append(HtmlEncode(options.PositiveCacheDuration.ToString())).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>NegativeCacheDuration</span>: ").Append(HtmlEncode(options.NegativeCacheDuration.ToString())).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>MaximumCacheDuration</span>: ").Append(HtmlEncode(options.MaximumCacheDuration.ToString())).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>MaxCacheEntries</span>: ").Append(HtmlEncode(options.MaxCacheEntries.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>MaxDnsQueriesPerClientPerMinute</span>: ").Append(HtmlEncode(options.MaxDnsQueriesPerClientPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>DnssecValidationMode</span>: ").Append(HtmlEncode(options.DnssecValidationMode.ToString())).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>DiagnosticsHistoryCapacity</span>: ").Append(HtmlEncode(options.DiagnosticsHistoryCapacity.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>Upstreams</span>: ").Append(HtmlEncode(string.Join(", ", upstreams.Select(u => u.DisplayName)))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>FilterLists</span>: ").Append(HtmlEncode(string.Join(", ", options.Filters.Select(f => f.Url)))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>CustomRecords</span>: ").Append(HtmlEncode(string.Join(", ", options.CustomRecords.Select(r => $"{r.Domain} => {r.Type}:{FormatCustomRecordValues(r)}")))).Append("</li>");
        stringBuilder.Append("<li><span class='mono'>LoadedFilterRules</span>: ").Append(HtmlEncode(filters.RuleCount.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</li>");
        stringBuilder.Append("</ul>");
        stringBuilder.Append("<h2>Filtering</h2>");
        var disabledUntilUtc = filteringPauseState.DisabledUntilUtc;
        if (disabledUntilUtc is null)
        {
            stringBuilder.Append("<p>Filtering is enabled.</p>");
            stringBuilder.Append("<form method='post' action='/filtering/disable'><button type='submit'>Disable filtering for 15 minutes</button></form>");
        }
        else
        {
            stringBuilder.Append("<p>Filtering is disabled until <span class='mono'>")
                .Append(HtmlEncode(disabledUntilUtc.Value.ToString("u", System.Globalization.CultureInfo.InvariantCulture)))
                .Append("</span>.</p>");
            stringBuilder.Append("<form method='post' action='/filtering/disable'><button type='submit' disabled>Disable filtering for 15 minutes</button></form>");
        }

        stringBuilder.Append("<h2>Recent Requests</h2>");
        stringBuilder.Append("<p class='small'>Stored entries: ").Append(HtmlEncode(historyEntries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))).Append("</p>");
        stringBuilder.Append("""
            <table>
              <thead>
                <tr>
                  <th>Timestamp (UTC)</th>
                  <th>Client</th>
                  <th>Protocol</th>
                  <th>Question</th>
                  <th>Result</th>
                  <th>Upstream</th>
                  <th>Latency</th>
                  <th>Response Code</th>
                  <th>Answers</th>
                </tr>
              </thead>
              <tbody>
            """);
        foreach (var historyEntry in historyEntries)
        {
            stringBuilder.Append("<tr>");
            stringBuilder.Append("<td class='mono'>").Append(HtmlEncode(historyEntry.TimestampUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture))).Append("</td>");
            stringBuilder.Append("<td class='mono'>").Append(HtmlEncode(historyEntry.Client)).Append("</td>");
            stringBuilder.Append("<td>").Append(HtmlEncode(historyEntry.Protocol)).Append("</td>");
            stringBuilder.Append("<td class='mono'>").Append(HtmlEncode($"{historyEntry.QuestionName} {historyEntry.QuestionType}")).Append("</td>");
            stringBuilder.Append("<td>").Append(HtmlEncode(historyEntry.Result)).Append("</td>");
            stringBuilder.Append("<td class='mono'>").Append(HtmlEncode(historyEntry.Upstream)).Append("</td>");
            stringBuilder.Append("<td>").Append(HtmlEncode(historyEntry.LatencyMs.HasValue ? historyEntry.LatencyMs.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms" : "-")).Append("</td>");
            stringBuilder.Append("<td>").Append(HtmlEncode(historyEntry.ResponseCode)).Append("</td>");
            stringBuilder.Append("<td class='mono'>").Append(HtmlEncode(string.Join("; ", historyEntry.Answers))).Append("</td>");
            stringBuilder.Append("</tr>");
        }

        stringBuilder.Append("""
              </tbody>
            </table>
            </body>
            </html>
            """);
        return stringBuilder.ToString();
    }

    private static string FormatCustomRecordValues(CustomDnsRecordOption option)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(option.Value))
        {
            values.Add(option.Value);
        }

        values.AddRange(option.Values.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.Join(", ", values);
    }

    private static string HtmlEncode(string value) => System.Net.WebUtility.HtmlEncode(value);
}
