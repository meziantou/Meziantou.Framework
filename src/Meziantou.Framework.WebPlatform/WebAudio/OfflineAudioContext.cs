using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.WebAudio;

/// <summary>The options of an <see cref="OfflineAudioContext"/> (https://webaudio.github.io/web-audio-api/#OfflineAudioContextOptions).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#OfflineAudioContextOptions"/>
[IdlInterface("OfflineAudioContextOptions", "https://webaudio.github.io/web-audio-api/#OfflineAudioContextOptions")]
public sealed class OfflineAudioContextOptions
{
    /// <summary>Gets the number of channels of the rendered buffers.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontextoptions-numberofchannels"/>
    [IdlMember("numberOfChannels", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontextoptions-numberofchannels")]
    public uint NumberOfChannels { get; init; } = 1;

    /// <summary>Gets the length of the rendering in sample frames, or null for a rendering of undefined length (rendered by chunks).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontextoptions-length"/>
    [IdlMember("length", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontextoptions-length")]
    public uint? Length { get; init; }

    /// <summary>Gets the sample rate in Hz.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontextoptions-samplerate"/>
    [IdlMember("sampleRate", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontextoptions-samplerate")]
    public required float SampleRate { get; init; }

    /// <summary>Gets the render quantum size: an <see cref="AudioContextRenderSizeCategory"/>, or a number of sample frames (a <see cref="uint"/>).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontextoptions-rendersizehint"/>
    [IdlMember("renderSizeHint", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontextoptions-rendersizehint")]
    public AudioContextRenderSizeHint RenderSizeHint { get; init; } = AudioContextRenderSizeCategory.Default;
}

/// <summary>The completion event of an <see cref="OfflineAudioContext"/> (https://webaudio.github.io/web-audio-api/#OfflineAudioCompletionEventInit).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#OfflineAudioCompletionEventInit"/>
[IdlInterface("OfflineAudioCompletionEventInit", "https://webaudio.github.io/web-audio-api/#OfflineAudioCompletionEventInit")]
public sealed class OfflineAudioCompletionEventInit : EventInit
{
    /// <summary>Gets the rendered buffer.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocompletioneventinit-renderedbuffer"/>
    [IdlMember("renderedBuffer", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocompletioneventinit-renderedbuffer")]
    public required AudioBuffer RenderedBuffer { get; init; }
}

/// <summary>The <c>complete</c> event of an <see cref="OfflineAudioContext"/> (https://webaudio.github.io/web-audio-api/#OfflineAudioCompletionEvent).</summary>
/// <seealso href="https://webaudio.github.io/web-audio-api/#OfflineAudioCompletionEvent"/>
[IdlInterface("OfflineAudioCompletionEvent", "https://webaudio.github.io/web-audio-api/#OfflineAudioCompletionEvent")]
public sealed class OfflineAudioCompletionEvent : Event
{
    /// <summary>Creates a completion event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocompletionevent-offlineaudiocompletionevent"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocompletionevent-offlineaudiocompletionevent")]
    public OfflineAudioCompletionEvent(string type, OfflineAudioCompletionEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        RenderedBuffer = eventInitDict.RenderedBuffer;
    }

    /// <summary>Gets the rendered buffer.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocompletionevent-renderedbuffer"/>
    [IdlMember("renderedBuffer", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocompletionevent-renderedbuffer")]
    public AudioBuffer RenderedBuffer { get; }
}

/// <summary>
/// An audio context that renders its graph to <see cref="AudioBuffer"/> objects as fast as possible
/// (https://webaudio.github.io/web-audio-api/#OfflineAudioContext), deterministically.
/// </summary>
/// <remarks>
/// The rendering runs in tasks of the event loop of the window of the context: <c>startRendering()</c> returns before the
/// rendering starts, and the promise is resolved by a task once the buffer is rendered. A context created by .NET code
/// without window renders synchronously.
/// </remarks>
/// <seealso href="https://webaudio.github.io/web-audio-api/#OfflineAudioContext"/>
[IdlInterface("OfflineAudioContext", "https://webaudio.github.io/web-audio-api/#OfflineAudioContext")]
public sealed class OfflineAudioContext : BaseAudioContext
{
    // The number of frames rendered by a task
    private const int RenderSliceFrames = 16384;

    private readonly Queue<PendingRendering> _pendingRenderings = new();
    private readonly SortedDictionary<long, TaskCompletionSource> _suspends = [];
    private bool _renderingStarted;
    private bool _renderingScheduled;
    private bool _suspendedAtFrame;
    private bool _controlClosed;
    private long _committedFrames;
    private long _renderedFrames;

    /// <summary>Creates an offline context.</summary>
    /// <exception cref="DomException">An option is not supported (a <c>NotSupportedError</c>), or the document is not fully active (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-offlineaudiocontext"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-offlineaudiocontext")]
    public OfflineAudioContext(OfflineAudioContextOptions contextOptions)
        : this(Validate(contextOptions), GetRenderQuantumSize(contextOptions))
    {
    }

    /// <summary>Creates an offline context that renders a buffer of a number of channels, a length and a sample rate.</summary>
    /// <exception cref="DomException">An argument is not supported (a <c>NotSupportedError</c>), or the document is not fully active (an <c>InvalidStateError</c>).</exception>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-offlineaudiocontext-numberofchannels-length-samplerate"/>
    [IdlMember("constructor", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-offlineaudiocontext-numberofchannels-length-samplerate")]
    public OfflineAudioContext(uint numberOfChannels, uint length, float sampleRate)
        : this(new OfflineAudioContextOptions { NumberOfChannels = numberOfChannels, Length = length, SampleRate = sampleRate })
    {
    }

    private OfflineAudioContext(OfflineAudioContextOptions options, int renderQuantumSize)
        : base(options.SampleRate, renderQuantumSize, options.NumberOfChannels, options.NumberOfChannels, isOffline: true)
    {
        NumberOfChannels = options.NumberOfChannels;
        Length = options.Length;
    }

    private static OfflineAudioContextOptions Validate(OfflineAudioContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (GlobalScope.Current is Window { Document.IsFullyActive: false })
            throw new DomException("The document is not fully active", "InvalidStateError");

        AudioBuffer.Validate(options.NumberOfChannels, options.Length ?? 1, options.SampleRate);
        return options;
    }

    /// <summary>The render quantum size of a hint (https://webaudio.github.io/web-audio-api/#supported-render-quantum-sizes).</summary>
    internal static int GetRenderQuantumSize(AudioContextRenderSizeHint hint, float sampleRate)
    {
        if (hint.Size is not { } size)
            return DefaultRenderQuantumSize;

        if (size == 0 || size > Math.Floor(6 * (double)sampleRate))
            throw new DomException($"The render quantum size {size} is not supported", "NotSupportedError");

        return (int)size;
    }

    private static int GetRenderQuantumSize(OfflineAudioContextOptions options) => GetRenderQuantumSize(options.RenderSizeHint, options.SampleRate);

    /// <summary>The number of channels of the rendered buffers.</summary>
    internal uint NumberOfChannels { get; }

    /// <summary>Gets the length of the rendering in sample frames, or null for a rendering of undefined length.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-length"/>
    [IdlMember("length", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-length")]
    public uint? Length { get; }

    /// <summary>Gets or sets the event handler of the <c>complete</c> event.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-oncomplete"/>
    [IdlMember("oncomplete", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-oncomplete")]
    public Action<Event>? OnComplete
    {
        get => GetEventHandler("complete");
        set => SetEventHandler("complete", value);
    }

    /// <summary>
    /// Starts the rendering of a buffer: the remaining frames of the context, or a chunk of frames (rounded up to a multiple of
    /// the render quantum size).
    /// </summary>
    /// <param name="chunkSize">The number of frames to render, or null.</param>
    /// <returns>A task completed with the rendered buffer.</returns>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-startrendering"/>
    [IdlMember("startRendering", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-startrendering")]
    public Task<AudioBuffer> StartRendering(uint? chunkSize = null)
    {
        if (!IsDocumentFullyActive)
            return Task.FromException<AudioBuffer>(new DomException("The document is not fully active", "InvalidStateError"));

        if (_controlClosed || State == AudioContextState.Closed)
            return Task.FromException<AudioBuffer>(new DomException("The context is closed", "InvalidStateError"));

        if (Length is { } length && _committedFrames >= length)
            return Task.FromException<AudioBuffer>(new DomException("All the frames of the context are rendered", "InvalidStateError"));

        // The length of the buffer
        var quantum = RenderQuantumSizeInternal;
        long bufferLength;
        if (Length is not { } totalLength)
        {
            bufferLength = chunkSize is { } size ? size : quantum;
        }
        else if (chunkSize is { } chunk && _committedFrames + chunk < totalLength)
        {
            bufferLength = chunk;
        }
        else
        {
            bufferLength = totalLength - _committedFrames;
        }

        var renderFrames = (bufferLength + quantum - 1) / quantum * quantum;
        if (Length is null || chunkSize is not null)
        {
            // A chunk is rounded up to a multiple of the render quantum size, except the last chunk of a finite rendering
            bufferLength = Length is { } finiteLength ? Math.Min(renderFrames, finiteLength - _committedFrames) : renderFrames;
        }

        AudioBuffer buffer;
        try
        {
            if (bufferLength > uint.MaxValue)
                throw new DomException($"The buffer of {bufferLength} frames is too large", "NotSupportedError");

            buffer = new AudioBuffer(NumberOfChannels, (uint)bufferLength, SampleRate);
        }
        catch (DomException ex)
        {
            return Task.FromException<AudioBuffer>(ex);
        }

        _renderingStarted = true;
        _committedFrames += buffer.Length;

        // The context runs as soon as the rendering is requested (the statechange event is fired by a task)
        if (State == AudioContextState.Suspended && !_suspendedAtFrame)
        {
            SetState(AudioContextState.Running);
        }

        var completion = new TaskCompletionSource<AudioBuffer>();
        _pendingRenderings.Enqueue(new PendingRendering(buffer, renderFrames, completion));
        ScheduleRendering();
        return completion.Task;
    }

    /// <summary>Resumes the rendering suspended by <see cref="Suspend"/>.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-resume"/>
    [IdlMember("resume", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-resume")]
    public Task Resume()
    {
        if (!IsDocumentFullyActive)
            return Task.FromException(new DomException("The document is not fully active", "InvalidStateError"));

        if (_controlClosed || State == AudioContextState.Closed)
            return Task.FromException(new DomException("The context is closed", "InvalidStateError"));

        if (!_renderingStarted)
            return Task.FromException(new DomException("The rendering has not started", "InvalidStateError"));

        var completion = new TaskCompletionSource();
        _suspendedAtFrame = false;
        QueueMediaElementTask(() =>
        {
            completion.TrySetResult();
            if (State != AudioContextState.Running && State != AudioContextState.Closed)
            {
                SetState(AudioContextState.Running);
            }
        });
        ScheduleRendering();
        return completion.Task;
    }

    /// <summary>Schedules a suspension of the rendering at a time (rounded up to a render quantum boundary).</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-suspend"/>
    [IdlMember("suspend", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-suspend")]
    public Task Suspend(double suspendTime)
    {
        if (!IsDocumentFullyActive)
            return Task.FromException(new DomException("The document is not fully active", "InvalidStateError"));

        if (_controlClosed || State == AudioContextState.Closed)
            return Task.FromException(new DomException("The context is closed", "InvalidStateError"));

        if (suspendTime < 0)
            return Task.FromException(new DomException($"The suspend time {suspendTime} cannot be negative", "InvalidStateError"));

        var quantum = RenderQuantumSizeInternal;
        var frame = (long)Math.Ceiling(suspendTime * SampleRate / quantum) * quantum;
        if (frame < CurrentFrame || (frame == CurrentFrame && _renderingStarted))
            return Task.FromException(new DomException($"The suspend time {suspendTime} is not after the current time", "InvalidStateError"));

        if (Length is { } length && frame >= length)
            return Task.FromException(new DomException($"The suspend time {suspendTime} is after the end of the rendering", "InvalidStateError"));

        if (_suspends.ContainsKey(frame))
            return Task.FromException(new DomException($"A suspension is already scheduled at the time {suspendTime}", "InvalidStateError"));

        var completion = new TaskCompletionSource();
        _suspends[frame] = completion;
        return completion.Task;
    }

    /// <summary>Closes the context: the rendering stops.</summary>
    /// <seealso href="https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-close"/>
    [IdlMember("close", "https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-close")]
    public Task Close()
    {
        if (!IsDocumentFullyActive)
            return Task.FromException(new DomException("The document is not fully active", "InvalidStateError"));

        if (_controlClosed || State == AudioContextState.Closed)
            return Task.FromException(new DomException("The context is closed", "InvalidStateError"));

        _controlClosed = true;
        var completion = new TaskCompletionSource();
        QueueMediaElementTask(() =>
        {
            RejectPendingRenderings();
            foreach (var suspend in _suspends.Values)
            {
                suspend.TrySetException(new DomException("The context was closed before the suspension", "InvalidStateError"));
            }

            _suspends.Clear();
            completion.TrySetResult();
            if (State != AudioContextState.Closed)
            {
                SetState(AudioContextState.Closed);
            }
        });
        return completion.Task;
    }

    private void RejectPendingRenderings()
    {
        while (_pendingRenderings.TryDequeue(out var pending))
        {
            pending.Completion.TrySetException(new DomException("The context was closed during the rendering", "AbortError"));
        }
    }

    private void ScheduleRendering()
    {
        if (_renderingScheduled)
            return;

        _renderingScheduled = true;
        if (Window is { } window)
        {
            window.QueueGlobalTask(RenderPending);
        }
        else
        {
            RenderPending();
        }
    }

    /// <summary>
    /// Renders the pending buffers, until a suspension or the end of the pending renderings. A task renders a slice of
    /// frames, so that the other tasks (e.g. a call of <see cref="Close"/>) can run during a long rendering.
    /// </summary>
    private void RenderPending()
    {
        _renderingScheduled = false;
        var remainingFrames = RenderSliceFrames;
        while (!_controlClosed && !_suspendedAtFrame && IsDocumentFullyActive && _pendingRenderings.TryPeek(out var pending))
        {
            if (State == AudioContextState.Suspended)
            {
                SetState(AudioContextState.Running);
            }

            while (pending.RenderedFrames < pending.RenderFrames)
            {
                if (remainingFrames <= 0 && Window is not null)
                {
                    ScheduleRendering();
                    return;
                }

                remainingFrames -= RenderQuantumSizeInternal;
                // https://webaudio.github.io/web-audio-api/#dom-offlineaudiocontext-suspend: the rendering is suspended at the
                // start of the render quantum of a suspension
                if (_suspends.Remove(CurrentFrame, out var suspend))
                {
                    _suspendedAtFrame = true;
                    QueueMediaElementTask(() =>
                    {
                        if (State != AudioContextState.Closed)
                        {
                            SetState(AudioContextState.Suspended);
                        }

                        suspend.TrySetResult();
                    });
                    return;
                }

                var output = RenderQuantum();
                pending.Write(output, RenderQuantumSizeInternal);
            }

            _pendingRenderings.Dequeue();
            _renderedFrames += pending.Buffer.Length;
            var buffer = pending.Buffer;
            var finished = Length is { } length && _renderedFrames >= length;
            QueueMediaElementTask(() =>
            {
                if (finished && State != AudioContextState.Closed)
                {
                    SetState(AudioContextState.Closed);
                }

                pending.Completion.TrySetResult(buffer);
                if (Length is not null)
                {
                    QueueMediaElementTask(() => FireEvent(new OfflineAudioCompletionEvent("complete", new OfflineAudioCompletionEventInit { RenderedBuffer = buffer })));
                }
            });
        }
    }

    /// <summary>A buffer being rendered by <see cref="StartRendering"/>.</summary>
    private sealed class PendingRendering(AudioBuffer buffer, long renderFrames, TaskCompletionSource<AudioBuffer> completion)
    {
        public AudioBuffer Buffer { get; } = buffer;

        public long RenderFrames { get; } = renderFrames;

        public TaskCompletionSource<AudioBuffer> Completion { get; } = completion;

        public long RenderedFrames { get; private set; }

        /// <summary>Copies a render quantum of the destination to the buffer (the frames after its length are dropped).</summary>
        public void Write(Rendering.AudioBus output, int length)
        {
            var offset = RenderedFrames;
            var count = (int)Math.Min(length, Buffer.Length - offset);
            if (count > 0 && !output.IsSilent)
            {
                for (var channel = 0; channel < Buffer.NumberOfChannels; channel++)
                {
                    var source = output.GetChannelSpan(Math.Min(channel, output.NumberOfChannels - 1));
                    if (channel >= output.NumberOfChannels)
                        continue;

                    source[..count].CopyTo(Buffer.GetWritableChannel(channel)[(int)offset..]);
                }
            }

            RenderedFrames += length;
        }
    }
}
