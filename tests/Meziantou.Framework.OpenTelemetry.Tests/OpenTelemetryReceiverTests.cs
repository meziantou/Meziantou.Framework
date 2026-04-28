using System.Net;
using System.Net.Http.Headers;
using System.Linq;
using Google.Protobuf;
using Grpc.Net.Client;
using Meziantou.Framework.OpenTelemetryCollector.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.Framework.OpenTelemetryCollector.Tests;

public sealed class OpenTelemetryReceiverTests
{
    [Fact]
    public async Task Http_LogsEndpoint_StoresTypedRequest()
    {
        await using var app = await TestApplication.CreateAsync();

        var payload = new ExportLogsServiceRequest();
        payload.ResourceLogs.Add(new global::OpenTelemetry.Proto.Logs.V1.ResourceLogs());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/logs");
        using var content = new ByteArrayContent(payload.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        request.Content = content;

        using var response = await app.HttpClient.SendAsync(request, XunitCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.IsType<OpenTelemetryLogsItem>(Assert.Single(app.Receiver.Logs));
        Assert.Equal(OpenTelemetryItemType.Logs, item.ItemType);
        _ = Assert.Single(item.Request.ResourceLogs);
        Assert.Equal("POST /v1/logs", item.Method);
        Assert.NotSame(payload, item.Request);
    }

    [Fact]
    public async Task Http_UnsupportedContentType_Returns415()
    {
        await using var app = await TestApplication.CreateAsync();

        using var content = new StringContent("{}");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await app.HttpClient.PostAsync("/v1/logs", content, XunitCancellationToken);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(app.Receiver.Logs);
    }

    [Fact]
    public async Task Http_InvalidPayload_Returns400()
    {
        await using var app = await TestApplication.CreateAsync();

        using var content = new ByteArrayContent([0x00, 0xFF, 0xA5]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        using var response = await app.HttpClient.PostAsync("/v1/traces", content, XunitCancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Receiver.Traces);
    }

    [Fact]
    public async Task Grpc_LogsEndpoint_StoresTypedRequest()
    {
        await using var app = await TestApplication.CreateAsync();

        var payload = new ExportLogsServiceRequest();
        payload.ResourceLogs.Add(new global::OpenTelemetry.Proto.Logs.V1.ResourceLogs());

        var client = new LogsService.LogsServiceClient(app.GrpcChannel);
        _ = await client.ExportAsync(payload, cancellationToken: XunitCancellationToken).ResponseAsync;

        var item = Assert.IsType<OpenTelemetryLogsItem>(Assert.Single(app.Receiver.Logs));
        Assert.Equal(OpenTelemetryItemType.Logs, item.ItemType);
        _ = Assert.Single(item.Request.ResourceLogs);
        Assert.Equal("/opentelemetry.proto.collector.logs.v1.LogsService/Export", item.Method);
    }

    [Fact]
    public async Task MultipleReceivers_CanBeConfigured()
    {
        var secondReceiver = new TestReceiver();
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.AddOpenTelemetryReceiver(_ => secondReceiver));

        await SendLogsAsync(app.HttpClient, "from-http");

        _ = Assert.Single(app.Receiver.Logs);
        Assert.Equal(1, secondReceiver.ReceivedLogsCount);
    }

    [Fact]
    public async Task Grpc_TraceEndpoint_StoresTypedRequest()
    {
        await using var app = await TestApplication.CreateAsync();

        var payload = new ExportTraceServiceRequest();
        payload.ResourceSpans.Add(new global::OpenTelemetry.Proto.Trace.V1.ResourceSpans());

        var client = new TraceService.TraceServiceClient(app.GrpcChannel);
        _ = await client.ExportAsync(payload, cancellationToken: XunitCancellationToken).ResponseAsync;

        var item = Assert.IsType<OpenTelemetryTracesItem>(Assert.Single(app.Receiver.Traces));
        Assert.Equal(OpenTelemetryItemType.Traces, item.ItemType);
        _ = Assert.Single(item.Request.ResourceSpans);
    }

    [Fact]
    public async Task Grpc_MetricsEndpoint_StoresTypedRequest()
    {
        await using var app = await TestApplication.CreateAsync();

        var payload = new ExportMetricsServiceRequest();
        payload.ResourceMetrics.Add(new global::OpenTelemetry.Proto.Metrics.V1.ResourceMetrics());

        var client = new MetricsService.MetricsServiceClient(app.GrpcChannel);
        _ = await client.ExportAsync(payload, cancellationToken: XunitCancellationToken).ResponseAsync;

        var item = Assert.IsType<OpenTelemetryMetricsItem>(Assert.Single(app.Receiver.Metrics));
        Assert.Equal(OpenTelemetryItemType.Metrics, item.ItemType);
        _ = Assert.Single(item.Request.ResourceMetrics);
    }

    [Fact]
    public async Task InMemoryReceiver_UsesConfiguredRetentionStrategy()
    {
        await using var app = await TestApplication.CreateAsync(new InMemoryOpenTelemetryHandlerOptions
        {
            MaximumLogCount = 2,
        });

        await SendLogsAsync(app.HttpClient, "first");
        await SendLogsAsync(app.HttpClient, "second");
        await SendLogsAsync(app.HttpClient, "third");

        var items = app.Receiver.Logs.ToList();
        Assert.Equal(2, items.Count);

        var first = Assert.IsType<OpenTelemetryLogsItem>(items[0]);
        var second = Assert.IsType<OpenTelemetryLogsItem>(items[1]);
        Assert.Equal("second", first.Request.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Body.StringValue);
        Assert.Equal("third", second.Request.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Body.StringValue);
    }

    [Fact]
    public async Task InMemoryReceiver_DefaultRetentionIsNoop()
    {
        await using var app = await TestApplication.CreateAsync();

        await SendLogsAsync(app.HttpClient, "first");
        await SendLogsAsync(app.HttpClient, "second");
        await SendLogsAsync(app.HttpClient, "third");

        Assert.Equal(3, app.Receiver.Logs.Count());
    }

    private static async Task SendLogsAsync(HttpClient httpClient, string body, string endpoint = "/v1/logs")
    {
        var payload = new ExportLogsServiceRequest();
        payload.ResourceLogs.Add(new global::OpenTelemetry.Proto.Logs.V1.ResourceLogs
        {
            ScopeLogs =
            {
                new global::OpenTelemetry.Proto.Logs.V1.ScopeLogs
                {
                    LogRecords =
                    {
                        new global::OpenTelemetry.Proto.Logs.V1.LogRecord
                        {
                            Body = new global::OpenTelemetry.Proto.Common.V1.AnyValue
                            {
                                StringValue = body,
                            },
                        },
                    },
                },
            },
        });

        using var content = new ByteArrayContent(payload.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        using var response = await httpClient.PostAsync(endpoint, content, XunitCancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed class TestReceiver : OpenTelemetryHandler
    {
        public int ReceivedLogsCount { get; private set; }

        public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReceivedLogsCount++;
            return ValueTask.CompletedTask;
        }

        public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestApplication(WebApplication app, HttpClient httpClient, GrpcChannel grpcChannel, InMemoryOpenTelemetryHandler receiver) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;

        public HttpClient HttpClient { get; } = httpClient;

        public GrpcChannel GrpcChannel { get; } = grpcChannel;

        public InMemoryOpenTelemetryHandler Receiver { get; } = receiver;

        public static async Task<TestApplication> CreateAsync(InMemoryOpenTelemetryHandlerOptions? options = null, Action<IServiceCollection>? configureServices = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Services.AddInMemoryOpenTelemetryReceiver(options);

            configureServices?.Invoke(builder.Services);

            var app = builder.Build();
            app.MapOpenTelemetryReceiverEndpoints();

            await app.StartAsync(XunitCancellationToken);

            var receiver = app.Services.GetRequiredService<InMemoryOpenTelemetryHandler>();
            var httpClient = app.GetTestClient();
            var grpcChannel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = app.GetTestServer().CreateHandler(),
            });

            return new TestApplication(app, httpClient, grpcChannel, receiver);
        }

        public async ValueTask DisposeAsync()
        {
            GrpcChannel.Dispose();
            HttpClient.Dispose();
            await App.DisposeAsync();
        }
    }
}
