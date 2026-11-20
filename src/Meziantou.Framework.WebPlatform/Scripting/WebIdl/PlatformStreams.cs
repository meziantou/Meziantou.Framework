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
}

/// <summary>
/// The .NET side of a <c>ReadableStream</c> of a platform object (e.g. <c>MediaStreamTrackProcessor.readable</c>): the
/// streams are implemented in JavaScript (Streams.js), so the stream of the scripts is created when the object is first
/// converted to a JavaScript value (its high water mark is 0). Its pull and cancel algorithms call the callbacks, and the
/// platform object enqueues the chunks with <see cref="Enqueue"/>, on the event loop of the realm.
/// </summary>
internal sealed class PlatformReadableStream(Action pull, Action<object?> cancel)
{
    private IPlatformStreamController? _controller;
    private bool _closed;
    private Exception? _error;

    /// <summary>Whether the stream of the scripts was created.</summary>
    public bool IsAttached => _controller is not null;

    /// <summary>The pull algorithm: the stream wants a chunk.</summary>
    internal void Pull() => pull();

    /// <summary>The cancel algorithm: the reader canceled the stream.</summary>
    internal void Cancel(object? reason) => cancel(reason);

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
internal sealed class PlatformWritableStream(Action<object?> write, Action close, Action<object?> abort)
{
    private IPlatformStreamController? _controller;
    private bool _closed;

    /// <summary>Whether the stream of the scripts was created.</summary>
    public bool IsAttached => _controller is not null;

    internal void Write(object? chunk) => write(chunk);

    internal void CloseAlgorithm() => close();

    internal void Abort(object? reason) => abort(reason);

    /// <summary>Connects the stream of the scripts (once): the stream is closed if it was before.</summary>
    internal void Attach(IPlatformStreamController controller)
    {
        _controller = controller;
        if (_closed)
        {
            controller.Close();
        }
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
