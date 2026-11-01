using System.Net;

namespace Meziantou.Framework.TemporaryContainers.Strategies;

/// <summary>Waits until an HTTP endpoint of the container answers with an expected response.</summary>
internal sealed class HttpWaitStrategy : IWaitStrategy
{
    private readonly int _containerPort;
    private readonly string _path;
    private readonly HttpMethod _method;
    private readonly HttpStatusCode[] _statusCodes;
    private readonly Func<string, bool>? _responseBodyPredicate;
    private readonly bool _useHttps;
    private readonly bool _validateServerCertificate;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _requestTimeout;

    /// <summary>Copies the options, so that changing them once the strategy is created does not change the strategy.</summary>
    public HttpWaitStrategy(int containerPort, string path, HttpWaitStrategyOptions options)
    {
        _containerPort = containerPort;
        _path = path;
        _method = options.Method;
        _statusCodes = [.. options.StatusCodes];
        _responseBodyPredicate = options.ResponseBodyPredicate;
        _useHttps = options.UseHttps;
        _validateServerCertificate = options.ValidateServerCertificate;
        _pollInterval = options.PollInterval;
        _requestTimeout = options.RequestTimeout;
    }

    public Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        return WaitCoreAsync(GetUri(container), cancellationToken);
    }

    /// <summary>The one place that decides where the endpoint is reached from: the published host port, on the loopback address.</summary>
    private Uri GetUri(TemporaryContainer container)
    {
        var scheme = _useHttps ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var baseUri = new UriBuilder(scheme, IPAddress.Loopback.ToString(), container.GetMappedPort(_containerPort)).Uri;
        return new Uri(baseUri, _path);
    }

    /// <summary>The wait itself, taking the address of the endpoint so it can be tested without a container.</summary>
    internal async Task WaitCoreAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler();
        if (!_validateServerCertificate)
        {
#pragma warning disable CA5359 // Do not disable certificate validation: the user opted out, for a container that serves a self-signed certificate
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
        }

        using var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
        while (true)
        {
            if (await IsReadyAsync(client, uri, cancellationToken).ConfigureAwait(false))
                return;

            await Task.Delay(_pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> IsReadyAsync(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(_requestTimeout);
        try
        {
            using var request = new HttpRequestMessage(_method, uri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestCts.Token).ConfigureAwait(false);
            if (!IsExpectedStatusCode(response.StatusCode))
                return false;

            if (_responseBodyPredicate is null)
                return true;

            var body = await response.Content.ReadAsStringAsync(requestCts.Token).ConfigureAwait(false);
            return _responseBodyPredicate(body);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // Not listening yet, or a port forwarder that accepted the connection and dropped it.
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The request timed out: the service may be too busy starting to answer yet.
            return false;
        }
    }

    private bool IsExpectedStatusCode(HttpStatusCode statusCode)
    {
        if (_statusCodes.Length == 0)
            return (int)statusCode is >= 200 and <= 299;

        return Array.IndexOf(_statusCodes, statusCode) >= 0;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{(_useHttps ? "HTTPS" : "HTTP")} {_method} {_path} on port {_containerPort}");
}
