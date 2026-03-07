## Meziantou.Framework.OpenTelemetryCollector

This package provides OpenTelemetry OTLP receiver endpoints for:

- HTTP (`/v1/logs`, `/v1/traces`, `/v1/metrics`)
- gRPC (`LogsService`, `TraceService`, `MetricsService`)

The receiver API is abstract, so you can implement custom handling logic and register one or multiple receivers.

For an in-memory implementation, use the `Meziantou.Framework.OpenTelemetryCollector.InMemory` package.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenTelemetryReceiver<MyReceiver>();

var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();

app.Run();
```
