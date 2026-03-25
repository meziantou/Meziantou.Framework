using Meziantou.Framework.DnsServer.Handler;
using Meziantou.Framework.DnsServer.Protocol;
using Meziantou.Framework.DnsServer.Protocol.Wire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.Framework.DnsServer.Hosting;

/// <summary>Extension methods for mapping DNS endpoints on an <see cref="IEndpointRouteBuilder"/>.</summary>
public static class DnsEndpointRouteBuilderExtensions
{
    private const string DnsMessageContentType = "application/dns-message";

    /// <summary>Registers the DNS request handler delegate. This must be called before the application starts.</summary>
    public static IEndpointRouteBuilder MapDnsHandler(this IEndpointRouteBuilder endpoints, DnsRequestDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(handler);

        var registry = endpoints.ServiceProvider.GetRequiredService<DnsRequestDelegateHolder>();
        registry.Handler = handler;

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

        var handler = httpContext.RequestServices.GetRequiredService<DnsRequestDelegateHolder>();

        using var ms = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(ms, httpContext.RequestAborted).ConfigureAwait(false);
        var queryBytes = ms.ToArray();

        return await ProcessDnsQueryAsync(handler.Handler, queryBytes, httpContext).ConfigureAwait(false);
    }

    private static async Task<IResult> HandleDnsOverHttpsGetAsync(HttpContext httpContext)
    {
        var handler = httpContext.RequestServices.GetRequiredService<DnsRequestDelegateHolder>();

        if (!httpContext.Request.Query.TryGetValue("dns", out var dnsParam) || dnsParam.Count is 0)
        {
            return Results.BadRequest("Missing 'dns' query parameter.");
        }

        byte[] queryBytes;
        try
        {
            // Base64url decoding (RFC 8484)
            var base64 = dnsParam[0]!
                .Replace('-', '+')
                .Replace('_', '/');

            // Add padding if necessary
            var paddingNeeded = (4 - (base64.Length % 4)) % 4;
            base64 += new string('=', paddingNeeded);

            queryBytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return Results.BadRequest("Invalid base64url encoding in 'dns' query parameter.");
        }

        return await ProcessDnsQueryAsync(handler.Handler, queryBytes, httpContext).ConfigureAwait(false);
    }

    private static async Task<IResult> ProcessDnsQueryAsync(DnsRequestDelegate handler, byte[] queryBytes, HttpContext httpContext)
    {
        DnsMessage query;
        try
        {
            query = DnsMessageEncoder.DecodeQuery(queryBytes);
        }
        catch (DnsProtocolException)
        {
            return Results.BadRequest("Invalid DNS message.");
        }

        var context = new DnsRequestContext(query, DnsServerProtocol.Https, httpContext.Connection.RemoteIpAddress is not null
            ? new System.Net.IPEndPoint(httpContext.Connection.RemoteIpAddress, httpContext.Connection.RemotePort)
            : new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));

        var response = await handler(context, httpContext.RequestAborted).ConfigureAwait(false);
        var responseBytes = DnsMessageEncoder.EncodeResponse(response);

        return Results.Bytes(responseBytes, DnsMessageContentType);
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
