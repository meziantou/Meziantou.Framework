using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Classifies the failures of a registry operation that are worth another attempt.</summary>
internal static class TransientError
{
    // A registry failure rarely comes with a status code: the daemon reports most of them in the body of a response
    // whose status is a success, and the CLIs only ever expose them through their standard error, with an exit code
    // that is the same for every kind of failure. The messages below are the ones a second attempt can resolve.
    // Anything else is treated as permanent, so an unknown manifest or a rejected credential still fails at once.
    private static readonly string[] TransientMessages =
    [
        "toomanyrequests",
        "too many requests",
        "tls handshake timeout",
        "i/o timeout",
        "unexpected eof",
        "connection reset by peer",
        "connection refused",
        "no route to host",
        "temporary failure in name resolution",
        "server misbehaving",
        "timeout exceeded while awaiting headers",
        "internal server error",
        "service unavailable",
        "bad gateway",
        "gateway timeout",
        "unexpected http status: 408",
        "unexpected http status: 429",
        "unexpected http status: 500",
        "unexpected http status: 502",
        "unexpected http status: 503",
        "unexpected http status: 504",
    ];

    public static bool IsTransient(Exception exception) => exception switch
    {
        DockerApiException { StatusCode: { } statusCode } => IsTransientStatusCode(statusCode),
        DockerApiException => IsTransientMessage(exception.Message),
        ContainerRuntimeException runtimeException => IsTransientMessage(runtimeException.StandardError) || IsTransientMessage(runtimeException.StandardOutput),
        HttpRequestException or IOException or SocketException or TimeoutException => true,
        _ => false,
    };

    public static bool IsTransientStatusCode(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or (>= HttpStatusCode.InternalServerError and <= (HttpStatusCode)599);

    public static bool IsTransientMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return false;

        foreach (var transientMessage in TransientMessages)
        {
            if (message.Contains(transientMessage, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
