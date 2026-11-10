using System.Diagnostics;
using System.Runtime.CompilerServices;
using Meziantou.Framework.DnsClient.Internal;
using Meziantou.Framework.DnsClient.Protocol;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Meziantou.Framework.DnsClient.Transport;
using DnsResponseCode = Meziantou.Framework.DnsClient.Response.DnsResponseCode;

namespace Meziantou.Framework.DnsClient;

public sealed partial class DnsClient
{
    /// <summary>
    /// Transfers a whole zone (AXFR, RFC 5936). The transfer goes over TCP for a <see cref="DnsClientProtocol.Udp"/> or
    /// <see cref="DnsClientProtocol.Tcp"/> client, and over TLS for a <see cref="DnsClientProtocol.Tls"/> client.
    /// </summary>
    /// <param name="zoneName">The name of the zone. Unicode names are automatically converted to punycode.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The messages of the transfer, as they arrive. The answers of the first message start with the SOA record of the
    /// zone, and the last message ends with the same SOA record. When the server refuses the transfer, the only message
    /// has its response code.
    /// </returns>
    /// <remarks><see cref="DnsClientOptions.Timeout"/> applies to each message, not to the whole transfer.</remarks>
    /// <exception cref="NotSupportedException">The protocol of the client is DNS over HTTPS or DNS over QUIC.</exception>
    /// <exception cref="DnsProtocolException">A message is malformed, does not answer the query, or the server closed the connection before the end of the transfer.</exception>
    /// <exception cref="TimeoutException"><see cref="DnsClientOptions.Timeout"/> elapsed before a message arrived.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public IAsyncEnumerable<DnsResponseMessage> TransferZoneAsync(string zoneName, CancellationToken cancellationToken = default)
    {
        var transport = GetZoneTransferTransport(zoneName);
        return TransferZoneCoreAsync(transport, zoneName, currentSerial: null, cancellationToken);
    }

    /// <summary>
    /// Transfers the changes of a zone since the version the client has (IXFR, RFC 1995). The transfer goes over TCP for a
    /// <see cref="DnsClientProtocol.Udp"/> or <see cref="DnsClientProtocol.Tcp"/> client, and over TLS for a
    /// <see cref="DnsClientProtocol.Tls"/> client.
    /// </summary>
    /// <param name="zoneName">The name of the zone. Unicode names are automatically converted to punycode.</param>
    /// <param name="currentSerial">The serial of the version of the zone the client has.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The messages of the transfer, as they arrive. Their answers start with the SOA record of the current version, then
    /// come the differences: for each version, its old SOA record and the deleted records, then its new SOA record and the
    /// added records. The last message ends with the SOA record of the current version. A server that does not keep the
    /// differences sends the whole zone instead, as <see cref="TransferZoneAsync(string, CancellationToken)"/> does,
    /// and a client that is up to date gets the SOA record alone.
    /// </returns>
    /// <remarks><see cref="DnsClientOptions.Timeout"/> applies to each message, not to the whole transfer.</remarks>
    /// <exception cref="NotSupportedException">The protocol of the client is DNS over HTTPS or DNS over QUIC.</exception>
    /// <exception cref="DnsProtocolException">A message is malformed, does not answer the query, or the server closed the connection before the end of the transfer.</exception>
    /// <exception cref="TimeoutException"><see cref="DnsClientOptions.Timeout"/> elapsed before a message arrived.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    public IAsyncEnumerable<DnsResponseMessage> TransferZoneAsync(string zoneName, uint currentSerial, CancellationToken cancellationToken = default)
    {
        var transport = GetZoneTransferTransport(zoneName);
        return TransferZoneCoreAsync(transport, zoneName, currentSerial, cancellationToken);
    }

    private IDnsStreamTransport GetZoneTransferTransport(string zoneName)
    {
        ArgumentNullException.ThrowIfNull(zoneName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _streamTransport ?? throw new NotSupportedException($"Zone transfers need TCP or DNS over TLS; they are not supported over {_protocol}.");
    }

    private async IAsyncEnumerable<DnsResponseMessage> TransferZoneCoreAsync(IDnsStreamTransport transport, string zoneName, uint? currentSerial, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = new DnsQueryMessage
        {
            RecursionDesired = false,
        };
        query.Questions.Add(new DnsQuestion(zoneName, currentSerial is null ? DnsQueryType.AXFR : DnsQueryType.IXFR, DnsQueryClass.IN));
        var message = CloneWithOptions(query);

        using var activity = DnsTelemetry.ActivitySource.StartActivity("dns.zone_transfer", ActivityKind.Client);
        activity?.SetTag("dns.question.name", message.Questions[0].Name);
        activity?.SetTag("dns.question.type", message.Questions[0].Type.ToString());
        activity?.SetTag("dns.question.class", message.Questions[0].QueryClass.ToString());
        activity?.SetTag("network.transport", GetTransportName(_protocol));

        for (var attempt = 0; ; attempt++)
        {
            var session = await TraceErrorsAsync(activity, () => ZoneTransferSession.StartAsync(this, transport, message, currentSerial, cancellationToken)).ConfigureAwait(false);
            await using (session.ConfigureAwait(false))
            {
                while (true)
                {
                    var batch = await TraceErrorsAsync(activity, () => session.ReadAsync(allowCookieRetry: attempt is 0)).ConfigureAwait(false);
                    if (batch.RetryWithServerCookie)
                        break;

                    var last = batch.Messages[^1];
                    activity?.SetTag("dns.response.code", last.Header.ResponseCode.ToString());
                    if (batch.IsComplete)
                    {
                        activity?.SetTag("dns.zone_transfer.message_count", session.MessageCount);
                        if (last.Header.ResponseCode is DnsResponseCode.NoError)
                        {
                            activity?.SetStatus(ActivityStatusCode.Ok);
                        }
                        else
                        {
                            activity?.SetStatus(ActivityStatusCode.Error, $"DNS response code: {last.Header.ResponseCode}");
                        }
                    }

                    foreach (var response in batch.Messages)
                    {
                        yield return response;
                    }

                    if (batch.IsComplete)
                        yield break;
                }
            }
        }
    }

    /// <summary>Runs a step of a transfer, and marks the activity as failed when it throws.</summary>
    private static async Task<T> TraceErrorsAsync<T>(Activity? activity, Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    /// <summary>The messages a read of a transfer makes available to the caller.</summary>
    private readonly record struct ZoneTransferBatch(IReadOnlyList<DnsResponseMessage> Messages, bool IsComplete, bool RetryWithServerCookie);

    /// <summary>One connection of a zone transfer: the query, then the messages of the answer.</summary>
    private sealed class ZoneTransferSession : IAsyncDisposable
    {
        private readonly DnsClient _client;
        private readonly DnsStreamConnection _connection;
        private readonly DnsQueryMessage _message;
        private readonly ushort _queryId;
        private readonly DnsTsigVerifier? _verifier;
        private readonly DnsZoneTransferState _state;
        private readonly CancellationTokenSource _cts;
        private readonly CancellationToken _cancellationToken;

        // The unsigned messages of a TSIG-signed transfer, held until the next signature authenticates them
        private readonly List<DnsResponseMessage> _pendingMessages = [];

        private ZoneTransferSession(DnsClient client, DnsStreamConnection connection, DnsQueryMessage message, ushort queryId, DnsTsigVerifier? verifier, uint? currentSerial, CancellationTokenSource cts, CancellationToken cancellationToken)
        {
            _client = client;
            _connection = connection;
            _message = message;
            _queryId = queryId;
            _verifier = verifier;
            _state = new DnsZoneTransferState(currentSerial);
            _cts = cts;
            _cancellationToken = cancellationToken;
        }

        public int MessageCount { get; private set; }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session owns the connection, the verifier and the token source")]
        public static async Task<ZoneTransferSession> StartAsync(DnsClient client, IDnsStreamTransport transport, DnsQueryMessage message, uint? currentSerial, CancellationToken cancellationToken)
        {
            // Encoded for each connection: the TSIG signature and the cookie are those of the attempt
            var queryBytes = client.EncodeQuery(message, currentSerial, out var queryId, out var requestMac);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var verifier = client._options.TsigKey is { } key ? new DnsTsigVerifier(key, requestMac, client._options.TimeProvider) : null;
            DnsStreamConnection? connection = null;
            try
            {
                connection = await client.RunWithTimeoutAsync(transport.ConnectAsync, cts, cancellationToken).ConfigureAwait(false);
                await client.RunWithTimeoutAsync(async token => { await connection.WriteMessageAsync(queryBytes, token).ConfigureAwait(false); return true; }, cts, cancellationToken).ConfigureAwait(false);
                return new ZoneTransferSession(client, connection, message, queryId, verifier, currentSerial, cts, cancellationToken);
            }
            catch
            {
                if (connection is not null)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }

                verifier?.Dispose();
                cts.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads messages until some can be given to the caller: the next message, or, in a TSIG-signed transfer, the
        /// unsigned messages and the signed message that authenticates them.
        /// </summary>
        public async Task<ZoneTransferBatch> ReadAsync(bool allowCookieRetry)
        {
            while (true)
            {
                var data = await _client.RunWithTimeoutAsync(ReadMessageAsync, _cts, _cancellationToken).ConfigureAwait(false)
                    ?? throw new DnsProtocolException("The server closed the connection before the end of the zone transfer.");

                var isFirstMessage = MessageCount++ is 0;
                var response = DnsMessageEncoder.DecodeResponse(data);
                ValidateResponseMatchesQuery(_message, _queryId, response, isFirstMessage);
                _verifier?.Verify(data, response);
                var hasServerCookie = isFirstMessage && _client.ProcessCookies(response);

                if (response.Header.ResponseCode is not DnsResponseCode.NoError)
                {
                    // The server refused the transfer, or its TSIG verification failed: the error is the answer
                    if (!isFirstMessage)
                        throw new DnsProtocolException($"The server reported the error {response.Header.ResponseCode} during the zone transfer.");

                    // RFC 7873 section 5.3: the server wants its new cookie, send the query again with it, once
                    if (allowCookieRetry && hasServerCookie && response.Header.ResponseCode is DnsResponseCode.BadCookie)
                        return new ZoneTransferBatch([], IsComplete: false, RetryWithServerCookie: true);

                    return new ZoneTransferBatch([response], IsComplete: true, RetryWithServerCookie: false);
                }

                var isComplete = _state.Process(response.Answers);
                if (_verifier is { IsLastMessageSigned: false })
                {
                    if (isComplete)
                        throw new DnsProtocolException("The last message of the zone transfer is not signed.");

                    _pendingMessages.Add(response);
                    continue;
                }

                List<DnsResponseMessage> messages = [.. _pendingMessages, response];
                _pendingMessages.Clear();
                return new ZoneTransferBatch(messages, isComplete, RetryWithServerCookie: false);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _verifier?.Dispose();
            _cts.Dispose();
        }

        private async Task<byte[]?> ReadMessageAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _connection.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException ex)
            {
                throw new DnsProtocolException("The server closed the connection in the middle of a message of the zone transfer.", ex);
            }
        }
    }

    /// <summary>
    /// Runs one step of a zone transfer, which gets the whole <see cref="DnsClientOptions.Timeout"/>. The timer is
    /// stopped after the step, so the time the caller spends on a message does not count.
    /// </summary>
    private async Task<T> RunWithTimeoutAsync<T>(Func<CancellationToken, Task<T>> action, CancellationTokenSource cts, CancellationToken cancellationToken)
    {
        cts.CancelAfter(_options.Timeout);
        try
        {
            return await action(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The zone transfer timed out after {_options.Timeout} without receiving a message.", ex);
        }
        finally
        {
            cts.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }
}
