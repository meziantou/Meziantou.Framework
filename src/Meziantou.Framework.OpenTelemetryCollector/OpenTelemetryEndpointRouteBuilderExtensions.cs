using Google.Protobuf;
using Meziantou.Framework.OpenTelemetryCollector.Abstractions.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<OpenTelemetryReceiverOptions>>().Value;

        if (options.HttpLogsEndpoint is not null)
        {
            endpoints.MapPost(options.HttpLogsEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleLogsHttpRequestAsync(request, pipeline, cancellationToken));
        }

        if (options.HttpTracesEndpoint is not null)
        {
            endpoints.MapPost(options.HttpTracesEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleTracesHttpRequestAsync(request, pipeline, cancellationToken));
        }

        if (options.HttpMetricsEndpoint is not null)
        {
            endpoints.MapPost(options.HttpMetricsEndpoint, (HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken) =>
                HandleMetricsHttpRequestAsync(request, pipeline, cancellationToken));
        }

        if (options.EnableGrpcEndpoints)
        {
            endpoints.MapGrpcService<OpenTelemetryLogsGrpcService>();
            endpoints.MapGrpcService<OpenTelemetryTracesGrpcService>();
            endpoints.MapGrpcService<OpenTelemetryMetricsGrpcService>();
        }

        return endpoints;
    }

    private static Task<IResult> HandleLogsHttpRequestAsync(HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken)
    {
        return HandleHttpRequestAsync(
            request,
            ExportLogsServiceRequest.Parser,
            (context, payload, ct) => pipeline.HandleLogsAsync(context, payload, ct),
            static () => new ExportLogsServiceResponse(),
            cancellationToken);
    }

    private static Task<IResult> HandleTracesHttpRequestAsync(HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken)
    {
        return HandleHttpRequestAsync(
            request,
            ExportTraceServiceRequest.Parser,
            (context, payload, ct) => pipeline.HandleTracesAsync(context, payload, ct),
            static () => new ExportTraceServiceResponse(),
            cancellationToken);
    }

    private static Task<IResult> HandleMetricsHttpRequestAsync(HttpRequest request, OpenTelemetryRequestPipeline pipeline, CancellationToken cancellationToken)
    {
        return HandleHttpRequestAsync(
            request,
            ExportMetricsServiceRequest.Parser,
            (context, payload, ct) => pipeline.HandleMetricsAsync(context, payload, ct),
            static () => new ExportMetricsServiceResponse(),
            cancellationToken);
    }

    private static async Task<IResult> HandleHttpRequestAsync<TRequest, TResponse>(
        HttpRequest request,
        MessageParser<TRequest> parser,
        Func<OpenTelemetryHandlerContext, TRequest, CancellationToken, ValueTask> handler,
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
        await handler(context, payload, cancellationToken);

        return TypedResults.Ok(responseFactory());
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
}
