using System.Net;
using System.Net.Security;
using System.Threading.Channels;
using Meziantou.Framework.MsQuic;
using Meziantou.Framework.WebTransport.Http3;

namespace Meziantou.Framework.WebTransport;

/// <summary>Listens for WebTransport sessions over HTTP/3.</summary>
/// <remarks>
/// The listener accepts the QUIC connections and their WebTransport CONNECT requests. The other requests are answered
/// with a 501 response. A connection can carry several sessions.
/// </remarks>
public sealed class WebTransportListener : IAsyncDisposable
{
    private readonly MsQuicListener _listener;
    private readonly WebTransportListenerOptions _options;
    private readonly Channel<WebTransportSessionRequest> _requests = Channel.CreateUnbounded<WebTransportSessionRequest>();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly Lock _lock = new();
    private readonly List<Http3Connection> _connections = [];
    private int _disposed;

    private WebTransportListener(MsQuicListener listener, WebTransportListenerOptions options)
    {
        _listener = listener;
        _options = options;
        _ = AcceptConnectionsAsync();
    }

    /// <summary>Gets the address the listener listens on.</summary>
    public IPEndPoint LocalEndPoint => _listener.LocalEndPoint;

    /// <summary>Starts listening.</summary>
    /// <exception cref="PlatformNotSupportedException">MsQuic is not available.</exception>
    public static async ValueTask<WebTransportListener> ListenAsync(WebTransportListenerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.ServerCertificate is null)
            throw new ArgumentException($"{nameof(WebTransportListenerOptions.ServerCertificate)} is required.", nameof(options));

        var connectionOptions = new MsQuicServerConnectionOptions
        {
            EnableDatagrams = true,
            MaxInboundBidirectionalStreams = options.MaxIncomingStreams,
            MaxInboundUnidirectionalStreams = Math.Min(ushort.MaxValue, options.MaxIncomingStreams + 3),
            IdleTimeout = options.IdleTimeout,
            CongestionControlAlgorithm = options.CongestionControlAlgorithm,
            ReceivedDatagramQueueCapacity = Math.Max(options.ReceivedDatagramQueueCapacity, 1),
            DefaultCloseErrorCode = Http3ErrorCode.NoError,
            DefaultStreamErrorCode = Http3ErrorCode.WebTransportSessionGone,
            ServerAuthenticationOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = options.ServerCertificate,
                ApplicationProtocols = [SslApplicationProtocol.Http3],
            },
        };

        var listenerOptions = new MsQuicListenerOptions
        {
            ListenEndPoint = options.ListenEndPoint,
            ConnectionOptionsCallback = (_, _) => ValueTask.FromResult(connectionOptions),
        };
        listenerOptions.ApplicationProtocols.Add(SslApplicationProtocol.Http3);
        var listener = await MsQuicListener.ListenAsync(listenerOptions, cancellationToken).ConfigureAwait(false);
        return new WebTransportListener(listener, options);
    }

    /// <summary>Waits for the next WebTransport CONNECT request.</summary>
    /// <exception cref="ObjectDisposedException">The listener is disposed.</exception>
    public async ValueTask<WebTransportSessionRequest> AcceptAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        try
        {
            return await _requests.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            throw new ObjectDisposedException(GetType().FullName, ex);
        }
    }

    private async Task AcceptConnectionsAsync()
    {
        try
        {
            while (true)
            {
                var quic = await _listener.AcceptConnectionAsync(_disposeCancellation.Token).ConfigureAwait(false);
                _ = StartConnectionAsync(quic);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task StartConnectionAsync(MsQuicConnection quic)
    {
        var connection = new Http3Connection(quic, OnRequest);
        lock (_lock)
        {
            if (_disposed != 0)
            {
                _ = connection.DisposeAsync().AsTask();
                return;
            }

            _connections.Add(connection);
        }

        try
        {
            await connection.StartAsync(_disposeCancellation.Token).ConfigureAwait(false);
            await quic.Closed.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MsQuicException or OperationCanceledException or ObjectDisposedException)
        {
        }

        lock (_lock)
        {
            _connections.Remove(connection);
        }

        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void OnRequest(Http3Connection connection, MsQuicStream stream, Http3FrameReader reader, List<QPackField> fields)
    {
        _ = HandleRequestAsync(connection, stream, reader, fields);
    }

    private async Task HandleRequestAsync(Http3Connection connection, MsQuicStream stream, Http3FrameReader reader, List<QPackField> fields)
    {
        string? method = null;
        string? protocol = null;
        string? scheme = null;
        string? authority = null;
        string? path = null;
        var headers = new List<KeyValuePair<string, string>>();
        var valid = true;
        var pseudoHeadersEnded = false;
        foreach (var field in fields)
        {
            // Field names are lowercase in HTTP/3, and the pseudo-headers come first (RFC 9114, section 4.3)
            if (field.Name.Length == 0 || field.Name.Any(char.IsAsciiLetterUpper))
            {
                valid = false;
                break;
            }

            if (field.Name is [':', ..])
            {
                if (pseudoHeadersEnded)
                {
                    valid = false;
                    break;
                }

                switch (field.Name)
                {
                    case ":method" when method is null:
                        method = field.Value;
                        break;
                    case ":protocol" when protocol is null:
                        protocol = field.Value;
                        break;
                    case ":scheme" when scheme is null:
                        scheme = field.Value;
                        break;
                    case ":authority" when authority is null:
                        authority = field.Value;
                        break;
                    case ":path" when path is null:
                        path = field.Value;
                        break;
                    default:
                        valid = false;
                        break;
                }
            }
            else
            {
                pseudoHeadersEnded = true;
                headers.Add(KeyValuePair.Create(field.Name, field.Value));
            }
        }

        try
        {
            // A request that does not open a session (malformed, or not WebTransport): the streams that reference it are rejected
            if (!valid || method is null)
            {
                connection.CloseSessionId(stream.Id);
                stream.Abort(MsQuicAbortDirection.Both, Http3ErrorCode.MessageError);
                await stream.DisposeAsync().ConfigureAwait(false);
                return;
            }

            if (method != "CONNECT" || protocol != "webtransport" || scheme != "https" || string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(path))
            {
                // The listener only serves WebTransport
                connection.CloseSessionId(stream.Id);
                var status = method == "CONNECT" && protocol is not null && protocol != "webtransport" ? "501" : method == "CONNECT" && protocol == "webtransport" ? "400" : "501";
                await Http3Connection.WriteHeadersAsync(stream, [new(":status", status)], completeWrites: true, _disposeCancellation.Token).ConfigureAwait(false);
                stream.Abort(MsQuicAbortDirection.Read, Http3ErrorCode.NoError);
                await stream.DisposeAsync().ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex) when (ex is MsQuicException or OperationCanceledException or ObjectDisposedException)
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            return;
        }

        var protocolsHeader = headers.FirstOrDefault(header => header.Key == "wt-available-protocols").Value;
        var protocols = protocolsHeader is null ? [] : StructuredFieldStrings.ParseList(protocolsHeader) ?? [];
        var session = new WebTransportSession(connection, stream, reader, _options.ReceivedDatagramQueueCapacity, ownsConnection: false);
        connection.RegisterSession(session);
        var request = new WebTransportSessionRequest(session, stream, headers, authority, path, protocols, connection.Quic.RemoteEndPoint);
        if (!_requests.Writer.TryWrite(request))
        {
            await request.RejectAsync(503).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await _disposeCancellation.CancelAsync().ConfigureAwait(false);
        _requests.Writer.TryComplete();
        await _listener.DisposeAsync().ConfigureAwait(false);
        Http3Connection[] connections;
        lock (_lock)
        {
            connections = [.. _connections];
            _connections.Clear();
        }

        foreach (var connection in connections)
        {
            await connection.CloseAsync(Http3ErrorCode.NoError).ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        while (_requests.Reader.TryRead(out var request))
        {
            try
            {
                await request.RejectAsync(503).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
            }
        }

        _disposeCancellation.Dispose();
    }
}
