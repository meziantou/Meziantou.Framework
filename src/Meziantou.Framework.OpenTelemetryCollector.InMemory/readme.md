## Meziantou.Framework.OpenTelemetryCollector.InMemory

This package provides an in-memory implementation of OpenTelemetry receivers for `Meziantou.Framework.OpenTelemetryCollector`.
`AddInMemoryOpenTelemetryReceiver` is in the `Meziantou.Framework.OpenTelemetryCollector.InMemory` namespace.

<!-- ref:../../samples/Meziantou.AspNetCore.Samples/OpenTelemetryCollectorExamples.cs;region=OpenTelemetryCollectorInMemory_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInMemoryOpenTelemetryReceiver();

var app = builder.Build();
app.MapOpenTelemetryReceiverEndpoints();

app.Run();
```
<!-- endref -->

### Retention

By default nothing is ever evicted: every received export request is kept in full. That suits a test asserting on
everything an application exported, but a long running process must set explicit limits, otherwise memory grows with the
received telemetry. Once a limit is set, the oldest items are overwritten.

<!-- ref:../../tests/Meziantou.Framework.OpenTelemetry.Tests/OpenTelemetryReceiverTests.cs;region=OpenTelemetryCollectorInMemory_Retention;format=md-fenced-code-block;dedent=true -->
```csharp
builder.Services.AddInMemoryOpenTelemetryReceiver(new InMemoryOpenTelemetryHandlerOptions
{
    MaximumLogCount = 1000,
    MaximumTraceCount = 1000,
    MaximumMetricCount = 1000,
});
```
<!-- endref -->

`ReceivedAt` uses the `TimeProvider` registered in the service collection, so a test can control it with
`FakeTimeProvider`.
