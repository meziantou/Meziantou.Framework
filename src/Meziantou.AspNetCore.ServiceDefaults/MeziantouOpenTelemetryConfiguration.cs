using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Meziantou.AspNetCore.ServiceDefaults;

public sealed class MeziantouOpenTelemetryConfiguration
{
    public Action<OpenTelemetryLoggerOptions>? ConfigureLogging { get; set; }
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }
}
