namespace Meziantou.Framework.OpenTelemetryCollector;

public delegate ValueTask<bool> OpenTelemetryRequestFilter<in TRequest>(OpenTelemetryHandlerContext context, TRequest request, CancellationToken cancellationToken);
