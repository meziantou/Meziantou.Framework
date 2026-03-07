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
