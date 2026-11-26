using System.Globalization;
using System.Net;
using Meziantou.Framework.MsQuic;
using Meziantou.Framework.WebTransport.Http3;

namespace Meziantou.Framework.WebTransport;

/// <summary>A WebTransport CONNECT request a <see cref="WebTransportListener"/> received: accept or reject it.</summary>
public sealed class WebTransportSessionRequest
{
    private readonly WebTransportSession _session;
    private readonly MsQuicStream _connectStream;
    private int _responded;

    internal WebTransportSessionRequest(WebTransportSession session, MsQuicStream connectStream, IReadOnlyList<KeyValuePair<string, string>> headers, string authority, string path, IReadOnlyList<string> protocols, IPEndPoint? remoteEndPoint)
    {
        _session = session;
        _connectStream = connectStream;
        Headers = headers;
        Authority = authority;
        Path = path;
        Protocols = protocols;
        RemoteEndPoint = remoteEndPoint;
    }

    /// <summary>Gets the <c>:authority</c> of the request: the host and the port.</summary>
    public string Authority { get; }

    /// <summary>Gets the <c>:path</c> of the request: the path and the query.</summary>
    public string Path { get; }

    /// <summary>Gets the headers of the request, without the pseudo-headers.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

    /// <summary>Gets the value of the <c>origin</c> header, if any.</summary>
    public string? Origin => Headers.FirstOrDefault(header => header.Key == "origin").Value;

    /// <summary>Gets the application protocols the client offers (<c>wt-available-protocols</c>).</summary>
    public IReadOnlyList<string> Protocols { get; }

    /// <summary>Gets the address of the client.</summary>
    public IPEndPoint? RemoteEndPoint { get; }

    /// <summary>Accepts the session with a 200 response.</summary>
    /// <param name="protocol">The application protocol picked from <see cref="Protocols"/>, sent in <c>wt-protocol</c>.</param>
    /// <param name="headers">Additional response headers.</param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    public async ValueTask<WebTransportSession> AcceptAsync(string? protocol = null, IEnumerable<KeyValuePair<string, string>>? headers = null, CancellationToken cancellationToken = default)
    {
        // The response is built first: an invalid protocol throws while the request can still be answered
        var fields = new List<QPackField> { new(":status", "200") };
        if (protocol is not null)
        {
            fields.Add(new("wt-protocol", StructuredFieldStrings.SerializeString(protocol)));
        }

        AddHeaders(fields, headers);
        if (Interlocked.Exchange(ref _responded, 1) == 1)
            throw new InvalidOperationException("The request was already answered.");

        try
        {
            await Http3Connection.WriteHeadersAsync(_connectStream, fields, completeWrites: false, cancellationToken).ConfigureAwait(false);
        }
        catch (MsQuicException ex)
        {
            _session.Terminate(closeInfo: null, new WebTransportException(WebTransportError.SessionReset, "The client abandoned the request.", ex));
            await _session.DisposeAsync().ConfigureAwait(false);
            throw new WebTransportException(WebTransportError.SessionReset, "The client abandoned the request.", ex);
        }

        _session.Start();
        return _session;
    }

    /// <summary>Rejects the session.</summary>
    /// <param name="statusCode">The HTTP status code, outside 200-299.</param>
    /// <param name="headers">Additional response headers.</param>
    /// <param name="cancellationToken">The token to cancel the operation.</param>
    public async ValueTask RejectAsync(int statusCode = 404, IEnumerable<KeyValuePair<string, string>>? headers = null, CancellationToken cancellationToken = default)
    {
        if (statusCode is < 200 or > 999 or (>= 200 and <= 299))
            throw new ArgumentOutOfRangeException(nameof(statusCode), statusCode, "The status code must be a final status outside 200-299.");

        if (Interlocked.Exchange(ref _responded, 1) == 1)
            throw new InvalidOperationException("The request was already answered.");

        var fields = new List<QPackField> { new(":status", statusCode.ToString(CultureInfo.InvariantCulture)) };
        AddHeaders(fields, headers);
        _session.Terminate(closeInfo: null, new WebTransportException(WebTransportError.SessionRejected, "The session was rejected.") { StatusCode = statusCode });
        try
        {
            await Http3Connection.WriteHeadersAsync(_connectStream, fields, completeWrites: true, cancellationToken).ConfigureAwait(false);
        }
        catch (MsQuicException)
        {
        }

        await _session.DisposeAsync().ConfigureAwait(false);
    }

    private static void AddHeaders(List<QPackField> fields, IEnumerable<KeyValuePair<string, string>>? headers)
    {
        if (headers is null)
            return;

        foreach (var header in headers)
        {
            fields.Add(new(header.Key.ToLowerInvariant(), header.Value));
        }
    }
}
