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
    public static IEndpointRouteBuilder MapOpenTelemetryReceiverEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<OpenTelemetryReceiverOptions>>().Value;
        var maxRequestBodySize = options.MaxHttpRequestBodySize;

        if (options.HttpLogsEndpoint is not null)
        {
            endpoints.MapPost(options.HttpLogsEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleHttpRequestAsync(
                    request,
                    ExportLogsServiceRequest.Parser,
                    (context, payload, ct) => pipeline.HandleLogsAsync(context, payload, ct),
                    OpenTelemetryResponseFactory.CreateLogsResponse,
                    maxRequestBodySize,
                    cancellationToken));
        }

        if (options.HttpTracesEndpoint is not null)
        {
            endpoints.MapPost(options.HttpTracesEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleHttpRequestAsync(
                    request,
                    ExportTraceServiceRequest.Parser,
                    (context, payload, ct) => pipeline.HandleTracesAsync(context, payload, ct),
                    OpenTelemetryResponseFactory.CreateTracesResponse,
                    maxRequestBodySize,
                    cancellationToken));
        }

        if (options.HttpMetricsEndpoint is not null)
        {
            endpoints.MapPost(options.HttpMetricsEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleHttpRequestAsync(
                    request,
                    ExportMetricsServiceRequest.Parser,
                    (context, payload, ct) => pipeline.HandleMetricsAsync(context, payload, ct),
                    OpenTelemetryResponseFactory.CreateMetricsResponse,
                    maxRequestBodySize,
                    cancellationToken));
        }

        if (options.EnableGrpcEndpoints)
        {
            endpoints.MapGrpcService<OpenTelemetryLogsGrpcService>();
            endpoints.MapGrpcService<OpenTelemetryTracesGrpcService>();
            endpoints.MapGrpcService<OpenTelemetryMetricsGrpcService>();
        }

        return endpoints;
    }

    private static async Task<IResult> HandleHttpRequestAsync<TRequest, TResponse>(
        HttpRequest request,
        MessageParser<TRequest> parser,
        Func<OpenTelemetryHandlerContext, TRequest, CancellationToken, ValueTask> handler,
        Func<OpenTelemetryPartialSuccess, TResponse> responseFactory,
        long? maxRequestBodySize,
        CancellationToken cancellationToken)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>
    {
        if (!OpenTelemetryHttpPayload.TryGetPayloadFormat(request.ContentType, out var format) ||
            !OpenTelemetryHttpPayload.TryGetContentEncoding(request, out var decompressGzip))
        {
            return TypedResults.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var (payload, errorStatusCode) = await OpenTelemetryHttpPayload.ReadPayloadAsync(request, decompressGzip, maxRequestBodySize, cancellationToken);
        if (payload is null)
        {
            return TypedResults.StatusCode(errorStatusCode);
        }

        TRequest message;
        try
        {
            message = OpenTelemetryHttpPayload.Parse(parser, format, payload);
        }
        catch (Exception exception) when (exception is InvalidProtocolBufferException or InvalidJsonException or JsonException)
        {
            return TypedResults.BadRequest();
        }

        var partialSuccess = new OpenTelemetryPartialSuccess();
        var method = $"{request.Method} {request.Path}";
        var context = OpenTelemetryHandlerContext.CreateHttp(method, partialSuccess);
        await handler(context, message, cancellationToken);

        return new OpenTelemetryProtoResult<TResponse>(responseFactory(partialSuccess), format);
    }
}
