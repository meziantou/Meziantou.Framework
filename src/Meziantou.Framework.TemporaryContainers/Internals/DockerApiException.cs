using System.Net;

namespace Meziantou.Framework.TemporaryContainers.Internals;

/// <summary>Reports a Docker Engine API request that the daemon refused, or an operation that the daemon reported as failed in the body of a response whose status is a success.</summary>
/// <remarks>It derives from <see cref="InvalidOperationException"/> because that is the exception these code paths have always thrown.</remarks>
internal sealed class DockerApiException : InvalidOperationException
{
    public DockerApiException()
    {
    }

    public DockerApiException(string message)
        : base(message)
    {
    }

    public DockerApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DockerApiException(string message, HttpStatusCode? statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The status of the response, or <see langword="null"/> when the daemon reported the failure in the body of a response whose status is a success.</summary>
    public HttpStatusCode? StatusCode { get; }
}
