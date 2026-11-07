using System.Net;

namespace Meziantou.Framework.TemporaryContainers;

/// <summary>Options of the wait strategy created by <see cref="Wait.ForHttp(int, string, Action{HttpWaitStrategyOptions}?)"/>.</summary>
public sealed class HttpWaitStrategyOptions : PollingWaitStrategyOptions
{
    /// <summary>Gets or sets the HTTP method of the request. The default value is <see cref="HttpMethod.Get"/>.</summary>
    public HttpMethod Method { get; set; } = HttpMethod.Get;

    /// <summary>Gets the status codes that mean the container is ready. When the collection is empty, any 2xx status code does.</summary>
    public ICollection<HttpStatusCode> StatusCodes { get; } = new List<HttpStatusCode>();

    /// <summary>Gets or sets a predicate the response body must satisfy, in addition to the status code. When <see langword="null"/>, the body is not read.</summary>
    public Func<string, bool>? ResponseBodyPredicate { get; set; }

    /// <summary>Gets or sets a value indicating whether the request uses HTTPS instead of HTTP.</summary>
    public bool UseHttps { get; set; }

    /// <summary>Gets or sets a value indicating whether the certificate of the server is validated. Set it to <see langword="false"/> for a container that serves a self-signed certificate. The default value is <see langword="true"/>.</summary>
    public bool ValidateServerCertificate { get; set; } = true;

    /// <summary>Gets or sets the time a single request has to complete before it counts as a failed check. The default value is 5 seconds.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(5);

    internal override void Validate(string paramName)
    {
        base.Validate(paramName);
        if (Method is null)
            throw new ArgumentException($"{nameof(Method)} must not be null.", paramName);

        if (RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentException($"{nameof(RequestTimeout)} must be positive.", paramName);
    }
}
