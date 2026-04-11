using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Http.Recording;

/// <summary>Thrown when no recorded response matches an incoming request during replay.</summary>
[SuppressMessage("Design", "CA1032:Implement standard exception constructors")]
public sealed class HttpRecordingMissException : InvalidOperationException
{
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings")]
    public HttpRecordingMissException(string method, string requestUri)
        : base($"No recorded response found for {method} {requestUri}.")
    {
        Method = method;
        RequestUri = requestUri;
    }

    /// <summary>Gets the HTTP method of the unmatched request.</summary>
    public string Method { get; }

    /// <summary>Gets the request URI of the unmatched request.</summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings")]
    public string RequestUri { get; }
}
