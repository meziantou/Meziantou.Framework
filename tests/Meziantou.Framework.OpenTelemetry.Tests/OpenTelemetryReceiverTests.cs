using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using Grpc.Net.Client;
using Meziantou.Framework.OpenTelemetryCollector.InMemory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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

        using var content = new StringContent("hello");
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
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
            options.Samplers.Add(new OpenTelemetryTailSampler
            {
                ShouldSample = static (context, _) => ValueTask.FromResult(context.RootSpan?.Name == "root-keep"),
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000021", ("0000000000000022", "0000000000000021", "child")));
        Assert.Empty(app.Receiver.Traces);

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000021", ("0000000000000021", null, "root-keep")));

        var spans = GetTraceSpans(app.Receiver);
        Assert.HasCount(2, spans);
        Assert.Contains(spans, span => span.Name == "root-keep");
        Assert.Contains(spans, span => span.Name == "child");
    }

    [Fact]
    public async Task Grpc_TailFilter_RootArrivesLast()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampler
            {
                ShouldSample = static (context, _) => ValueTask.FromResult(context.RootSpan?.Name == "root-keep"),
            });
        }));

        var client = new TraceService.TraceServiceClient(app.GrpcChannel);
        _ = await client.ExportAsync(CreateTraceRequest("00000000000000000000000000000031", ("0000000000000032", "0000000000000031", "child")), cancellationToken: XunitCancellationToken).ResponseAsync;
        Assert.Empty(app.Receiver.Traces);

        _ = await client.ExportAsync(CreateTraceRequest("00000000000000000000000000000031", ("0000000000000031", null, "root-keep")), cancellationToken: XunitCancellationToken).ResponseAsync;

        var spans = GetTraceSpans(app.Receiver);
        Assert.HasCount(2, spans);
        Assert.Contains(spans, span => span.Name == "root-keep");
        Assert.Contains(spans, span => span.Name == "child");
    }

    [Fact]
    public async Task Http_TailFilter_TimeoutCompletesTrace()
    {
        var timeProvider = new FakeTimeProvider();
        await using var app = await TestApplication.CreateAsync(configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(timeProvider);
            services.Configure<OpenTelemetryReceiverOptions>(static options => options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(1),
                SweepInterval = TimeSpan.FromHours(1),
                ShouldSample = static (context, _) => ValueTask.FromResult(context.TimedOut),
            }));
        });

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000041", ("0000000000000042", "0000000000000041", "child-timeout")));
        timeProvider.Advance(TimeSpan.FromMinutes(2));
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
            options.Samplers.Add(new OpenTelemetryTailSampler
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
            options.Samplers.Add(new OpenTelemetryTailSampler
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
        Assert.HasCount(2, spans);
        Assert.Contains(spans, span => span.Name == "root");
        Assert.Contains(spans, span => span.Name == "child-1");
        Assert.DoesNotContain(spans, span => span.Name == "child-2");
    }

    [Fact]
    public async Task Http_TailFilter_DropOldestSpans_WhenPerTraceLimitIsExceeded()
    {
        var timeProvider = new FakeTimeProvider();
        await using var app = await TestApplication.CreateAsync(configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(timeProvider);
            services.Configure<OpenTelemetryReceiverOptions>(static options => options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(1),
                SweepInterval = TimeSpan.FromHours(1),
                MaxBufferedSpansPerTrace = 2,
                MaxBufferedSpans = 10,
                OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropOldestSpans,
                ShouldSample = static (context, _) => ValueTask.FromResult(context.TimedOut),
            }));
        });

        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "00000000000000000000000000000071",
            ("0000000000000071", null, "root"),
            ("0000000000000072", "0000000000000071", "child-1"),
            ("0000000000000073", "0000000000000071", "child-2")));

        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000072", ("0000000000000074", null, "other-root")));

        var spans = GetTraceSpans(app.Receiver);
        var names = spans.Select(span => span.Name).ToList();
        Assert.HasCount(2, names);
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
        Assert.HasCount(2, items);

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

        Assert.HasCount(3, app.Receiver.Logs);
    }

    [Fact]
    public async Task Http_Response_IsProtobufEncoded()
    {
        await using var app = await TestApplication.CreateAsync();

        using var response = await PostAsync(app.HttpClient, "/v1/logs", new ExportLogsServiceRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-protobuf", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsByteArrayAsync(XunitCancellationToken);
        var parsed = ExportLogsServiceResponse.Parser.ParseFrom(body);
        Assert.Null(parsed.PartialSuccess);
    }

    [Fact]
    public async Task AddOpenTelemetryReceiver_RegistersTheReceiverInDependencyInjection()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddOpenTelemetryReceiver<TestReceiver>();

        await using var app = builder.Build();
        app.MapOpenTelemetryReceiverEndpoints();
        await app.StartAsync(XunitCancellationToken);

        using var httpClient = app.GetTestClient();
        await SendLogsAsync(httpClient, "from-http");

        Assert.Equal(1, app.Services.GetRequiredService<TestReceiver>().ReceivedLogsCount);
    }

    [Fact]
    public async Task Http_JsonPayload_IsSupported()
    {
        await using var app = await TestApplication.CreateAsync();

        const string Payload = """{"resourceLogs":[{"scopeLogs":[{"logRecords":[{"body":{"stringValue":"from-json"}}]}]}]}""";
        using var response = await PostJsonAsync(app.HttpClient, "/v1/logs", Payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var item = Assert.IsType<OpenTelemetryLogsItem>(Assert.Single(app.Receiver.Logs));
        Assert.Equal("from-json", item.Request.ResourceLogs[0].ScopeLogs[0].LogRecords[0].Body.StringValue);
    }

    [Fact]
    public async Task Http_JsonPayload_DecodesHexEncodedIdentifiers()
    {
        await using var app = await TestApplication.CreateAsync();

        // OTLP/JSON hex-encodes trace and span ids instead of using the base64 encoding of the Protobuf JSON mapping.
        const string Payload = """
            {"resourceSpans":[{"scopeSpans":[{"spans":[{"traceId":"000102030405060708090a0b0c0d0e0f","spanId":"1011121314151617","parentSpanId":"18191a1b1c1d1e1f","name":"json-span"}]}]}]}
            """;
        using var response = await PostJsonAsync(app.HttpClient, "/v1/traces", Payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var span = Assert.Single(GetTraceSpans(app.Receiver));
        Assert.Equal("json-span", span.Name);
        Assert.Equal("000102030405060708090A0B0C0D0E0F", Convert.ToHexString(span.TraceId.ToByteArray()));
        Assert.Equal("1011121314151617", Convert.ToHexString(span.SpanId.ToByteArray()));
        Assert.Equal("18191A1B1C1D1E1F", Convert.ToHexString(span.ParentSpanId.ToByteArray()));
    }

    [Fact]
    public async Task Http_CompressedPayload_IsSupportedThroughTheRequestDecompressionMiddleware()
    {
        await using var app = await TestApplication.CreateAsync(
            configureServices: static services => services.AddRequestDecompression(),
            configureApp: static app => app.UseRequestDecompression());

        var payload = new ExportLogsServiceRequest();
        payload.ResourceLogs.Add(new global::OpenTelemetry.Proto.Logs.V1.ResourceLogs());

        using var buffer = new MemoryStream();
        await using (var gzipStream = new GZipStream(buffer, CompressionMode.Compress, leaveOpen: true))
        {
            await gzipStream.WriteAsync(payload.ToByteArray(), XunitCancellationToken);
        }

        using var content = new ByteArrayContent(buffer.ToArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        content.Headers.ContentEncoding.Add("gzip");

        using var response = await app.HttpClient.PostAsync("/v1/logs", content, XunitCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.IsType<OpenTelemetryLogsItem>(Assert.Single(app.Receiver.Logs));
        _ = Assert.Single(item.Request.ResourceLogs);
    }

    [Fact]
    public async Task Http_InvalidCompressedPayload_Returns400()
    {
        await using var app = await TestApplication.CreateAsync(
            configureServices: static services => services.AddRequestDecompression(),
            configureApp: static app => app.UseRequestDecompression());

        using var content = new ByteArrayContent([0x00, 0xFF, 0xA5]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        content.Headers.ContentEncoding.Add("gzip");

        using var response = await app.HttpClient.PostAsync("/v1/logs", content, XunitCancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(app.Receiver.Logs);
    }

    [Fact]
    public async Task Grpc_PartialSuccess_IsReportedToTheClient()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: static services => services.AddOpenTelemetryReceiver(static _ => new RejectingHandler()));

        var client = new LogsService.LogsServiceClient(app.GrpcChannel);
        var response = await client.ExportAsync(new ExportLogsServiceRequest(), cancellationToken: XunitCancellationToken).ResponseAsync;

        Assert.Equal(2, response.PartialSuccess.RejectedLogRecords);
        Assert.Equal("rejected", response.PartialSuccess.ErrorMessage);
    }

    [Fact]
    public async Task Http_PartialSuccess_IsReportedToTheClient()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: static services => services.AddOpenTelemetryReceiver(static _ => new RejectingHandler()));

        using var response = await PostAsync(app.HttpClient, "/v1/logs", new ExportLogsServiceRequest());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(XunitCancellationToken);
        var parsed = ExportLogsServiceResponse.Parser.ParseFrom(body);
        Assert.Equal(2, parsed.PartialSuccess.RejectedLogRecords);
        Assert.Equal("rejected", parsed.PartialSuccess.ErrorMessage);
    }

    [Fact]
    public async Task Http_TailFilter_FlushesTimedOutTracesWithoutFurtherTraffic()
    {
        var timeProvider = new FakeTimeProvider();
        await using var app = await TestApplication.CreateAsync(configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(timeProvider);
            services.Configure<OpenTelemetryReceiverOptions>(static options => options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(1),
                ShouldSample = static (context, _) => ValueTask.FromResult(context.TimedOut),
            }));
        });

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000061", ("0000000000000062", "0000000000000061", "orphan-child")));
        Assert.Empty(GetTraceSpans(app.Receiver));

        // No other trace is received: only the background sweep can release the buffered trace. The
        // sweep waits on a PeriodicTimer, and a clock advance that happens before the timer is
        // created schedules no tick, so keep advancing until the sweep runs.
        await WaitForAsync(() =>
        {
            if (GetTraceSpans(app.Receiver).Count > 0)
                return true;

            timeProvider.Advance(TimeSpan.FromMinutes(2));
            return false;
        });

        var span = Assert.Single(GetTraceSpans(app.Receiver));
        Assert.Equal("orphan-child", span.Name);
    }

    [Fact]
    public async Task Http_TailFilter_DispatchesBufferedTracesWithoutTheRequestCancellationToken()
    {
        var recordingHandler = new CancellationRecordingHandler();
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.AddOpenTelemetryReceiver(_ => recordingHandler, static options => options.Samplers.Add(new OpenTelemetryTailSampler
        {
            MaxTraceDuration = TimeSpan.FromMinutes(1),
        })));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "00000000000000000000000000000071",
            ("0000000000000072", "0000000000000071", "child"),
            ("0000000000000071", null, "root")));

        // The spans left the buffer, so aborting the request must not discard them.
        Assert.Equal(1, recordingHandler.TracesCallCount);
        Assert.False(recordingHandler.LastTracesTokenCanBeCanceled);
    }

    [Fact]
    public async Task Http_TailFilter_DropWholeTrace_KeepsDroppingLaterBatchesOfTheSameTrace()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: static services => services.Configure<OpenTelemetryReceiverOptions>(static options => options.Samplers.Add(new OpenTelemetryTailSampler
        {
            MaxTraceDuration = TimeSpan.FromMinutes(1),
            MaxBufferedSpansPerTrace = 2,
            OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace,
        })));

        const string TraceId = "00000000000000000000000000000081";
        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            TraceId,
            ("0000000000000082", "0000000000000081", "child-1"),
            ("0000000000000083", "0000000000000081", "child-2"),
            ("0000000000000084", "0000000000000081", "child-3")));

        // The trace exceeded its own limit, so the later batch carrying the root span must be dropped too.
        await SendTracesAsync(app.HttpClient, CreateTraceRequest(TraceId, ("0000000000000081", null, "root")));

        Assert.Empty(GetTraceSpans(app.Receiver));
    }

    [Fact]
    public async Task Http_TailFilter_ReportsDroppedSpansAsPartialSuccess()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: static services => services.Configure<OpenTelemetryReceiverOptions>(static options => options.Samplers.Add(new OpenTelemetryTailSampler
        {
            MaxTraceDuration = TimeSpan.FromMinutes(1),
            MaxBufferedSpansPerTrace = 2,
            OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace,
        })));

        using var response = await PostAsync(app.HttpClient, "/v1/traces", CreateTraceRequest(
            "00000000000000000000000000000091",
            ("0000000000000092", "0000000000000091", "child-1"),
            ("0000000000000093", "0000000000000091", "child-2"),
            ("0000000000000094", "0000000000000091", "child-3")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(XunitCancellationToken);
        var parsed = ExportTraceServiceResponse.Parser.ParseFrom(body);
        Assert.Equal(3, parsed.PartialSuccess.RejectedSpans);
    }

    [Fact]
    public async Task Http_TailFilter_GlobalLimit_DoesNotTruncateATraceWithinItsOwnLimit()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(10),
                MaxBufferedSpansPerTrace = 100,
                MaxBufferedSpans = 3,
                OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace,
                ShouldSample = static (_, _) => ValueTask.FromResult(true),
            });
        }));

        // A large trace fills the global buffer
        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "000000000000000000000000000000AA",
            ("00000000000000A2", "00000000000000A1", "a-child-1"),
            ("00000000000000A3", "00000000000000A1", "a-child-2"),
            ("00000000000000A4", "00000000000000A1", "a-child-3")));

        // A small, unrelated trace arrives while the buffer is full. It is far below its own limit, so it must be
        // buffered intact: the large trace is evicted instead.
        await SendTracesAsync(app.HttpClient, CreateTraceRequest("000000000000000000000000000000BB", ("00000000000000B2", "00000000000000B1", "b-child-1")));
        await SendTracesAsync(app.HttpClient, CreateTraceRequest("000000000000000000000000000000BB", ("00000000000000B1", null, "b-root")));

        var names = GetTraceSpans(app.Receiver).Select(static span => span.Name).ToList();
        Assert.HasCount(2, names);
        Assert.Contains("b-root", names);
        Assert.Contains("b-child-1", names);
    }

    [Fact]
    public async Task Http_TailFilter_GlobalLimit_DoesNotEmitAnEvictedTraceAsAFragment()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(10),
                MaxBufferedSpansPerTrace = 100,
                MaxBufferedSpans = 3,
                OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace,
                ShouldSample = static (_, _) => ValueTask.FromResult(true),
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "000000000000000000000000000000CC",
            ("00000000000000C2", "00000000000000C1", "c-child-1"),
            ("00000000000000C3", "00000000000000C1", "c-child-2"),
            ("00000000000000C4", "00000000000000C1", "c-child-3")));

        // Evicts the trace above
        await SendTracesAsync(app.HttpClient, CreateTraceRequest("000000000000000000000000000000DD", ("00000000000000D2", "00000000000000D1", "d-child-1")));

        // The root of the evicted trace arrives. Emitting it alone would look like a complete single-span trace.
        await SendTracesAsync(app.HttpClient, CreateTraceRequest("000000000000000000000000000000CC", ("00000000000000C1", null, "c-root")));

        Assert.DoesNotContain(GetTraceSpans(app.Receiver), static span => span.Name.StartsWith("c-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Http_TailFilter_GlobalLimit_ReportsEvictedSpansToTheOwningClientOnly()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(10),
                MaxBufferedSpansPerTrace = 100,
                MaxBufferedSpans = 2,
                OverflowPolicy = OpenTelemetryTailBufferOverflowPolicy.DropWholeTrace,
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "000000000000000000000000000000EE",
            ("00000000000000E2", "00000000000000E1", "e-child-1"),
            ("00000000000000E3", "00000000000000E1", "e-child-2")));

        // This request evicts the trace above. Those spans belong to the previous request, so they must not be
        // reported in this response: partial_success only describes the records this request sent.
        using var response = await PostAsync(app.HttpClient, "/v1/traces", CreateTraceRequest("000000000000000000000000000000FF", ("00000000000000F2", "00000000000000F1", "f-child-1")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync(XunitCancellationToken);
        var parsed = ExportTraceServiceResponse.Parser.ParseFrom(body);
        Assert.Null(parsed.PartialSuccess);
    }

    [Fact]
    public async Task Http_TailFilter_HandlerFailureOnABufferedTrace_DoesNotFailAnUnrelatedRequest()
    {
        var timeProvider = new FakeTimeProvider();
        await using var app = await TestApplication.CreateAsync(configureServices: services =>
        {
            services.AddSingleton<TimeProvider>(timeProvider);
            services.AddOpenTelemetryReceiver(static _ => new ThrowingTracesHandler("failing-"));
            services.Configure<OpenTelemetryReceiverOptions>(static options => options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(1),

                // Disable the background sweep so the timed-out trace is flushed by the next incoming request
                SweepInterval = TimeSpan.FromHours(1),
                ShouldSample = static (_, _) => ValueTask.FromResult(true),
            }));
        });

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000101", ("0000000000000102", "0000000000000101", "failing-child")));
        timeProvider.Advance(TimeSpan.FromMinutes(2));

        // This request flushes the trace above, whose handler throws. The failure belongs to another client.
        using var response = await PostAsync(app.HttpClient, "/v1/traces", CreateTraceRequest("00000000000000000000000000000102", ("0000000000000103", null, "healthy-root")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(GetTraceSpans(app.Receiver), static span => span.Name == "healthy-root");
    }

    [Fact]
    public async Task Http_TailFilter_GroupsDispatchedSpansByResourceAndScope()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(10),
                ShouldSample = static (_, _) => ValueTask.FromResult(true),
            });
        }));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest(
            "00000000000000000000000000000111",
            ("0000000000000111", null, "root"),
            ("0000000000000112", "0000000000000111", "child-1"),
            ("0000000000000113", "0000000000000111", "child-2")));

        // The client sent one resource and one scope, so the dispatched request must not repeat them per span.
        var item = Assert.Single(app.Receiver.Traces.Cast<OpenTelemetryTracesItem>());
        var resourceSpans = Assert.Single(item.Request.ResourceSpans);
        var scopeSpans = Assert.Single(resourceSpans.ScopeSpans);
        Assert.HasCount(3, scopeSpans.Spans);
    }

    [Fact]
    public async Task AddOpenTelemetryReceiver_RegisteringTheSameReceiverTwice_DispatchesOnce()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: static services => services.AddInMemoryOpenTelemetryReceiver());

        await SendLogsAsync(app.HttpClient, "hello");

        Assert.Single(app.Receiver.Logs);
    }

    [Fact]
    public async Task AddOpenTelemetryReceiver_RegisteringTheSameReceiverTypeTwice_DispatchesOnce()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: static services =>
        {
            services.AddOpenTelemetryReceiver<TestReceiver>();
            services.AddOpenTelemetryReceiver<TestReceiver>();
        });

        await SendLogsAsync(app.HttpClient, "hello");

        Assert.Equal(1, app.App.Services.GetRequiredService<TestReceiver>().ReceivedLogsCount);
    }

    [Fact]
    public async Task Http_JsonPayload_ResponseIsJsonEncoded()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: static services => services.AddOpenTelemetryReceiver(static _ => new RejectingHandler()));

        const string Payload = """{"resourceLogs":[{"scopeLogs":[{"logRecords":[{"body":{"stringValue":"hello"}}]}]}]}""";
        using var response = await PostJsonAsync(app.HttpClient, "/v1/logs", Payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(XunitCancellationToken);
        using var document = JsonDocument.Parse(body);
        var partialSuccess = document.RootElement.GetProperty("partialSuccess");
        Assert.Equal("2", partialSuccess.GetProperty("rejectedLogRecords").GetString());
        Assert.Equal("rejected", partialSuccess.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task MapOpenTelemetryReceiverEndpoints_ReturnsABuilderThatAppliesConventions()
    {
        await using var app = await TestApplication.CreateAsync(
            configureApp: static app => app.MapOpenTelemetryReceiverEndpoints().WithMetadata(new EndpointNameMetadata("otlp")),
            mapEndpoints: false);

        var endpoints = app.App.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        var httpEndpoints = endpoints.Where(static endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "otlp").ToList();

        // The three HTTP endpoints and the three gRPC services
        Assert.NotEmpty(httpEndpoints);
        Assert.Contains(httpEndpoints, static endpoint => (endpoint as RouteEndpoint)?.RoutePattern.RawText == "/v1/logs");
        Assert.Contains(httpEndpoints, static endpoint => (endpoint as RouteEndpoint)?.RoutePattern.RawText == "/v1/traces");
        Assert.Contains(httpEndpoints, static endpoint => (endpoint as RouteEndpoint)?.RoutePattern.RawText == "/v1/metrics");
    }

    [Fact]
    public async Task Http_TailFilter_HandlesConcurrentRequestsForTheSameTrace()
    {
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.Configure<OpenTelemetryReceiverOptions>(static options =>
        {
            options.Samplers.Add(new OpenTelemetryTailSampler
            {
                MaxTraceDuration = TimeSpan.FromMinutes(10),
                MaxBufferedSpansPerTrace = 1000,
                MaxBufferedSpans = 10_000,
                ShouldSample = static (_, _) => ValueTask.FromResult(true),
            });
        }));

        const int RequestCount = 50;
        var tasks = new List<Task>(RequestCount);
        for (var i = 0; i < RequestCount; i++)
        {
            var spanId = (0x200 + i).ToString("x16", CultureInfo.InvariantCulture);
            tasks.Add(SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000121", (spanId, "0000000000000121", "child-" + i.ToString(CultureInfo.InvariantCulture)))));
        }

        await Task.WhenAll(tasks);
        Assert.Empty(GetTraceSpans(app.Receiver));

        await SendTracesAsync(app.HttpClient, CreateTraceRequest("00000000000000000000000000000121", ("0000000000000121", null, "root")));

        // Every span must be released exactly once, whichever order the concurrent requests were processed in
        var spans = GetTraceSpans(app.Receiver);
        Assert.HasCount(RequestCount + 1, spans);
        Assert.HasCount(RequestCount + 1, spans.Select(static span => span.Name).Distinct(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public async Task InMemoryReceiver_UsesTheInjectedTimeProvider()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero));
        await using var app = await TestApplication.CreateAsync(configureServices: services => services.AddSingleton<TimeProvider>(timeProvider));

        await SendLogsAsync(app.HttpClient, "hello");

        var item = Assert.Single(app.Receiver.Logs);
        Assert.Equal(timeProvider.GetUtcNow(), item.ReceivedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OpenTelemetryTailSampler_RejectsInvalidSpanLimits(int value)
    {
        var sampler = new OpenTelemetryTailSampler();

        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.MaxBufferedSpans = value);
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.MaxBufferedSpansPerTrace = value);
    }

    [Fact]
    public void OpenTelemetryTailSampler_RejectsInvalidDurations()
    {
        var sampler = new OpenTelemetryTailSampler();

        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.MaxTraceDuration = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.MaxTraceDuration = TimeSpan.FromSeconds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => sampler.SweepInterval = TimeSpan.Zero);
    }

    [Fact]
    public void OpenTelemetryHandlerContext_CanBeCreatedByHandlerTests()
    {
        var partialSuccess = new OpenTelemetryPartialSuccess();
        var context = new OpenTelemetryHandlerContext(OpenTelemetryTransport.Grpc, "POST /v1/logs", partialSuccess);

        context.PartialSuccess.Reject(3, "nope");

        Assert.Equal("POST /v1/logs", context.Method);
        Assert.Equal(OpenTelemetryTransport.Grpc, context.Transport);
        Assert.Equal(3, partialSuccess.RejectedCount);

        // A default instance must not hand out a null Method
        Assert.Equal("", default(OpenTelemetryHandlerContext).Method);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200; i++)
        {
            if (condition())
                return;

            await Task.Delay(25, XunitCancellationToken);
        }

        Assert.Fail("The condition was not met before the timeout");
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient httpClient, string endpoint, IMessage payload)
    {
        using var content = new ByteArrayContent(payload.ToByteArray());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
        return await httpClient.PostAsync(endpoint, content, XunitCancellationToken);
    }

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient httpClient, string endpoint, string payload)
    {
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return await httpClient.PostAsync(endpoint, content, XunitCancellationToken);
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

    private sealed class RejectingHandler : OpenTelemetryHandler
    {
        public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken)
        {
            context.PartialSuccess.Reject(2, "rejected");
            return ValueTask.CompletedTask;
        }

        public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        {
            context.PartialSuccess.Reject(2, "rejected");
            return ValueTask.CompletedTask;
        }

        public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken)
        {
            context.PartialSuccess.Reject(2, "rejected");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellationRecordingHandler : OpenTelemetryHandler
    {
        public int TracesCallCount { get; private set; }

        public bool? LastTracesTokenCanBeCanceled { get; private set; }

        public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        {
            TracesCallCount++;
            LastTracesTokenCanBeCanceled = cancellationToken.CanBeCanceled;
            return ValueTask.CompletedTask;
        }

        public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class ThrowingTracesHandler(string failingSpanNamePrefix) : OpenTelemetryHandler
    {
        private readonly string _failingSpanNamePrefix = failingSpanNamePrefix;

        public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken)
        {
            var throws = request.ResourceSpans
                .SelectMany(static resourceSpans => resourceSpans.ScopeSpans)
                .SelectMany(static scopeSpans => scopeSpans.Spans)
                .Any(span => span.Name.StartsWith(_failingSpanNamePrefix, StringComparison.Ordinal));

            return throws ? throw new InvalidOperationException("The handler cannot store this trace") : ValueTask.CompletedTask;
        }

        public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class TestApplication(WebApplication app, HttpClient httpClient, GrpcChannel grpcChannel, InMemoryOpenTelemetryHandler receiver) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;

        public HttpClient HttpClient { get; } = httpClient;

        public GrpcChannel GrpcChannel { get; } = grpcChannel;

        public InMemoryOpenTelemetryHandler Receiver { get; } = receiver;

        public static async Task<TestApplication> CreateAsync(InMemoryOpenTelemetryHandlerOptions? options = null, Action<IServiceCollection>? configureServices = null, Action<WebApplication>? configureApp = null, bool mapEndpoints = true)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();

            builder.Services.AddInMemoryOpenTelemetryReceiver(options);

            configureServices?.Invoke(builder.Services);

            var app = builder.Build();
            configureApp?.Invoke(app);
            if (mapEndpoints)
            {
                app.MapOpenTelemetryReceiverEndpoints();
            }

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
