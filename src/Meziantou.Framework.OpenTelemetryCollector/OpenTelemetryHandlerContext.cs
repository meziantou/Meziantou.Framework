namespace Meziantou.Framework.OpenTelemetryCollector;

public readonly struct OpenTelemetryHandlerContext
{
    private readonly OpenTelemetryPartialSuccess? _partialSuccess;

    internal OpenTelemetryHandlerContext(OpenTelemetryTransport transport, string method, OpenTelemetryPartialSuccess partialSuccess)
    {
        Transport = transport;
        Method = method;
        _partialSuccess = partialSuccess;
    }

    public string Method { get; }

    public OpenTelemetryTransport Transport { get; }

    /// <summary>Gets the object used to report the records that could not be accepted, so they are sent back to the client in the OTLP <c>partial_success</c> response field.</summary>
    public OpenTelemetryPartialSuccess PartialSuccess => _partialSuccess ?? OpenTelemetryPartialSuccess.Discarded;

    internal static OpenTelemetryHandlerContext CreateHttp(string method, OpenTelemetryPartialSuccess partialSuccess)
    {
        return new OpenTelemetryHandlerContext(OpenTelemetryTransport.Http, method, partialSuccess);
    }

    internal static OpenTelemetryHandlerContext CreateGrpc(string method, OpenTelemetryPartialSuccess partialSuccess)
    {
        return new OpenTelemetryHandlerContext(OpenTelemetryTransport.Grpc, method, partialSuccess);
    }
}
