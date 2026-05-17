using System.Diagnostics.Metrics;

namespace Meziantou.Framework.Http.ServerSideRequestForgery;

internal static class ServerSideRequestForgeryMetrics
{
    internal const string MeterName = "Meziantou.Framework.Http.ServerSideRequestForgery";
    internal const string MeterVersion = "1.0.0";
    internal const string RejectedRequestsCounterName = "meziantou.framework.http.server_side_request_forgery.rejected_requests.total";
    internal const string ReasonTagName = "reason";

    private static readonly Meter Meter = new(MeterName, MeterVersion);
    private static readonly Counter<long> RejectedRequestsCounter = Meter.CreateCounter<long>(
        name: RejectedRequestsCounterName,
        unit: "{requests}",
        description: "Number of outbound requests rejected by SSRF protections.");

    internal static void IncrementRejectedRequest(string reason)
    {
        RejectedRequestsCounter.Add(1, new KeyValuePair<string, object?>(ReasonTagName, reason));
    }
}
