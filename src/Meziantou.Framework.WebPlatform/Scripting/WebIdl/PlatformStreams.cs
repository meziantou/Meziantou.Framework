namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The operations with which a platform stream (<see cref="PlatformReadableStream"/>, <see cref="PlatformWritableStream"/>)
/// controls the stream of the scripts (Streams.js) created for it in a realm. They run on the thread of the realm.
/// </summary>
internal interface IPlatformStreamController
{
    /// <summary>Enqueues a chunk in a ReadableStream (https://streams.spec.whatwg.org/#readablestreamdefaultcontroller-enqueue).</summary>
    void Enqueue(object chunk);

    /// <summary>Closes the stream: a ReadableStream once its queued chunks are read, a WritableStream once its queued chunks are written.</summary>
    void Close();

    /// <summary>Errors the stream.</summary>
    void Error(Exception error);

    /// <summary>
    /// Cancels a ReadableStream (ReadableStream.prototype.cancel()) or aborts a WritableStream (WritableStream.prototype.abort()):
    /// the task fails with a <see cref="Dom.TypeErrorException"/> when the stream is locked.
    /// </summary>
    Task CancelOrAbort(object? reason);
}

/// <summary>
/// The .NET side of a <c>ReadableStream</c> of a platform object (e.g. <c>MediaStreamTrackProcessor.readable</c>): the
/// streams are implemented in JavaScript (Streams.js), so the stream of the scripts is created when the object is first
/// converted to a JavaScript value (its high water mark is 0). Its pull and cancel algorithms call the callbacks, and the
/// platform object enqueues the chunks with <see cref="Enqueue"/>, on the event loop of the realm.
/// </summary>
internal sealed class PlatformReadableStream
{
    private readonly Func<Task> _pull;
    private readonly Func<object?, Task> _cancel;
    private IPlatformStreamController? _controller;
    private bool _closed;
    private Exception? _error;

    public PlatformReadableStream(Action pull, Action<object?> cancel)
    {
        _pull = () =>
        {
            pull();
            return Task.CompletedTask;
        };
        _cancel = reason =>
        {
            cancel(reason);
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Creates a stream whose algorithms complete asynchronously: the pull algorithm completes once the chunk is enqueued (e.g.
    /// the bytes read from a serial port). A readable byte stream (<paramref name="isBytes"/>) has byte reading support: its
    /// chunks are bytes, which fill the views of the BYOB readers.
    /// </summary>
    public PlatformReadableStream(Func<Task> pullAsync, Func<object?, Task> cancelAsync, bool isBytes)
    {
        _pull = pullAsync;
        _cancel = cancelAsync;
        IsBytes = isBytes;
    }

    /// <summary>Whether the stream of the scripts was created.</summary>
    public bool IsAttached => _controller is not null;

    /// <summary>Whether the stream is a readable byte stream.</summary>
    public bool IsBytes { get; }

    /// <summary>
    /// Cancels the stream as ReadableStream.prototype.cancel() does: the cancel algorithm runs, and the task fails with a
    /// <see cref="Dom.TypeErrorException"/> when the stream of the scripts is locked. Without stream of the scripts, the cancel
    /// algorithm runs.
    /// </summary>
    public Task CancelStream(object? reason)
    {
        if (_controller is { } controller)
            return controller.CancelOrAbort(reason);

        if (_closed || _error is not null)
            return Task.CompletedTask;

        _closed = true;
        return _cancel(reason);
    }

    /// <summary>The pull algorithm: the stream wants a chunk.</summary>
    internal Task Pull() => _pull();

    /// <summary>The cancel algorithm: the reader canceled the stream.</summary>
    internal Task Cancel(object? reason) => _cancel(reason);

    /// <summary>Connects the stream of the scripts (once): the stream is closed or errored if it was before.</summary>
    internal void Attach(IPlatformStreamController controller)
    {
        _controller = controller;
        if (_error is not null)
        {
            controller.Error(_error);
        }
        else if (_closed)
        {
            controller.Close();
        }
    }

    /// <summary>Enqueues a chunk: returns false when the stream of the scripts does not exist.</summary>
    public bool Enqueue(object chunk)
    {
        if (_controller is null || _closed || _error is not null)
            return false;

        _controller.Enqueue(chunk);
        return true;
    }

    /// <summary>Closes the stream.</summary>
    public void Close()
    {
        if (_closed || _error is not null)
            return;

        _closed = true;
        _controller?.Close();
    }

    /// <summary>Errors the stream.</summary>
    public void Error(Exception error)
    {
        if (_closed || _error is not null)
            return;

        _error = error;
        _controller?.Error(error);
    }
}

/// <summary>
/// The .NET side of a <c>WritableStream</c> of a platform object (e.g. <c>VideoTrackGenerator.writable</c>): the stream of
/// the scripts (Streams.js) is created when the object is first converted to a JavaScript value (its high water mark is 1).
/// Its write algorithm calls <c>write</c> with the chunk (the platform object, or the JavaScript value when it is not a
/// platform object): a <see cref="Dom.TypeErrorException"/> or a <see cref="Dom.DomException"/> rejects the write.
/// </summary>
internal sealed class PlatformWritableStream
{
    private readonly Func<object?, Task> _write;
    private readonly Func<Task> _close;
    private readonly Func<object?, Task> _abort;
    private IPlatformStreamController? _controller;
    private bool _closed;

    public PlatformWritableStream(Action<object?> write, Action close, Action<object?> abort)
    {
        _write = chunk =>
        {
            write(chunk);
            return Task.CompletedTask;
        };
        _close = () =>
        {
            close();
            return Task.CompletedTask;
        };
        _abort = reason =>
        {
            abort(reason);
            return Task.CompletedTask;
        };
    }

    /// <summary>Creates a stream whose algorithms complete asynchronously (e.g. a write is fulfilled once the bytes are sent).</summary>
    public PlatformWritableStream(Func<object?, Task> writeAsync, Func<Task> closeAsync, Func<object?, Task> abortAsync)
    {
        _write = writeAsync;
        _close = closeAsync;
        _abort = abortAsync;
    }

    /// <summary>Whether the stream of the scripts was created.</summary>
    public bool IsAttached => _controller is not null;

    /// <summary>The write algorithm: the task of the write (completed when the write is synchronous).</summary>
    internal Task Write(object? chunk) => _write(chunk);

    internal Task CloseAlgorithm() => _close();

    internal Task Abort(object? reason) => _abort(reason);

    /// <summary>
    /// Aborts the stream as WritableStream.prototype.abort() does: the abort algorithm runs, and the task fails with a
    /// <see cref="Dom.TypeErrorException"/> when the stream of the scripts is locked. Without stream of the scripts, the abort
    /// algorithm runs.
    /// </summary>
    public Task AbortStream(object? reason)
    {
        if (_controller is { } controller)
            return controller.CancelOrAbort(reason);

        if (_closed)
            return Task.CompletedTask;

        _closed = true;
        return _abort(reason);
    }

    /// <summary>Connects the stream of the scripts (once): the stream is closed if it was before.</summary>
    internal void Attach(IPlatformStreamController controller)
    {
        _controller = controller;
        if (_closed)
        {
            controller.Close();
        }
    }

    /// <summary>Errors the stream (e.g. the serial port was disconnected).</summary>
    public void Error(Exception error)
    {
        if (_closed)
            return;

        _closed = true;
        _controller?.Error(error);
    }

    /// <summary>Closes the stream once its queued chunks are written: the next writes are rejected with a <c>TypeError</c>.</summary>
    public void Close()
    {
        if (_closed)
            return;

        _closed = true;
        _controller?.Close();
    }
}
