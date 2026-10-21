using Meziantou.Framework.OpenTelemetryCollector;
using Meziantou.Framework.OpenTelemetryCollector.InMemory;
using Microsoft.AspNetCore.Builder;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Meziantou.AspNetCore.Samples;

/// <summary>
/// The examples of src/Meziantou.Framework.OpenTelemetryCollector/readme.md and
/// src/Meziantou.Framework.OpenTelemetryCollector.InMemory/readme.md that start a web application.
/// </summary>
public static class OpenTelemetryCollectorExamples
{
    public static void CustomReceiver(string[] args)
    {
        #region OpenTelemetryCollector_CustomReceiver
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddOpenTelemetryReceiver<MyReceiver>();

        var app = builder.Build();
        app.MapOpenTelemetryReceiverEndpoints();

        app.Run();
        #endregion
    }

    public static void SecureEndpoints(WebApplication app)
    {
        #region OpenTelemetryCollector_SecureEndpoints
        app.MapOpenTelemetryReceiverEndpoints()
           .RequireAuthorization()
           .RequireRateLimiting("otlp");
        #endregion
    }

    public static void InMemoryReceiver(string[] args)
    {
        #region OpenTelemetryCollectorInMemory_Usage
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddInMemoryOpenTelemetryReceiver();

        var app = builder.Build();
        app.MapOpenTelemetryReceiverEndpoints();

        app.Run();
        #endregion
    }

    private sealed class MyReceiver : OpenTelemetryHandler
    {
        public override ValueTask HandleLogsAsync(OpenTelemetryHandlerContext context, ExportLogsServiceRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public override ValueTask HandleTracesAsync(OpenTelemetryHandlerContext context, ExportTraceServiceRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public override ValueTask HandleMetricsAsync(OpenTelemetryHandlerContext context, ExportMetricsServiceRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
