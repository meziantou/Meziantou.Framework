using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Http;

namespace Meziantou.Framework.OpenTelemetryCollector;

/// <summary>Writes an OTLP response using the same encoding as the request, as required by the OTLP/HTTP specification.</summary>
internal sealed class OpenTelemetryProtoResult<TResponse>(TResponse message, OpenTelemetryPayloadFormat format) : IResult
    where TResponse : class, IMessage<TResponse>
{
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        string contentType;
        byte[] body;
        if (format is OpenTelemetryPayloadFormat.Json)
        {
            contentType = OpenTelemetryHttpPayload.JsonContentType;
            body = Encoding.UTF8.GetBytes(JsonFormatter.Default.Format(message));
        }
        else
        {
            contentType = OpenTelemetryHttpPayload.ProtobufContentType;
            body = message.ToByteArray();
        }

        httpContext.Response.ContentType = contentType;
        httpContext.Response.ContentLength = body.Length;
        return httpContext.Response.Body.WriteAsync(body, httpContext.RequestAborted).AsTask();
    }
}
