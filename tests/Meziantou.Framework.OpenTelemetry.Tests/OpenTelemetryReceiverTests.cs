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
    public async Task Http_LogsFilter_DropsPayload()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new DenyLogsSampler());
        }));

        await SendLogsAsync(app.HttpClient, "ignored");

        Assert.Empty(app.Receiver.Logs);
    }

    [Fact]
    public async Task Grpc_TracesFilter_DropsPayload()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new DenyTracesSampler());
        }));

        var payload = CreateTraceRequest("00000000000000000000000000000011", ("0000000000000011", null, "root"));
        var client = new TraceService.TraceServiceClient(app.GrpcChannel);
        _ = await client.ExportAsync(payload, cancellationToken: XunitCancellationToken).ResponseAsync;

        Assert.Empty(app.Receiver.Traces);
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
    public async Task Http_TailFilter_RootArrivesLast()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampling
            {
                ShouldSample = static (context, _) => ValueTask.FromResult(context.RootSpan?.Name == "root-keep"),
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000021", ("0000000000000022", "0000000000000021", "child")));
        Assert.Empty(app.Receiver.Traces);

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000021", ("0000000000000021", null, "root-keep")));

        var spans = GetTraceSpans(app.Receiver);
        Assert.Equal(2, spans.Count);
        Assert.Contains(spans, span => span.Name == "root-keep");
        Assert.Contains(spans, span => span.Name == "child");
    }

    [Fact]
    public async Task Grpc_TailFilter_RootArrivesLast()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampling
            {
                ShouldSample = static (context, _) => ValueTask.FromResult(context.RootSpan?.Name == "root-keep"),
            });
        }));

        var client = new TraceService.TraceServiceClient(app.GrpcChannel);
        _ = await client.ExportAsync(CreateTraceRequest("00000000000000000000000000000031", ("0000000000000032", "0000000000000031", "child")), cancellationToken: XunitCancellationToken).ResponseAsync;
        Assert.Empty(app.Receiver.Traces);

        _ = await client.ExportAsync(CreateTraceRequest("00000000000000000000000000000031", ("0000000000000031", null, "root-keep")), cancellationToken: XunitCancellationToken).ResponseAsync;

        var spans = GetTraceSpans(app.Receiver);
        Assert.Equal(2, spans.Count);
        Assert.Contains(spans, span => span.Name == "root-keep");
        Assert.Contains(spans, span => span.Name == "child");
    }

    [Fact]
    public async Task Http_TailFilter_TimeoutCompletesTrace()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampling
            {
                MaxTraceDuration = TimeSpan.FromMilliseconds(20),
                ShouldSample = static (context, _) => ValueTask.FromResult(context.TimedOut),
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000041", ("0000000000000042", "0000000000000041", "child-timeout")));
        await Task.Delay(50, XunitCancellationToken);
        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000042", ("0000000000000043", null, "other-root")));

        var spans = GetTraceSpans(app.Receiver);
        var timedOutSpans = spans.Where(span => Convert.ToHexString(span.TraceId.ToByteArray()) == "00000000000000000000000000000041").ToList();
        Assert.Single(timedOutSpans);
        Assert.Equal("child-timeout", timedOutSpans[0].Name);
    }

    [Fact]
    public async Task Http_TailFilter_DropWholeTrace_WhenPerTraceLimitIsExceeded()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampling
            {
                MaxBufferedSpansPerTrace = 2,
                MaxBufferedSpans = 10,
                OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace,
                ShouldSample = static (_, _) => ValueTask.FromResult(true),
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "00000000000000000000000000000051",
            ("0000000000000051", null, "root"),
            ("0000000000000052", "0000000000000051", "child-1"),
            ("0000000000000053", "0000000000000051", "child-2")));

        Assert.Empty(app.Receiver.Traces);
    }

    [Fact]
    public async Task Http_TailFilter_DropNewestSpans_WhenPerTraceLimitIsExceeded()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampling
            {
                MaxBufferedSpansPerTrace = 2,
                MaxBufferedSpans = 10,
                OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropNewestSpans,
                ShouldSample = static (_, _) => ValueTask.FromResult(true),
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "00000000000000000000000000000061",
            ("0000000000000061", null, "root"),
            ("0000000000000062", "0000000000000061", "child-1"),
            ("0000000000000063", "0000000000000061", "child-2")));

        var spans = GetTraceSpans(app.Receiver);
        Assert.Equal(2, spans.Count);
        Assert.Contains(spans, span => span.Name == "root");
        Assert.Contains(spans, span => span.Name == "child-1");
        Assert.DoesNotContain(spans, span => span.Name == "child-2");
    }

    [Fact]
    public async Task Http_TailFilter_DropOldestSpans_WhenPerTraceLimitIsExceeded()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampling
            {
                MaxTraceDuration = TimeSpan.FromMilliseconds(20),
                MaxBufferedSpansPerTrace = 2,
                MaxBufferedSpans = 10,
                OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropOldestSpans,
                ShouldSample = static (context, _) => ValueTask.FromResult(context.TimedOut),
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "00000000000000000000000000000071",
            ("0000000000000071", null, "root"),
            ("0000000000000072", "0000000000000071", "child-1"),
            ("0000000000000073", "0000000000000071", "child-2")));

        await Task.Delay(50, XunitCancellationToken);
        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000072", ("0000000000000074", null, "other-root")));

        var spans = GetTraceSpans(app.Receiver);
        var names = spans.Select(span => span.Name).ToList();
        Assert.Equal(2, names.Count);
        Assert.DoesNotContain("root", names);
        Assert.Contains("child-1", names);
        Assert.Contains("child-2", names);
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

    private static async Task SendTracesAsync(HttpClient httpClient, ExportTraceServiceRequest payload, string endpoint = "/v1/traces")
    {
        using var content = new ByteArrayContent(payload.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");

        using var response = await httpClient.PostAsync(endpoint, content, XunitCancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static ExportTraceServiceRequest CreateTraceRequest(string traceId, params (string SpanId, string? ParentSpanId, string Name)[] spans)
    {
        var payload = new ExportTraceServiceRequest();
        var resourceSpans = new global::OpenTelemetry.Proto.Trace.V1.ResourceSpans();
        var scopeSpans = new global::OpenTelemetry.Proto.Trace.V1.ScopeSpans();
        resourceSpans.ScopeSpans.Add(scopeSpans);
        payload.ResourceSpans.Add(resourceSpans);

        foreach (var span in spans)
        {
            scopeSpans.Spans.Add(new global::OpenTelemetry.Proto.Trace.V1.Span
            {
                TraceId = ByteString.CopyFrom(Convert.FromHexString(traceId)),
                SpanId = ByteString.CopyFrom(Convert.FromHexString(span.SpanId)),
                ParentSpanId = span.ParentSpanId is null ? ByteString.Empty : ByteString.CopyFrom(Convert.FromHexString(span.ParentSpanId)),
                Name = span.Name,
            });
        }

        return payload;
    }

    private static List<global::OpenTelemetry.Proto.Trace.V1.Span> GetTraceSpans(InMemoryOpenTelemetryHandler receiver)
    {
        return receiver.Traces
            .Cast<OpenTelemetryTracesItem>()
            .SelectMany(static item => item.Request.ResourceSpans)
            .SelectMany(static resourceSpans => resourceSpans.ScopeSpans)
            .SelectMany(static scopeSpans => scopeSpans.Spans)
            .ToList();
    }

    private sealed class DenyLogsSampler : OpenTelemetrySampler
    {
        public override ValueTask<bool> ShouldSampleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }
    }

    private sealed class DenyTracesSampler : OpenTelemetrySampler
    {
        public override ValueTask<bool> ShouldSampleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(false);
        }
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
