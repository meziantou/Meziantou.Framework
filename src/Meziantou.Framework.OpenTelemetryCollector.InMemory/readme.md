## Meziantou.Framework.OpenTelemetryCollector.InMemory

This package provides an in-memory implementation of OpenTelemetry receivers for `Meziantou.Framework.OpenTelemetryCollector.Abstractions`.

```csharp
using Meziantou.Framework.OpenTelemetryCollector.Abstractions;
using Meziantou.Framework.OpenTelemetryCollector.InMemory;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInMemoryOpenTelemetryReceiver();

var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();

app.Run();
```

### Retention

By default nothing is ever evicted: every received export request is kept in full. That suits a test asserting on
everything an application exported, but a long running process must set explicit limits, otherwise memory grows with the
received telemetry. Once a limit is set, the oldest items are overwritten.

```csharp
builder.Services.AddInMemoryOpenTelemetryReceiver(new InMemoryOpenTelemetryHandlerOptions
{
    MaximumLogCount = 1000,
    MaximumTraceCount = 1000,
    MaximumMetricCount = 1000,
});
```

`ReceivedAt` uses the `TimeProvider` registered in the service collection, so a test can control it with
`FakeTimeProvider`.
