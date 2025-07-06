using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Meziantou.AspNetCore.ServiceDefaults;

public sealed class MeziantouAntiForgeryConfiguration
{
    public bool Enabled { get; set; } = true;
}
