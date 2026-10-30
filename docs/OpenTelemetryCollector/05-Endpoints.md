---
title: Endpoints
description: Choose the paths of the OTLP/HTTP endpoints, disable gRPC, send Protobuf or JSON, accept compressed requests, and secure the endpoints.
---

[MapOpenTelemetryReceiverEndpoints](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryEndpointRouteBuilderExtensions.MapOpenTelemetryReceiverEndpoints) maps the endpoints configured in [OpenTelemetryReceiverOptions](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryReceiverOptions):

| Option | Default | Endpoint |
| --- | --- | --- |
| [HttpLogsEndpoint](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryReceiverOptions.HttpLogsEndpoint) | `/v1/logs` | `POST` endpoint for logs |
| [HttpTracesEndpoint](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryReceiverOptions.HttpTracesEndpoint) | `/v1/traces` | `POST` endpoint for traces |
| [HttpMetricsEndpoint](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryReceiverOptions.HttpMetricsEndpoint) | `/v1/metrics` | `POST` endpoint for metrics |
| [EnableGrpcEndpoints](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryReceiverOptions.EnableGrpcEndpoints) | `true` | The gRPC services [LogsService](cref:OpenTelemetry.Proto.Collector.Logs.V1.LogsService), [TraceService](cref:OpenTelemetry.Proto.Collector.Trace.V1.TraceService) and [MetricsService](cref:OpenTelemetry.Proto.Collector.Metrics.V1.MetricsService) |

## Choose the endpoints

Set the options in the `configure` argument of [AddOpenTelemetryReceiver](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryServiceCollectionExtensions.AddOpenTelemetryReceiver) or [AddInMemoryOpenTelemetryReceiver](cref:Meziantou.Framework.OpenTelemetryCollector.InMemory.OpenTelemetryCollectorInMemoryServiceCollectionExtensions.AddInMemoryOpenTelemetryReceiver). A
`null` path does not map the endpoint:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_ConfigureEndpoints;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseTestServer();
builder.Services.AddInMemoryOpenTelemetryReceiver(options =>
{
    options.HttpLogsEndpoint = "/otlp/v1/logs";
    options.HttpTracesEndpoint = "/otlp/v1/traces";
    options.HttpMetricsEndpoint = null;   // null: the endpoint is not mapped
    options.EnableGrpcEndpoints = false;  // no gRPC services
});

await using var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();
await app.StartAsync();

using var httpClient = app.GetTestClient();
using var logsContent = new ByteArrayContent(new ExportLogsServiceRequest().ToByteArray());
using var logsResponse = await httpClient.PostAsync(new Uri("/otlp/v1/logs", UriKind.Relative), logsContent);
Assert.Equal(HttpStatusCode.OK, logsResponse.StatusCode);

using var metricsContent = new ByteArrayContent(new ExportMetricsServiceRequest().ToByteArray());
using var metricsResponse = await httpClient.PostAsync(new Uri("/v1/metrics", UriKind.Relative), metricsContent);
Assert.Equal(HttpStatusCode.NotFound, metricsResponse.StatusCode);
```
<!-- endref -->

## Protobuf and JSON

The OTLP/HTTP endpoints accept both encodings of OTLP, chosen by the `Content-Type` of the request:

| Content type | Encoding |
| --- | --- |
| `application/x-protobuf`, `application/octet-stream`, or none | Protobuf |
| `application/json` | OTLP/JSON |
| Anything else | Rejected with `415 Unsupported Media Type` |

The response uses the encoding of the request. A body that cannot be parsed is rejected with `400 Bad Request`. In
OTLP/JSON, trace and span ids are hexadecimal strings, and unknown fields are ignored:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_OtlpJson;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseTestServer();
builder.Services.AddInMemoryOpenTelemetryReceiver();

await using var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();
await app.StartAsync();

// OTLP/JSON: trace and span ids are hexadecimal strings
using var httpClient = app.GetTestClient();
using var content = new StringContent("""
    {"resourceSpans":[{"scopeSpans":[{"spans":[
      {"traceId":"5b8efff798038103d269b633813fc60c","spanId":"eee19b7ec3c1b174","name":"GET /orders"}
    ]}]}]}
    """, Encoding.UTF8, "application/json");
using var response = await httpClient.PostAsync(new Uri("/v1/traces", UriKind.Relative), content);

// The response uses the encoding of the request
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

var receiver = app.Services.GetRequiredService<InMemoryOpenTelemetryHandler>();
var span = receiver.Traces.OfType<OpenTelemetryTracesItem>()
    .SelectMany(item => item.Request.ResourceSpans)
    .SelectMany(resourceSpans => resourceSpans.ScopeSpans)
    .SelectMany(scopeSpans => scopeSpans.Spans)
    .Single();
Assert.Equal("GET /orders", span.Name);
Assert.Equal("5b8efff798038103d269b633813fc60c", Convert.ToHexStringLower(span.TraceId.Span));
```
<!-- endref -->

## gRPC

The gRPC services implement the `Export` methods of OTLP/gRPC. [AddOpenTelemetryReceiver](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryServiceCollectionExtensions.AddOpenTelemetryReceiver) registers the gRPC services of
ASP.NET Core. The client classes generated from the OTLP definitions, such as [TraceService.TraceServiceClient](cref:OpenTelemetry.Proto.Collector.Trace.V1.TraceService.TraceServiceClient), are
public, so a test can call the services:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_GrpcExport;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseTestServer();
builder.Services.AddInMemoryOpenTelemetryReceiver();

await using var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();
await app.StartAsync();

// The gRPC client classes are generated from the OTLP protos, in the same namespaces as the messages
using var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() });
var client = new TraceService.TraceServiceClient(channel);
_ = await client.ExportAsync(new ExportTraceServiceRequest { ResourceSpans = { new ResourceSpans() } });

var receiver = app.Services.GetRequiredService<InMemoryOpenTelemetryHandler>();
var item = Assert.Single(receiver.Traces);
Assert.Equal("/opentelemetry.proto.collector.trace.v1.TraceService/Export", item.Method);
```
<!-- endref -->

gRPC needs HTTP/2: configure the Kestrel endpoint that receives the gRPC requests to accept it.

## Compressed requests

Compressed requests are decompressed by the request decompression middleware of ASP.NET Core, so every encoding it
supports works (`gzip`, `br`, `deflate`, or a provider of your own):

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollector_RequestDecompression;format=md-fenced-code-block;dedent=true -->
```csharp
builder.Services.AddRequestDecompression();

var app = builder.Build();
app.UseRequestDecompression();
app.MapOpenTelemetryReceiverEndpoints();
```
<!-- endref -->

A body that cannot be decompressed is rejected with `400 Bad Request`. The size limits of the request are the ones of
the server, such as the maximum request body size of Kestrel.

## Secure the endpoints

[MapOpenTelemetryReceiverEndpoints](cref:Meziantou.Framework.OpenTelemetryCollector.OpenTelemetryEndpointRouteBuilderExtensions.MapOpenTelemetryReceiverEndpoints) returns an `IEndpointConventionBuilder` that applies the conventions to every mapped
endpoint, HTTP and gRPC alike. An OTLP receiver accepts writes from anyone who can reach it: restrict it, unless it is
only reachable from a trusted network.

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/OpenTelemetryCollectorExamples.cs;region=OpenTelemetryCollector_SecureEndpoints;format=md-fenced-code-block;dedent=true -->
```csharp
app.MapOpenTelemetryReceiverEndpoints()
   .RequireAuthorization()
   .RequireRateLimiting("otlp");
```
<!-- endref -->

`RequireAuthorization` and `RequireRateLimiting` need the authorization and rate limiting services and middleware of
ASP.NET Core, configured as for any other endpoint.
