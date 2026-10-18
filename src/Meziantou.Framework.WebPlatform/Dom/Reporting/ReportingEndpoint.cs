using System.Text.Json;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A location to which the reports of a document or a worker are sent (https://w3c.github.io/reporting/#endpoint).</summary>
internal sealed class ReportingEndpoint(string name, UrlRecord url)
{
    /// <summary>https://w3c.github.io/reporting/#endpoint-name</summary>
    public string Name { get; } = name;

    /// <summary>https://w3c.github.io/reporting/#endpoint-url</summary>
    public UrlRecord Url { get; } = url;

    /// <summary>The number of consecutive failed deliveries (https://w3c.github.io/reporting/#endpoint-failures).</summary>
    public int Failures { get; set; }

    /// <summary>
    /// The endpoints of a response (https://w3c.github.io/reporting/#process-header): the members of the
    /// <c>Reporting-Endpoints</c> dictionary whose value is a string that is a potentially trustworthy URL (resolved against
    /// the URL of the response). Without a <c>Reporting-Endpoints</c> header, the groups of the legacy <c>Report-To</c> header
    /// (https://www.w3.org/TR/2018/WD-reporting-1-20180925/#header) are used, as in Chromium: the first URL of each group.
    /// A response whose URL is not potentially trustworthy has no endpoint.
    /// </summary>
    public static List<ReportingEndpoint>? Process(IReadOnlyList<KeyValuePair<string, string>> headers, UrlRecord responseUrl)
    {
        // Step 1: the HTTPS state of the response is "modern" (an https: response), or its origin is potentially trustworthy
        if (!ReferrerPolicies.IsOriginPotentiallyTrustworthy(responseUrl))
            return null;

        // Step 2
        if (StructuredFields.GetDictionary(headers, "Reporting-Endpoints") is not { } parsedHeader)
            return ProcessReportToHeader(headers, responseUrl);

        // Steps 3-5
        List<ReportingEndpoint>? endpoints = null;
        foreach (var (name, member) in parsedHeader)
        {
            if (member.Item?.Value is not string endpointUrlString)
                continue;

            if (UrlParser.Parse(endpointUrlString, responseUrl) is not { } endpointUrl || !ReferrerPolicies.IsOriginPotentiallyTrustworthy(endpointUrl))
                continue;

            endpoints ??= [];
            endpoints.Add(new ReportingEndpoint(name, endpointUrl));
        }

        return endpoints;
    }

    /// <summary>
    /// The legacy <c>Report-To</c> header: a list of JSON objects (combined by commas) whose <c>group</c> member names the
    /// endpoint (<c>default</c> by default) and whose <c>endpoints</c> member lists the URLs of the group. The first potentially
    /// trustworthy URL of a group is its endpoint; the other members (<c>max_age</c>, <c>include_subdomains</c>, the
    /// priorities and the weights) are ignored: the endpoints belong to the document or the worker, as the endpoints of the
    /// <c>Reporting-Endpoints</c> header. A group named twice keeps its first definition.
    /// </summary>
    private static List<ReportingEndpoint>? ProcessReportToHeader(IReadOnlyList<KeyValuePair<string, string>> headers, UrlRecord responseUrl)
    {
        if (HeaderList.Get(headers, "Report-To") is not { } value)
            return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse("[" + value + "]");
        }
        catch (JsonException)
        {
            return null;
        }

        List<ReportingEndpoint>? endpoints = null;
        using (document)
        {
            foreach (var group in document.RootElement.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("endpoints", out var groupEndpoints) || groupEndpoints.ValueKind != JsonValueKind.Array)
                    continue;

                var name = group.TryGetProperty("group", out var groupName) && groupName.ValueKind == JsonValueKind.String ? groupName.GetString()! : "default";
                if (endpoints is not null && endpoints.Exists(endpoint => endpoint.Name == name))
                    continue;

                foreach (var endpoint in groupEndpoints.EnumerateArray())
                {
                    if (endpoint.ValueKind == JsonValueKind.Object && endpoint.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
                        && UrlParser.Parse(url.GetString()!, responseUrl) is { } endpointUrl && ReferrerPolicies.IsOriginPotentiallyTrustworthy(endpointUrl))
                    {
                        endpoints ??= [];
                        endpoints.Add(new ReportingEndpoint(name, endpointUrl));
                        break;
                    }
                }
            }
        }

        return endpoints;
    }
}
