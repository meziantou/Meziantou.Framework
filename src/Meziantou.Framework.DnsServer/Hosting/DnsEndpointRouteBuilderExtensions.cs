using System.Buffers.Text;
using System.Globalization;
using System.Net;
using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Extension methods for mapping DNS endpoints on an <see cref="IEndpointRouteBuilder"/>.</summary>
public static class DnsEndpointRouteBuilderExtensions
{
    private const string DnsMessageContentType = "application/dns-message";

    /// <summary>A DNS message cannot exceed this size, so anything larger is rejected before it is buffered.</summary>
    private const int MaxDnsMessageSize = DnsMessageEncoder.MaxMessageSize;

    /// <summary>The longest base64url string that can hold a DNS message of the maximum size.</summary>
    private const int MaxEncodedQueryLength = ((MaxDnsMessageSize + 2) / 3 * 4) + 4;

    /// <summary>Registers the DNS request handler delegate. This must be called before the application starts.</summary>
    public static IEndpointRouteBuilder MapDnsHandler(this IEndpointRouteBuilder endpoints, DnsRequestDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);

        var registry = endpoints.ServiceProvider.GetRequiredService<DnsRequestDelegateHolder>();
        registry.SetHandler(handler);

        return endpoints;
    }

    /// <summary>Maps a DNS over HTTPS endpoint at the specified path pattern.</summary>
    public static IEndpointConventionBuilder MapDnsOverHttps(this IEndpointRouteBuilder endpoints, string pattern = "/dns-query")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var postEndpoint = endpoints.MapPost(pattern, (Delegate)HandleDnsOverHttpsPostAsync);
        var getEndpoint = endpoints.MapGet(pattern, (Delegate)HandleDnsOverHttpsGetAsync);

        // Return a composite that applies conventions to both
        return new CompositeEndpointConventionBuilder([postEndpoint, getEndpoint]);
    }

    private static async Task<IResult> HandleDnsOverHttpsPostAsync(HttpContext httpContext)
    {
        if (!IsDnsContentType(httpContext.Request.ContentType))
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        if (httpContext.Request.ContentLength > MaxDnsMessageSize)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Cap the body even when no Content-Length was sent, so a chunked request cannot make the
        // server buffer far more than a DNS message can ever hold.
        var maxSizeFeature = httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxSizeFeature is { IsReadOnly: false })
        {
            maxSizeFeature.MaxRequestBodySize = MaxDnsMessageSize;
        }

        byte[] queryBytes;
        using (var ms = new MemoryStream())
        {
            try
            {
                await httpContext.Request.Body.CopyToAsync(ms, httpContext.RequestAborted).ConfigureAwait(false);
            }
            catch (BadHttpRequestException)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            queryBytes = ms.ToArray();
        }

        return await ProcessDnsQueryAsync(queryBytes, httpContext).ConfigureAwait(false);
    }

    private static async Task<IResult> HandleDnsOverHttpsGetAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Query.TryGetValue("dns", out var dnsParam) || dnsParam.Count is 0 || dnsParam[0] is not { } encodedQuery)
        {
            return Results.BadRequest("Missing 'dns' query parameter.");
        }

        if (encodedQuery.Length > MaxEncodedQueryLength)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Base64url decoding (RFC 8484)
        var queryBytes = new byte[Base64Url.GetMaxDecodedLength(encodedQuery.Length)];
        if (!Base64Url.TryDecodeFromChars(encodedQuery, queryBytes, out var decodedLength))
        {
            return Results.BadRequest("Invalid base64url encoding in 'dns' query parameter.");
        }

        return await ProcessDnsQueryAsync(queryBytes.AsMemory(0, decodedLength), httpContext).ConfigureAwait(false);
    }

    private static async Task<IResult> ProcessDnsQueryAsync(ReadOnlyMemory<byte> queryBytes, HttpContext httpContext)
    {
        var processor = httpContext.RequestServices.GetRequiredService<DnsRequestProcessor>();

        var remoteEndPoint = httpContext.Connection.RemoteIpAddress is not null
            ? new IPEndPoint(httpContext.Connection.RemoteIpAddress, httpContext.Connection.RemotePort)
            : new IPEndPoint(IPAddress.Loopback, 0);

        // HTTP can report a bad request directly, so there is no need to answer with a FORMERR message.
        var responseBytes = await processor.ProcessAsync(queryBytes, DnsServerProtocol.Https, remoteEndPoint, MaxDnsMessageSize, httpContext.RequestAborted, replyWithFormatError: false).ConfigureAwait(false);
        if (responseBytes is null)
        {
            return Results.BadRequest("Invalid DNS message.");
        }

        // RFC 8484 5.1: the response is cacheable for as long as its shortest record TTL.
        httpContext.Response.Headers.CacheControl = GetCacheControl(responseBytes);

        return Results.Bytes(responseBytes, DnsMessageContentType);
    }

    private static string GetCacheControl(byte[] responseBytes)
    {
        uint? minimumTtl = null;
        try
        {
            var response = DnsMessageEncoder.DecodeQuery(responseBytes);
            foreach (var record in response.Answers.Concat(response.Authorities).Concat(response.AdditionalRecords))
            {
                minimumTtl = minimumTtl is { } current ? Math.Min(current, record.TimeToLive) : record.TimeToLive;
            }
        }
        catch (DnsProtocolException)
        {
            // Fall through to no-store rather than guessing at a lifetime.
        }

        return minimumTtl is { } ttl and > 0
            ? "max-age=" + ttl.ToString(CultureInfo.InvariantCulture)
            : "no-store";
    }

    private static bool IsDnsContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        return contentType.StartsWith(DnsMessageContentType, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CompositeEndpointConventionBuilder : IEndpointConventionBuilder
    {
        private readonly IEndpointConventionBuilder[] _builders;

        public CompositeEndpointConventionBuilder(IEndpointConventionBuilder[] builders)
        {
            _builders = builders;
        }

        public void Add(Action<EndpointBuilder> convention)
        {
            foreach (var builder in _builders)
            {
                builder.Add(convention);
            }
        }

        public void Finally(Action<EndpointBuilder> finalConvention)
        {
            foreach (var builder in _builders)
            {
                builder.Finally(finalConvention);
            }
        }
    }
}
