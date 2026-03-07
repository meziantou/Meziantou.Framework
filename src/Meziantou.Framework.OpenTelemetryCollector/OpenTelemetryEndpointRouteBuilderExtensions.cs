using Google.Protobuf;
using Meziantou.AspNetCore.OpenTelemetryCollector;
using Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

public static class OpenTelemetryEndpointRouteBuilderExtensions
{
    private const string ProtobufContentType = "application/x-protobuf";
    private const string OctetStreamContentType = "application/octet-stream";

    public static IEndpointRouteBuilder MapOpenTelemetryReceiverEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<OpenTelemetryOptions>();

        if (options.HttpLogsEndpoint is not null)
        {
            endpoints.MapPost(options.HttpLogsEndpoint, (HttpRequest request, IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations, CancellationToken cancellationToken) =>
                HandleLogsHttpRequestAsync(request, receiverRegistrations, cancellationToken));
        }

        if (options.HttpTracesEndpoint is not null)
        {
            endpoints.MapPost(options.HttpTracesEndpoint, (HttpRequest request, IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations, CancellationToken cancellationToken) =>
                HandleTracesHttpRequestAsync(request, receiverRegistrations, cancellationToken));
        }

        if (options.HttpMetricsEndpoint is not null)
        {
            endpoints.MapPost(options.HttpMetricsEndpoint, (HttpRequest request, IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations, CancellationToken cancellationToken) =>
                HandleMetricsHttpRequestAsync(request, receiverRegistrations, cancellationToken));
        }

        if (options.EnableGrpcEndpoints)
        {
            endpoints.MapGrpcService<OpenTelemetryLogsGrpcService>();
            endpoints.MapGrpcService<OpenTelemetryTracesGrpcService>();
            endpoints.MapGrpcService<OpenTelemetryMetricsGrpcService>();
        }

        return endpoints;
    }

    private static Task<IResult> HandleLogsHttpRequestAsync(HttpRequest request, IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations, CancellationToken cancellationToken)
    {
        return HandleHttpRequestAsync(
            request,
            receiverRegistrations,
            ExportLogsServiceRequest.Parser,
            static (context, payload, ct) => context.Receiver.HandleLogsAsync(context.Context, payload, ct),
            static () => new ExportLogsServiceResponse(),
            cancellationToken);
    }

    private static Task<IResult> HandleTracesHttpRequestAsync(HttpRequest request, IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations, CancellationToken cancellationToken)
    {
        return HandleHttpRequestAsync(
            request,
            receiverRegistrations,
            ExportTraceServiceRequest.Parser,
            static (context, payload, ct) => context.Receiver.HandleTracesAsync(context.Context, payload, ct),
            static () => new ExportTraceServiceResponse(),
            cancellationToken);
    }

    private static Task<IResult> HandleMetricsHttpRequestAsync(HttpRequest request, IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations, CancellationToken cancellationToken)
    {
        return HandleHttpRequestAsync(
            request,
            receiverRegistrations,
            ExportMetricsServiceRequest.Parser,
            static (context, payload, ct) => context.Receiver.HandleMetricsAsync(context.Context, payload, ct),
            static () => new ExportMetricsServiceResponse(),
            cancellationToken);
    }

    private static async Task<IResult> HandleHttpRequestAsync<TRequest, TResponse>(
        HttpRequest request,
        IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations,
        MessageParser<TRequest> parser,
        Func<HandlerContext, TRequest, CancellationToken, ValueTask> handler,
        Func<TResponse> responseFactory,
        CancellationToken cancellationToken)
        where TRequest : class, IMessage<TRequest>
    {
        if (!IsSupportedContentType(request.ContentType))
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        TRequest payload;
        try
        {
            payload = await ParsePayloadAsync(parser, request, cancellationToken);
        }
        catch (InvalidProtocolBufferException)
        {
            return TypedResults.BadRequest();
        }

        var method = $"{request.Method} {request.Path}";
        var context = OpenTelemetryHandlerContext.CreateHttp(method);
        foreach (var receiver in GetReceivers(receiverRegistrations))
        {
            await handler(new HandlerContext(receiver, context), payload, cancellationToken);
        }

        return TypedResults.Ok(responseFactory());
    }

    private static OpenTelemetryHandler[] GetReceivers(IEnumerable<OpenTelemetryHandlerRegistration> receiverRegistrations)
    {
        ArgumentNullException.ThrowIfNull(receiverRegistrations);

        var receivers = receiverRegistrations.Select(static item => item.Handler).ToArray();
        if (receivers.Length is 0)
        {
            throw new InvalidOperationException($"No OpenTelemetry receivers are registered. Use {nameof(OpenTelemetryServiceCollectionExtensions)}.{nameof(OpenTelemetryServiceCollectionExtensions.AddOpenTelemetryReceiver)}(...).");
        }

        return receivers;
    }

    private static bool IsSupportedContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
        {
            return true;
        }

        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaTypeHeaderValue))
        {
            return false;
        }

        var mediaType = mediaTypeHeaderValue.MediaType.Value;
        return string.Equals(mediaType, ProtobufContentType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(mediaType, OctetStreamContentType, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<TRequest> ParsePayloadAsync<TRequest>(MessageParser<TRequest> parser, HttpRequest request, CancellationToken cancellationToken)
        where TRequest : class, IMessage<TRequest>
    {
        await using var stream = new MemoryStream();
        await request.Body.CopyToAsync(stream, cancellationToken);
        return parser.ParseFrom(stream.ToArray());
    }

    private readonly record struct HandlerContext(OpenTelemetryHandler Receiver, OpenTelemetryHandlerContext Context);
}
