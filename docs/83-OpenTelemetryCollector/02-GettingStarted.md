---
title: Getting started
description: Install the packages, add the OTLP endpoints to an ASP.NET Core application, and receive telemetry in memory.
---

## Install the packages

Add the packages to an ASP.NET Core project, or a test project, that targets .NET 10 or later. The in-memory receiver
brings the main package with it:

```bash
dotnet add package Meziantou.Framework.OpenTelemetryCollector.InMemory
```

To write your own handlers only, add `Meziantou.Framework.OpenTelemetryCollector` instead.

## Host the OTLP endpoints

Register a receiver, then map the endpoints:

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/OpenTelemetryCollectorExamples.cs;region=OpenTelemetryCollectorInMemory_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInMemoryOpenTelemetryReceiver();

var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();

app.Run();
```
<!-- endref -->

- `AddInMemoryOpenTelemetryReceiver` registers a handler that keeps every export request in memory. To handle the
  requests in code, register your own handler with `AddOpenTelemetryReceiver<T>`. See
  [Custom handlers](04-CustomHandlers.md).
- `MapOpenTelemetryReceiverEndpoints` maps the OTLP/HTTP endpoints, `POST /v1/logs`, `POST /v1/traces` and
  `POST /v1/metrics`, and the OTLP/gRPC services. See [Endpoints](05-Endpoints.md).

Point the OTLP exporter of an application to this address, and its logs, traces and metrics reach the receiver.

## Receive an export request

The OTLP exporters send export requests: an `ExportLogsServiceRequest`, an `ExportTraceServiceRequest` or an
`ExportMetricsServiceRequest`, encoded with Protobuf or JSON. This test hosts the receiver on the in-memory ASP.NET Core
test server, sends a request as an exporter does, and reads what the receiver stored:

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollectorInMemory_AssertOnReceivedLogs;format=md-fenced-code-block;dedent=true -->
```csharp
// Host the receiver in memory, with the ASP.NET Core test server
var builder = WebApplication.CreateBuilder();
builder.WebHost.UseTestServer();
builder.Services.AddInMemoryOpenTelemetryReceiver();

await using var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();
await app.StartAsync();

// Send an OTLP/HTTP export request, as the OTLP exporter of an application does
var exportRequest = new ExportLogsServiceRequest
{
    ResourceLogs =
    {
        new ResourceLogs
        {
            ScopeLogs =
            {
                new ScopeLogs
                {
                    LogRecords =
                    {
                        new LogRecord
                        {
                            Body = new AnyValue { StringValue = "Order shipped" },
                            Attributes = { new KeyValue { Key = "order.id", Value = new AnyValue { IntValue = 42 } } },
                        },
                    },
                },
            },
        },
    },
};

using var httpClient = app.GetTestClient();
using var content = new ByteArrayContent(exportRequest.ToByteArray());
content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
using var response = await httpClient.PostAsync(new Uri("/v1/logs", UriKind.Relative), content);
Assert.Equal(HttpStatusCode.OK, response.StatusCode);

// Read what the receiver stored: one item per export request
var receiver = app.Services.GetRequiredService<InMemoryOpenTelemetryHandler>();
var item = Assert.Single(receiver.Logs.OfType<OpenTelemetryLogsItem>());
Assert.Equal("POST /v1/logs", item.Method);

var logRecord = item.Request.ResourceLogs.SelectMany(r => r.ScopeLogs).SelectMany(s => s.LogRecords).Single();
Assert.Equal("Order shipped", logRecord.Body.StringValue);
Assert.Equal(42, logRecord.Attributes.Single(attribute => attribute.Key == "order.id").Value.IntValue);
```
<!-- endref -->

`UseTestServer` and `GetTestClient` come from the `Microsoft.AspNetCore.TestHost` package. The OTLP messages are in the
`OpenTelemetry.Proto.*` namespaces, and `ToByteArray` is an extension method of the `Google.Protobuf` namespace.

## Next steps

- [Testing with the in-memory receiver](03-InMemoryReceiver.md): read the stored items and limit their number.
- [Custom handlers](04-CustomHandlers.md): handle the export requests in code.
