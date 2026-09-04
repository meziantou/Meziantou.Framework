namespace Meziantou.Framework.OpenTelemetryCollector;

public readonly struct OpenTelemetryHandlerContext
{
    private readonly string? _method;
    private readonly OpenTelemetryPartialSuccess? _partialSuccess;

    /// <summary>Initializes a context. Handlers receive one from the receiver; this constructor exists so they can be unit tested.</summary>
    public OpenTelemetryHandlerContext(OpenTelemetryTransport transport, string method, OpenTelemetryPartialSuccess partialSuccess)
    {
        Transport = transport;
        _method = method;
        _partialSuccess = partialSuccess;
    }

    /// <summary>Gets the transport method that carried the request, or an empty string for a default instance.</summary>
    public string Method => _method ?? "";

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
