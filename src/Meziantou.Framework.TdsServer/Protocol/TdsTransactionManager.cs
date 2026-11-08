using System.Buffers.Binary;
using System.Data;
using System.Net;
using System.Security.Claims;
using Meziantou.Framework.Tds.Handler;

namespace Meziantou.Framework.Tds.Protocol;

/// <summary>Tracks the transaction of a connection and answers the transaction manager requests (MS-TDS 2.2.6.9).</summary>
internal sealed class TdsTransactionManager
{
    private const ushort BeginTransactionRequest = 5;
    private const ushort CommitTransactionRequest = 7;
    private const ushort RollbackTransactionRequest = 8;
    private const ushort SaveTransactionRequest = 9;

    private static long s_lastTransactionId;

    private readonly TdsTransactionDelegate? _handler;
    private readonly EndPoint _remoteEndPoint;
    private readonly ClaimsPrincipal? _userContext;
    private readonly List<string> _savepoints = [];
    private string? _transactionName;

    public TdsTransactionManager(TdsTransactionDelegate? handler, EndPoint remoteEndPoint, ClaimsPrincipal? userContext)
    {
        _handler = handler;
        _remoteEndPoint = remoteEndPoint;
        _userContext = userContext;
    }

    /// <summary>Gets the identifier of the active transaction, or <see langword="null"/> when there is none.</summary>
    public long? TransactionId { get; private set; }

    public async ValueTask<byte[]> ProcessAsync(byte[] payload, CancellationToken cancellationToken)
    {
        if (!TryParse(payload, out var request))
        {
            return TdsResponseSerializer.CreateProtocolError(50001, "Unsupported transaction manager request");
        }

        return request.RequestType switch
        {
            BeginTransactionRequest => await BeginAsync(request, cancellationToken).ConfigureAwait(false),
            CommitTransactionRequest or RollbackTransactionRequest => await EndAsync(request, cancellationToken).ConfigureAwait(false),
            SaveTransactionRequest => await SaveAsync(request, cancellationToken).ConfigureAwait(false),
            _ => TdsResponseSerializer.CreateProtocolError(50001, $"Unsupported transaction manager request: {request.RequestType}"),
        };
    }

    /// <summary>Rolls back the active transaction because the connection is closing, as SQL Server does.</summary>
    public async ValueTask AbortAsync()
    {
        if (TransactionId is not { } transactionId)
        {
            return;
        }

        TransactionId = null;
        if (_handler is null)
        {
            return;
        }

        _ = await _handler(CreateContext(TdsTransactionRequestType.Rollback, transactionId, savepointName: null, IsolationLevel.Unspecified, isConnectionClosing: true), CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<byte[]> BeginAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        if (TransactionId is not null)
        {
            return CreateError(50006, "The connection already has an active transaction.");
        }

        var transactionId = Interlocked.Increment(ref s_lastTransactionId);
        var error = await InvokeHandlerAsync(TdsTransactionRequestType.Begin, transactionId, request.Name, savepointName: null, request.IsolationLevel, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return CreateError(error);
        }

        Begin(transactionId, request.Name);
        return TdsResponseSerializer.CreateTransactionResponse(endedTransactionId: null, committed: false, begunTransactionId: transactionId);
    }

    private async ValueTask<byte[]> EndAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        var commit = request.RequestType is CommitTransactionRequest;
        if (TransactionId is not { } transactionId)
        {
            return commit
                ? CreateError(3902, "The COMMIT TRANSACTION request has no corresponding BEGIN TRANSACTION.")
                : CreateError(3903, "The ROLLBACK TRANSACTION request has no corresponding BEGIN TRANSACTION.");
        }

        // SqlTransaction.Rollback(savePointName) sends the savepoint name. A rollback to a savepoint keeps the
        // transaction open, and the savepoints created after it are gone.
        if (!commit && !string.IsNullOrEmpty(request.Name) && !string.Equals(request.Name, _transactionName, StringComparison.Ordinal))
        {
            var savepointIndex = _savepoints.LastIndexOf(request.Name);
            if (savepointIndex < 0)
            {
                return CreateError(6401, $"Cannot roll back {request.Name}. No transaction or savepoint of that name was found.");
            }

            var savepointError = await InvokeHandlerAsync(TdsTransactionRequestType.Rollback, transactionId, _transactionName, request.Name, IsolationLevel.Unspecified, cancellationToken).ConfigureAwait(false);
            if (savepointError is not null)
            {
                return CreateError(savepointError);
            }

            _savepoints.RemoveRange(savepointIndex + 1, _savepoints.Count - savepointIndex - 1);
            return TdsResponseSerializer.CreateTransactionResponse(endedTransactionId: null, committed: false, begunTransactionId: null);
        }

        var error = await InvokeHandlerAsync(commit ? TdsTransactionRequestType.Commit : TdsTransactionRequestType.Rollback, transactionId, _transactionName, savepointName: null, IsolationLevel.Unspecified, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return CreateError(error);
        }

        TransactionId = null;
        _transactionName = null;
        _savepoints.Clear();

        // fBeginXact: the client asks for a new transaction as soon as this one ends.
        long? newTransactionId = null;
        if (request.BeginNewTransaction)
        {
            var nextTransactionId = Interlocked.Increment(ref s_lastTransactionId);
            var beginError = await InvokeHandlerAsync(TdsTransactionRequestType.Begin, nextTransactionId, request.NewName, savepointName: null, request.IsolationLevel, cancellationToken).ConfigureAwait(false);
            if (beginError is not null)
            {
                return TdsResponseSerializer.CreateTransactionResponse(transactionId, commit, begunTransactionId: null, beginError);
            }

            Begin(nextTransactionId, request.NewName);
            newTransactionId = nextTransactionId;
        }

        return TdsResponseSerializer.CreateTransactionResponse(transactionId, commit, newTransactionId);
    }

    private async ValueTask<byte[]> SaveAsync(TransactionRequest request, CancellationToken cancellationToken)
    {
        if (TransactionId is not { } transactionId)
        {
            return CreateError(628, "Cannot issue SAVE TRANSACTION when there is no active transaction.");
        }

        if (string.IsNullOrEmpty(request.Name))
        {
            return CreateError(50001, "A savepoint requires a name.");
        }

        var error = await InvokeHandlerAsync(TdsTransactionRequestType.Save, transactionId, _transactionName, request.Name, IsolationLevel.Unspecified, cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return CreateError(error);
        }

        _savepoints.Add(request.Name);
        return TdsResponseSerializer.CreateTransactionResponse(endedTransactionId: null, committed: false, begunTransactionId: null);
    }

    private void Begin(long transactionId, string? name)
    {
        TransactionId = transactionId;
        _transactionName = string.IsNullOrEmpty(name) ? null : name;
        _savepoints.Clear();
    }

    private ValueTask<TdsQueryError?> InvokeHandlerAsync(TdsTransactionRequestType requestType, long transactionId, string? transactionName, string? savepointName, IsolationLevel isolationLevel, CancellationToken cancellationToken)
    {
        if (_handler is null)
        {
            return ValueTask.FromResult<TdsQueryError?>(null);
        }

        var context = CreateContext(requestType, transactionId, savepointName, isolationLevel, isConnectionClosing: false, transactionName);
        return _handler(context, cancellationToken);
    }

    private TdsTransactionContext CreateContext(TdsTransactionRequestType requestType, long transactionId, string? savepointName, IsolationLevel isolationLevel, bool isConnectionClosing, string? transactionName = null)
    {
        return new TdsTransactionContext
        {
            RemoteEndPoint = _remoteEndPoint,
            UserContext = _userContext,
            RequestType = requestType,
            TransactionId = transactionId,
            TransactionName = string.IsNullOrEmpty(transactionName) ? _transactionName : transactionName,
            SavepointName = savepointName,
            IsolationLevel = isolationLevel,
            IsConnectionClosing = isConnectionClosing,
        };
    }

    private static byte[] CreateError(uint number, string message)
    {
        return CreateError(new TdsQueryError { Number = number, Message = message });
    }

    private static byte[] CreateError(TdsQueryError error)
    {
        return TdsResponseSerializer.CreateTransactionResponse(endedTransactionId: null, committed: false, begunTransactionId: null, error);
    }

    private static bool TryParse(byte[] payload, out TransactionRequest request)
    {
        request = default;
        ReadOnlySpan<byte> data = payload.AsSpan(TdsQueryRequestParser.GetPayloadOffsetAfterAllHeaders(payload));
        if (data.Length < 2)
        {
            return false;
        }

        var requestType = BinaryPrimitives.ReadUInt16LittleEndian(data);
        data = data[2..];
        switch (requestType)
        {
            case BeginTransactionRequest:
                {
                    if (data.Length < 1)
                        return false;

                    var isolationLevel = ToIsolationLevel(data[0]);
                    data = data[1..];
                    if (!TryReadName(ref data, out var name))
                        return false;

                    request = new TransactionRequest(requestType, name, isolationLevel, BeginNewTransaction: false, NewName: null);
                    return true;
                }

            case CommitTransactionRequest:
            case RollbackTransactionRequest:
                {
                    if (!TryReadName(ref data, out var name))
                        return false;

                    if (data.Length < 1)
                        return false;

                    var beginNewTransaction = (data[0] & 0x01) != 0;
                    data = data[1..];
                    var isolationLevel = IsolationLevel.Unspecified;
                    string? newName = null;
                    if (beginNewTransaction)
                    {
                        if (data.Length < 1)
                            return false;

                        isolationLevel = ToIsolationLevel(data[0]);
                        data = data[1..];
                        if (!TryReadName(ref data, out newName))
                            return false;
                    }

                    request = new TransactionRequest(requestType, name, isolationLevel, beginNewTransaction, newName);
                    return true;
                }

            case SaveTransactionRequest:
                {
                    if (!TryReadName(ref data, out var name))
                        return false;

                    request = new TransactionRequest(requestType, name, IsolationLevel.Unspecified, BeginNewTransaction: false, NewName: null);
                    return true;
                }

            default:
                request = new TransactionRequest(requestType, Name: null, IsolationLevel.Unspecified, BeginNewTransaction: false, NewName: null);
                return true;
        }
    }

    /// <summary>Reads a name: unlike most strings of the protocol, its length prefix counts bytes, not characters.</summary>
    private static bool TryReadName(ref ReadOnlySpan<byte> data, out string? value)
    {
        value = null;
        if (data.Length < 1)
        {
            return false;
        }

        var byteLength = data[0];
        if (byteLength % 2 != 0 || data.Length < 1 + byteLength)
        {
            return false;
        }

        value = byteLength == 0 ? null : Encoding.Unicode.GetString(data.Slice(1, byteLength));
        data = data[(1 + byteLength)..];
        return true;
    }

    private static IsolationLevel ToIsolationLevel(byte value)
    {
        return value switch
        {
            1 => IsolationLevel.ReadUncommitted,
            2 => IsolationLevel.ReadCommitted,
            3 => IsolationLevel.RepeatableRead,
            4 => IsolationLevel.Serializable,
            5 => IsolationLevel.Snapshot,
            _ => IsolationLevel.Unspecified,
        };
    }

    private readonly record struct TransactionRequest(ushort RequestType, string? Name, IsolationLevel IsolationLevel, bool BeginNewTransaction, string? NewName);
}
