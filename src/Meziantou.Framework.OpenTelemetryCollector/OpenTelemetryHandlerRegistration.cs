namespace Meziantou.Framework.OpenTelemetryCollector;

internal sealed class OpenTelemetryHandlerRegistration(OpenTelemetryHandler handler)
{
    public OpenTelemetryHandler Handler { get; } = handler;
}