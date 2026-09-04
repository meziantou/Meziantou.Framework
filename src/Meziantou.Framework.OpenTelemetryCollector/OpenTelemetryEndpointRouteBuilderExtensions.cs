using System.Text.Json;
using Google.Protobuf;
using Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector;

public static class OpenTelemetryEndpointRouteBuilderExtensions
{
    /// <summary>Maps the OTLP receiver endpoints configured in <see cref="OpenTelemetryReceiverOptions"/>.</summary>
    /// <returns>
    /// A builder that applies conventions to every mapped endpoint, so authorization, rate limiting or CORS can be
    /// attached to them: <c>app.MapOpenTelemetryReceiverEndpoints().RequireAuthorization()</c>.
    /// </returns>
    public static IEndpointConventionBuilder MapOpenTelemetryReceiverEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<OpenTelemetryReceiverOptions>>().Value;
        var builders = new List<IEndpointConventionBuilder>();

        if (options.HttpLogsEndpoint is not null)
        {
            builders.Add(endpoints.MapPost(options.HttpLogsEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleHttpRequestAsync(
                    request,
                    ExportLogsServiceRequest.Parser,
                    (context, payload, ct) => pipeline.HandleLogsAsync(context, payload, ct),
                    OpenTelemetryResponseFactory.CreateLogsResponse,
                    cancellationToken)));
        }

        if (options.HttpTracesEndpoint is not null)
        {
            builders.Add(endpoints.MapPost(options.HttpTracesEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleHttpRequestAsync(
                    request,
                    ExportTraceServiceRequest.Parser,
                    (context, payload, ct) => pipeline.HandleTracesAsync(context, payload, ct),
                    OpenTelemetryResponseFactory.CreateTracesResponse,
                    cancellationToken)));
        }

        if (options.HttpMetricsEndpoint is not null)
        {
            builders.Add(endpoints.MapPost(options.HttpMetricsEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleHttpRequestAsync(
                    request,
                    ExportMetricsServiceRequest.Parser,
                    (context, payload, ct) => pipeline.HandleMetricsAsync(context, payload, ct),
                    OpenTelemetryResponseFactory.CreateMetricsResponse,
                    cancellationToken)));
        }

        if (options.EnableGrpcEndpoints)
        {
            builders.Add(endpoints.MapGrpcService<OpenTelemetryLogsGrpcService>());
            builders.Add(endpoints.MapGrpcService<OpenTelemetryTracesGrpcService>());
            builders.Add(endpoints.MapGrpcService<OpenTelemetryMetricsGrpcService>());
        }

        return new OpenTelemetryEndpointConventionBuilder(builders);
    }

    private static async Task<IResult> HandleHttpRequestAsync<TRequest, TResponse>(
        HttpRequest request,
        MessageParser<TRequest> parser,
        Func<OpenTelemetryHandlerContext, TRequest, CancellationToken, ValueTask> handler,
        Func<OpenTelemetryPartialSuccess, TResponse> responseFactory,
        CancellationToken cancellationToken)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>
    {
        if (!OpenTelemetryHttpPayload.TryGetPayloadFormat(request.ContentType, out var format))
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        TRequest message;
        try
        {
            using var payload = await OpenTelemetryHttpPayload.ReadPayloadAsync(request, cancellationToken);
            message = OpenTelemetryHttpPayload.Parse(parser, format, payload);
        }
        catch (Exception exception) when (exception is InvalidProtocolBufferException or InvalidJsonException or JsonException or InvalidDataException)
        {
            // The payload is malformed, or the request decompression middleware could not decompress it
            return TypedResults.BadRequest();
        }

        var partialSuccess = new OpenTelemetryPartialSuccess();
        var method = $"{request.Method} {request.Path}";
        var context = OpenTelemetryHandlerContext.CreateHttp(method, partialSuccess);
        await handler(context, message, cancellationToken);

        return new OpenTelemetryProtoResult<TResponse>(responseFactory(partialSuccess), format);
    }
}
