using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Meziantou.Framework.NodeJs.Internal;

/// <summary>A connection with a thread of the Node.js process that runs calls: the main thread, or a worker thread when <see cref="NodeJsHostOptions.WorkerThreads"/> is set.</summary>
/// <remarks>
/// Messages are newline-delimited JSON. The first message received is the handshake. Requests start with their identifier, which is unique in the host,
/// and their response starts with it. The main thread of a process that runs worker threads also sends notices, which start with their type.
/// </remarks>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "AvailableWaitHandle of the write lock is never used, and the stream is closed by CloseAsync")]
internal sealed class NodeJsChannel
{
    // Initial size of the buffer that receives messages. It grows to contain the largest message, and shrinks back once the message is processed.
    private const int ReadBufferSize = 16 * 1024;

    private readonly NodeJsHost _host;
    private readonly Stream _stream;
    private readonly ConcurrentDictionary<long, PendingRequest> _pendingRequests = new();
    private readonly ConcurrentDictionary<long, byte> _abandonedRequests = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource<byte[]> _hello = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _terminationException;
    private int _abandonedCalls;

    public NodeJsChannel(NodeJsHost host, Stream stream)
    {
        _host = host;
        _stream = stream;
    }

    public NodeJsHost Host => _host;

    /// <summary>Gets or sets the index of the worker thread, or <see langword="null"/> for the main thread and for a worker thread whose handshake is not validated yet.</summary>
    public int? Worker { get; set; }

    /// <summary>Gets or sets the generation of the worker thread: it increases each time a worker thread is restarted.</summary>
    public int Generation { get; set; }

    /// <summary>Gets the handshake, the first message sent by the thread.</summary>
    public Task<byte[]> Hello => _hello.Task;

    /// <summary>Gets a task that completes once the connection is closed and the host processed it.</summary>
    public Task ReadTask { get; private set; } = Task.CompletedTask;

    /// <summary>Gets the number of canceled calls whose response has not been received yet, so their JavaScript code may still be running.</summary>
    public int AbandonedCalls => Volatile.Read(ref _abandonedCalls);

    public bool IsTerminated => Volatile.Read(ref _terminationException) is not null;

    public void StartReading()
    {
        ReadTask = ReadMessagesAsync();
    }

    /// <summary>Gets a value indicating whether a call is canceled while its JavaScript code may still be running.</summary>
    public bool IsAbandoned(long id) => _abandonedRequests.ContainsKey(id);

    /// <summary>Fails the handshake, when the connection is closed before receiving it.</summary>
    public void FailHello(Exception exception) => _hello.TrySetException(exception);

    /// <summary>Closes the connection. The thread stops once its connection is closed.</summary>
    public ValueTask CloseAsync() => _stream.DisposeAsync();

    /// <summary>Fails the pending calls and the future calls of this connection.</summary>
    public void Terminate(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _terminationException, exception, comparand: null) is not null)
            return;

        foreach (var (_, request) in _pendingRequests)
        {
            request.SetException(exception);
        }
    }

    public void ThrowIfTerminated()
    {
        var exception = Volatile.Read(ref _terminationException);
        if (exception is not null)
        {
            _host.ThrowTerminationException(exception);
        }
    }

    /// <summary>Releases a value referenced by a <see cref="JSReference"/>, without waiting for the message to be sent.</summary>
    public void ReleaseReference(long referenceId)
    {
        _ = ReleaseReferenceAsync(referenceId);
    }

    /// <summary>Releases a value referenced by a <see cref="JSReference"/>. Never throws: when the thread is gone, there is nothing to release.</summary>
    public async Task ReleaseReferenceAsync(long referenceId)
    {
        if (IsTerminated)
            return;

        try
        {
            await WriteMessageAsync(writer =>
            {
                writer.WriteString("type", "release");
                writer.WriteNumber("reference", referenceId);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The connection is lost, so the thread and its values are gone
        }
    }

    /// <summary>Writes a message that has no response.</summary>
    public async Task WriteMessageAsync(Action<Utf8JsonWriter> writeMessage)
    {
        PooledBufferWriter? message = SerializeMessage(writeMessage);
        try
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            var writeTask = WriteMessageAndReleaseLockAsync(message);
            message = null;
            await writeTask.ConfigureAwait(false);
        }
        finally
        {
            message?.Dispose();
        }
    }

    /// <summary>Sends a request and waits for its response.</summary>
    /// <param name="id">The identifier of the request, unique in the host.</param>
    public async Task<T> SendRequestAsync<T>(long id, Action<Utf8JsonWriter> writeMessage, ResultKind resultKind, ResultReader<T> readResult, CancellationToken cancellationToken)
    {
        PooledBufferWriter? message = SerializeMessage(writer =>
        {
            writer.WriteNumber("id", id);
            switch (resultKind)
            {
                case ResultKind.Void:
                    writer.WriteString("returns", "void");
                    break;
                case ResultKind.Reference:
                    writer.WriteString("returns", "reference");
                    break;
            }

            writeMessage(writer);
        });

        var request = new PendingRequest<T>(readResult, hasResult: resultKind is not ResultKind.Void);
        _pendingRequests[id] = request;
        try
        {
            // Terminate sets the exception before failing pending requests, so a request added concurrently is never left pending
            ThrowIfTerminated();

            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            // Once started, the message is always written completely, as a partial message would corrupt the following ones.
            // Canceling only stops waiting for the write to complete.
            var writeTask = WriteMessageAndReleaseLockAsync(message);
            message = null;
            try
            {
                await writeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            // On Windows, disposing the named pipe cancels the pending write, which throws OperationCanceledException
            catch (Exception ex) when ((writeTask.IsFaulted || writeTask.IsCanceled) && ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // The connection is lost, e.g. because the thread exited, and the host may still be processing it (e.g. waiting for the exit code).
                // The read loop terminates the connection with the cause of the failure, which explains it better than the write error.
                await ReadTask.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                ThrowIfTerminated();
                cancellationToken.ThrowIfCancellationRequested();
                throw new NodeJsException("Cannot send the message to the Node.js process.", ex);
            }
            catch (OperationCanceledException)
            {
                // The write continues in the background. A failure means the connection is lost, which is reported by the read loop.
                _ = writeTask.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                AbandonRequest(id);
                throw;
            }

            try
            {
                return await request.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ProcessResponse releases the value when the response arrives after this point.
                // When the response is already being processed, the value must be released here.
                if (!AbandonRequest(id) && resultKind is ResultKind.Reference)
                {
                    _ = request.Task.ContinueWith(task =>
                    {
                        if (task.Result is JSReference reference)
                        {
                            reference.Dispose();
                        }
                    }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }

                throw;
            }
        }
        finally
        {
            _pendingRequests.TryRemove(id, out _);
            message?.Dispose();
        }
    }

    /// <summary>Stops waiting for the response of a request that was sent.</summary>
    /// <returns><see langword="false"/> when the response is already being processed.</returns>
    private bool AbandonRequest(long id)
    {
        // The request is added before being removed from the pending requests, so a response received concurrently always finds it in one of them
        _abandonedRequests[id] = 0;
        if (!_pendingRequests.TryRemove(id, out _))
        {
            _abandonedRequests.TryRemove(id, out _);
            return false;
        }

        // The call keeps running until its response arrives, and its JavaScript code may block the event loop
        Interlocked.Increment(ref _abandonedCalls);
        _host.OnRequestAbandoned();
        return true;
    }

    /// <summary>Serializes a message. The caller must dispose the returned buffer, or pass it to <see cref="WriteMessageAndReleaseLockAsync"/>.</summary>
    private static PooledBufferWriter SerializeMessage(Action<Utf8JsonWriter> writeMessage)
    {
        var buffer = new PooledBufferWriter();
        try
        {
            using (var writer = new Utf8JsonWriter(buffer, NodeJsHost.MessageWriterOptions))
            {
                writer.WriteStartObject();
                writeMessage(writer);
                writer.WriteEndObject();
            }

            // Messages are separated by line breaks. Utf8JsonWriter escapes them in strings, but raw JSON written by a custom converter can contain them as whitespace.
            if (buffer.WrittenMemory.Span.Contains((byte)'\n'))
            {
                var minified = Minify(buffer.WrittenMemory);
                buffer.Dispose();
                buffer = minified;
            }

            buffer.Write("\n"u8);
            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    private static PooledBufferWriter Minify(ReadOnlyMemory<byte> json)
    {
        var buffer = new PooledBufferWriter();
        try
        {
            using (var document = JsonDocument.Parse(json, NodeJsHost.UnlimitedDepthDocumentOptions))
            using (var writer = new Utf8JsonWriter(buffer, NodeJsHost.MessageWriterOptions))
            {
                document.WriteTo(writer);
            }

            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    /// <summary>Writes a message, then releases the write lock and the message buffer.</summary>
    private async Task WriteMessageAndReleaseLockAsync(PooledBufferWriter message)
    {
        try
        {
            await _stream.WriteAsync(message.WrittenMemory, CancellationToken.None).ConfigureAwait(false);
            await _stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
            message.Dispose();
        }
    }

    private async Task ReadMessagesAsync()
    {
        Exception? error = null;

        // Messages are read as UTF-8 bytes and parsed in place, so a large result is not converted to a string, and only the parsed result is allocated.
        // Messages are separated by a line break, which JSON.stringify never writes in a value.
        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        try
        {
            var messageStart = 0;
            var dataEnd = 0;
            while (true)
            {
                if (dataEnd == buffer.Length)
                {
                    if (messageStart > 0)
                    {
                        // Move the incomplete message to the start of the buffer
                        buffer.AsSpan(messageStart, dataEnd - messageStart).CopyTo(buffer);
                        dataEnd -= messageStart;
                        messageStart = 0;
                    }
                    else
                    {
                        // The buffer grows exponentially, so a large message is copied a constant number of times on average
                        if (buffer.Length == Array.MaxLength)
                            throw new InvalidOperationException("The message is too large.");

                        var newBuffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(buffer.Length * 2L, Array.MaxLength));
                        buffer.AsSpan(0, dataEnd).CopyTo(newBuffer);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = newBuffer;
                    }
                }

                var bytesRead = await _stream.ReadAsync(buffer.AsMemory(dataEnd)).ConfigureAwait(false);
                if (bytesRead is 0)
                    break;

                // Only the new bytes are searched for line breaks, so a message split into many chunks is read in linear time
                var searchStart = dataEnd;
                dataEnd += bytesRead;
                int index;
                while ((index = buffer.AsSpan(searchStart, dataEnd - searchStart).IndexOf((byte)'\n')) >= 0)
                {
                    var messageEnd = searchStart + index;
                    var message = buffer.AsSpan(messageStart, messageEnd - messageStart);

                    // The first message is the handshake, validated by the host
                    if (!_hello.Task.IsCompleted)
                    {
                        _hello.TrySetResult(message.ToArray());
                    }
                    else
                    {
                        ProcessMessage(message);
                    }

                    messageStart = searchStart = messageEnd + 1;
                }

                if (messageStart == dataEnd)
                {
                    messageStart = dataEnd = 0;

                    // Do not keep the large buffer needed by a large message
                    if (buffer.Length > ReadBufferSize)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            error = ex;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await _host.OnChannelClosedAsync(this, error).ConfigureAwait(false);
        _hello.TrySetException(new IOException("The connection was closed before the handshake."));
    }

    // Responses always start with the identifier of the request, followed by at most one property: {"id":1,"result":...}
    private void ProcessMessage(ReadOnlySpan<byte> message)
    {
        var reader = new Utf8JsonReader(message);
        if (!reader.Read() || reader.TokenType is not JsonTokenType.StartObject ||
            !reader.Read() || reader.TokenType is not JsonTokenType.PropertyName)
        {
            throw new JsonException("The response does not start with the identifier of the request.");
        }

        if (reader.ValueTextEquals("type"u8))
        {
            _host.OnNotice(this, message);
            return;
        }

        if (!reader.ValueTextEquals("id"u8) || !reader.Read())
            throw new JsonException("The response does not start with the identifier of the request.");

        var id = reader.GetInt64();
        if (!reader.Read())
            throw new JsonException("The response is incomplete.");

        // A void call has no result
        if (reader.TokenType is JsonTokenType.EndObject)
        {
            if (TryRemoveRequest(id, out var request))
            {
                request.SetNoResult();
            }

            return;
        }

        if (reader.TokenType is not JsonTokenType.PropertyName)
            throw new JsonException("The response is invalid.");

        // The value is the rest of the message, so it is parsed only once, directly by the reader of the request.
        // Parsing fails when the value is not exactly one JSON value, for instance when another property follows.
        var valueEnd = message.LastIndexOf((byte)'}');
        if (valueEnd < reader.BytesConsumed)
            throw new JsonException("The response is incomplete.");

        var value = message[(int)reader.BytesConsumed..valueEnd];

        // A request is only removed once the response is known to be valid, so it is never left pending when the connection is terminated.
        // Reading the result cannot terminate the connection, as its failure is reported to the caller.
        if (reader.ValueTextEquals("result"u8) || reader.ValueTextEquals("reference"u8))
        {
            if (TryRemoveRequest(id, out var request))
            {
                request.SetResult(value);
            }
            else if (reader.ValueTextEquals("reference"u8))
            {
                // The value kept for the caller is no longer needed
                ReleaseReference(NodeJsHost.ReadReferenceId(value));
            }
        }
        else if (reader.ValueTextEquals("error"u8))
        {
            var exception = CreateJavaScriptException(JsonElement.Parse(value));
            if (TryRemoveRequest(id, out var request))
            {
                request.SetException(exception);
            }
        }
        else
        {
            throw new JsonException("The response contains an unexpected property.");
        }
    }

    // The cause of the error, if any, is the inner exception. The depth of the causes is bounded by the Node.js process.
    private static NodeJsException CreateJavaScriptException(JsonElement error)
    {
        var name = GetStringOrNull(error, "name");
        var message = GetStringOrNull(error, "message");
        var cause = error.TryGetProperty("cause", out var causeElement) && causeElement.ValueKind is JsonValueKind.Object ? CreateJavaScriptException(causeElement) : null;
        return new NodeJsException(name is null ? message ?? "" : $"{name}: {message}", name, GetStringOrNull(error, "stack"), GetStringOrNull(error, "code"), cause);

        static string? GetStringOrNull(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind is JsonValueKind.String ? value.GetString() : null;
        }
    }

    /// <summary>Removes the request answered by a response.</summary>
    /// <returns><see langword="false"/> when the caller stopped waiting (see <see cref="AbandonRequest"/>).</returns>
    private bool TryRemoveRequest(long id, [NotNullWhen(true)] out PendingRequest? request)
    {
        if (_pendingRequests.TryRemove(id, out request))
            return true;

        // The caller stopped waiting, and the call is no longer running
        if (_abandonedRequests.TryRemove(id, out _))
        {
            Interlocked.Decrement(ref _abandonedCalls);
        }

        return false;
    }
}
