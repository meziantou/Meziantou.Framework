namespace Meziantou.Framework.OpenTelemetryCollector;

public readonly struct OpenTelemetryHandlerContext
{
    internal OpenTelemetryHandlerContext(OpenTelemetryTransport transport, string method)
    {
        Transport = transport;
        Method = method;
    }

    public string Method { get; }

    public OpenTelemetryTransport Transport { get; }

    internal static OpenTelemetryHandlerContext CreateHttp(string method)
    {
        return new OpenTelemetryHandlerContext(OpenTelemetryTransport.Http, method);
    }

    internal static OpenTelemetryHandlerContext CreateGrpc(string method)
    {
        return new OpenTelemetryHandlerContext(OpenTelemetryTransport.Grpc, method);
    }
}
