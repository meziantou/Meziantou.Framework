namespace Meziantou.Framework.OpenTelemetryCollector;

internal class OpenTelemetryHandlerRegistration(OpenTelemetryHandler handler)
{
    public OpenTelemetryHandler Handler { get; } = handler;
}

/// <summary>A registration keyed on the handler type, so registering the same handler type twice is a no-op.</summary>
internal sealed class OpenTelemetryHandlerRegistration<TReceiver>(TReceiver handler) : OpenTelemetryHandlerRegistration(handler)
    where TReceiver : OpenTelemetryHandler
{
}
