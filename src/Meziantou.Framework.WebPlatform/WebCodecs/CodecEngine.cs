using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.WebCodecs;

/// <summary>
/// The codec processing model shared by the decoders and the encoders of WebCodecs
/// (https://w3c.github.io/webcodecs/#codec-processing-model-section): the state, the control message queue, the queue size
/// and its <c>dequeue</c> events, the pending flush promises, the reset and close algorithms. The codec work queue is the
/// event loop of the codec: the codec implementation runs in its tasks, one operation at a time (the codec is saturated
/// while an operation is pending), so the operations queued before a reset never deliver their outputs.
/// </summary>
internal sealed class CodecEngine<TImplementation>
    where TImplementation : MediaCodecImplementation
{
    /// <summary>The largest number of pending control messages: a page that queues more closes the codec (the inputs it holds would never be released).</summary>
    internal const int MaxPendingMessages = 1 << 16;

    private readonly EventTarget _owner;
    private readonly WebCodecsErrorCallback _errorCallback;
    private readonly Queue<Func<bool>> _messages = new();
    private readonly List<TaskCompletionSource> _pendingFlushes = [];
    private readonly List<Action> _discards = [];
    private bool _blocked;
    private bool _saturated;
    private bool _dequeueScheduled;
    private int _generation;

    public CodecEngine(EventTarget owner, WebCodecsErrorCallback errorCallback, GlobalScope? global)
    {
        _owner = owner;
        _errorCallback = errorCallback;
        Global = global;
    }

    /// <summary>The global object of the codec: its event loop runs the codec work.</summary>
    public GlobalScope? Global { get; }

    /// <summary>The codecs of the global object, or null.</summary>
    public MediaCodecBackend? Backend => Global?.Options.MediaCodecs;

    public CodecState State { get; private set; } = CodecState.Unconfigured;

    /// <summary>The number of pending decode or encode requests ([[decodeQueueSize]], [[encodeQueueSize]]).</summary>
    public uint QueueSize { get; private set; }

    /// <summary>The codec implementation of the active configuration ([[codec implementation]]), or null.</summary>
    public TImplementation? Implementation { get; private set; }

    /// <summary>The generation of the outputs: it changes when the codec is reset, so the outputs of the previous configurations are discarded.</summary>
    public int Generation => _generation;

    /// <summary>Throws an <c>InvalidStateError</c> when the codec is closed.</summary>
    public void ThrowIfClosed()
    {
        if (State == CodecState.Closed)
            throw new DomException("The codec is closed", "InvalidStateError");
    }

    /// <summary>Throws an <c>InvalidStateError</c> when the codec is not configured.</summary>
    public void ThrowIfNotConfigured()
    {
        if (State != CodecState.Configured)
            throw new DomException("The codec is not configured", "InvalidStateError");
    }

    /// <summary>
    /// The control message of <c>configure()</c>: the queue is blocked until the codec implementation of the configuration
    /// is created (the previous one is flushed into <paramref name="outputPending"/> and released), or the codec is closed
    /// with a <c>NotSupportedError</c> when the backend does not support it.
    /// </summary>
    /// <param name="create">Creates the codec implementation, or returns null when the configuration is not supported.</param>
    /// <param name="outputPending">Delivers the pending outputs of the previous codec implementation, or null.</param>
    public void Configure(Func<TImplementation?> create, Action<TImplementation>? outputPending)
    {
        State = CodecState.Configured;
        EnqueueMessage(() =>
        {
            _blocked = true;
            var generation = _generation;
            QueueTask(() =>
            {
                if (generation != _generation)
                    return;

                if (Implementation is { } previous)
                {
                    if (!RunCodecOperation(() => outputPending?.Invoke(previous)) || generation != _generation)
                        return;

                    Implementation = null;
                    previous.Dispose();
                }

                TImplementation? implementation = null;
                if (!RunCodecOperation(() => implementation = create()) || generation != _generation)
                    return;

                if (implementation is null)
                {
                    Close(new DomException("The configuration is not supported", "NotSupportedError"));
                    return;
                }

                Implementation = implementation;
                _blocked = false;
                ProcessQueue();
            });
            return true;
        });
    }

    /// <summary>
    /// Queues a decode or an encode request: the queue size grows until the codec runs the operation in a task of its
    /// event loop (one operation at a time); the operation delivers the outputs it produces.
    /// </summary>
    public void EnqueueWork(Action<TImplementation> operation, Action? discard = null)
    {
        QueueSize++;
        EnqueueMessage(() =>
        {
            if (_saturated)
                return false;

            _saturated = true;
            QueueSize--;
            ScheduleDequeueEvent();
            var generation = _generation;
            QueueTask(() =>
            {
                if (generation != _generation || Implementation is not { } implementation)
                {
                    discard?.Invoke();
                    return;
                }

                RunCodecOperation(() => operation(implementation));
                if (generation != _generation)
                    return;

                _saturated = false;
                ProcessQueue();
            });
            return true;
        }, discard);
    }

    /// <summary>The flush steps (https://w3c.github.io/webcodecs/#dom-audiodecoder-flush): the operation delivers the pending outputs, then the promise resolves.</summary>
    public Task Flush(Action<TImplementation> operation)
    {
        if (State != CodecState.Configured)
            return Task.FromException(new DomException("The codec is not configured", "InvalidStateError"));

        var completion = new TaskCompletionSource();
        _pendingFlushes.Add(completion);
        EnqueueMessage(() =>
        {
            if (_saturated)
                return false;

            _saturated = true;
            var generation = _generation;
            QueueTask(() =>
            {
                if (generation != _generation || !_pendingFlushes.Contains(completion))
                    return;

                if (Implementation is { } implementation && !RunCodecOperation(() => operation(implementation)))
                    return;

                if (generation != _generation)
                    return;

                _saturated = false;
                _pendingFlushes.Remove(completion);
                completion.TrySetResult();
                ProcessQueue();
            });
            return true;
        });
        return completion.Task;
    }

    /// <summary>The reset algorithm (https://w3c.github.io/webcodecs/#reset-audiodecoder): the pending requests are discarded and the pending flushes rejected.</summary>
    public void Reset(DomException exception)
    {
        ThrowIfClosed();
        ResetCore(exception);
    }

    /// <summary>The close algorithm (https://w3c.github.io/webcodecs/#close-audiodecoder): the codec is reset, then released; an error other than an <c>AbortError</c> is given to the error callback.</summary>
    public void Close(DomException exception)
    {
        if (State == CodecState.Closed)
            return;

        ResetCore(exception);
        State = CodecState.Closed;
        if (exception.Name != "AbortError")
        {
            try
            {
                _errorCallback(exception);
            }
#pragma warning disable CA1031 // The exceptions of the callbacks of the scripts are reported
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ScriptErrors.Report(ex, Global);
            }
        }
    }

    /// <summary>Invokes an output callback: its exceptions are reported.</summary>
    public void InvokeCallback(Action callback)
    {
        try
        {
            callback();
        }
#pragma warning disable CA1031 // The exceptions of the callbacks of the scripts are reported
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ScriptErrors.Report(ex, Global);
        }
    }

    private void ResetCore(DomException exception)
    {
        State = CodecState.Unconfigured;
        _generation++;
        while (_messages.Count > 0)
        {
            _messages.Dequeue();
        }

        foreach (var discard in _discards)
        {
            discard();
        }

        _discards.Clear();
        _blocked = false;
        _saturated = false;
        if (Implementation is { } implementation)
        {
            Implementation = null;
            try
            {
                implementation.Reset();
            }
            finally
            {
                implementation.Dispose();
            }
        }

        if (QueueSize > 0)
        {
            QueueSize = 0;
            ScheduleDequeueEvent();
        }

        var flushes = _pendingFlushes.ToArray();
        _pendingFlushes.Clear();
        foreach (var flush in flushes)
        {
            flush.TrySetException(exception);
        }
    }

    private void EnqueueMessage(Func<bool> message, Action? discard = null)
    {
        if (_messages.Count >= MaxPendingMessages)
        {
            discard?.Invoke();
            Close(new DomException("The codec has too many pending requests", "QuotaExceededError"));
            return;
        }

        if (discard is not null)
        {
            _discards.Add(discard);
            var inner = message;
            message = () =>
            {
                var processed = inner();
                if (processed)
                {
                    _discards.Remove(discard);
                }

                return processed;
            };
        }

        _messages.Enqueue(message);
        ProcessQueue();
    }

    /// <summary>https://w3c.github.io/webcodecs/#process-the-control-message-queue</summary>
    private void ProcessQueue()
    {
        while (!_blocked && _messages.Count > 0)
        {
            var generation = _generation;
            if (!_messages.Peek()())
                break;

            // A message that closed the codec cleared the queue
            if (generation == _generation)
            {
                _messages.Dequeue();
            }
        }
    }

    /// <summary>https://w3c.github.io/webcodecs/#schedule-dequeue-event</summary>
    private void ScheduleDequeueEvent()
    {
        if (_dequeueScheduled)
            return;

        _dequeueScheduled = true;
        QueueTask(() =>
        {
            _dequeueScheduled = false;
            _owner.DispatchEvent(new Event("dequeue"));
        });
    }

    /// <summary>Runs an operation of the codec implementation: an exception closes the codec with an <c>EncodingError</c> (or the <see cref="DomException"/> it is).</summary>
    private bool RunCodecOperation(Action operation)
    {
        try
        {
            operation();
            return true;
        }
        catch (DomException ex)
        {
            Close(ex);
            return false;
        }
#pragma warning disable CA1031 // A failure of the codec implementation closes the codec
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Close(new DomException(ex.Message, "EncodingError"));
            return false;
        }
    }

    private void QueueTask(Action task)
    {
        if (Global is { } global)
        {
            global.QueueGlobalTask(task);
        }
        else
        {
            task();
        }
    }
}
