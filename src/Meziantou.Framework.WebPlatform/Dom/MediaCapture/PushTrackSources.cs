using Meziantou.Framework.WebPlatform.Media;
using Meziantou.Framework.WebPlatform.WebCodecs;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A source that pushes its media (<see cref="MediaStreamTrackSource.PushesMedia"/>): each frame is delivered to the sinks
/// of the tracks, and the last one is the current frame of the sinks that sample the track (e.g. a video element). The
/// audio samples are delivered to the sinks, and kept in a buffer of 5 seconds read by the sinks that sample the track.
/// </summary>
internal abstract class PushTrackSource : MediaStreamTrackSource
{
    private const int AudioBufferSeconds = 5;

    private VideoFrame? _frame;
    private MediaVideoFrame? _mediaFrame;
    private float[]? _audio;
    private int _audioChannels = 1;
    private int _audioSampleRate = 48000;
    private long _audioWritten;

    protected PushTrackSource(MediaTrackKinds kind, string label)
        : base(kind, label)
    {
    }

    public override bool PushesMedia => true;

    public override Dictionary<string, object> GetInitialSettings() => new(StringComparer.Ordinal);

    /// <summary>The size of the frames is the size of the last frame.</summary>
    public override void UpdateSettings(Dictionary<string, object> settings)
    {
        if (_frame is { DisplayWidth: > 0, DisplayHeight: > 0 } frame)
        {
            settings["width"] = (double)frame.DisplayWidth;
            settings["height"] = (double)frame.DisplayHeight;
            settings["aspectRatio"] = (double)frame.DisplayWidth / frame.DisplayHeight;
        }
    }

    public override MediaVideoFrame? GetVideoFrame()
    {
        if (_mediaFrame is not null || _frame is null || IsStopped)
            return _mediaFrame;

        if (_frame.GetRenderedPixels() is { Width: > 0, Height: > 0 } pixels)
        {
            _mediaFrame = new MediaVideoFrame(pixels.Width, pixels.Height, pixels.Rgba);
        }

        return _mediaFrame;
    }

    /// <summary>Presents a frame (the source takes it): the sinks receive it, and it becomes the current frame.</summary>
    protected void PresentVideoFrame(VideoFrame frame)
    {
        if (IsStopped)
        {
            frame.Close();
            return;
        }

        var sizeChanged = _frame is null || _frame.DisplayWidth != frame.DisplayWidth || _frame.DisplayHeight != frame.DisplayHeight;
        _frame?.Close();
        _frame = frame;
        _mediaFrame = null;
        DeliverVideoFrame(frame);
        if (sizeChanged)
        {
            NotifyTracksChanged();
        }
    }

    public override int AudioChannelCount => _audioChannels;

    public override int AudioSampleRate => _audioSampleRate;

    /// <summary>The position of the audio is the number of frames received.</summary>
    public override long AudioPosition => _audioWritten;

    /// <summary>Presents audio samples (the source takes them): the sinks receive them, and they are appended to the buffer.</summary>
    protected void PresentAudioData(AudioData data)
    {
        if (IsStopped || data.NumberOfFrames == 0)
        {
            data.Close();
            return;
        }

        var channels = (int)Math.Clamp(data.NumberOfChannels, 1u, 32u);
        var sampleRate = (int)Math.Clamp(data.SampleRate, 3000f, 768000f);
        if (_audio is null || channels != _audioChannels || sampleRate != _audioSampleRate)
        {
            // The first samples (or samples of another format) set the format of the buffer
            _audioChannels = channels;
            _audioSampleRate = sampleRate;
            _audio = new float[(long)sampleRate * AudioBufferSeconds * channels];
        }

        var samples = data.GetInterleavedFloats();
        var capacity = _audio.Length / channels;
        var frames = samples.Length / channels;
        var first = Math.Max(0, frames - capacity);
        for (var frame = first; frame < frames; frame++)
        {
            var index = (int)((_audioWritten + frame) % capacity);
            samples.AsSpan(frame * channels, channels).CopyTo(_audio.AsSpan(index * channels, channels));
        }

        _audioWritten += frames;
        DeliverAudioData(data);
        data.Close();
    }

    public override void ReadAudio(long position, Span<float> destination)
    {
        destination.Clear();
        if (_audio is not { } audio || IsStopped)
            return;

        var channels = _audioChannels;
        var capacity = audio.Length / channels;
        var frames = destination.Length / channels;
        for (var frame = 0; frame < frames; frame++)
        {
            var current = position + frame;
            if (current < 0 || current >= _audioWritten || current < _audioWritten - capacity)
                continue;

            audio.AsSpan((int)(current % capacity) * channels, channels).CopyTo(destination.Slice(frame * channels, channels));
        }
    }

    protected override void OnStopped()
    {
        _frame?.Close();
        _frame = null;
        _mediaFrame = null;
        _audio = null;
    }
}

/// <summary>
/// The source of the tracks of a <see cref="VideoTrackGenerator"/> or a <see cref="MediaStreamTrackGenerator"/>
/// (https://w3c.github.io/mediacapture-transform/#video-track-generator): the frames (or the audio samples) written to
/// the generator.
/// </summary>
internal sealed class GeneratorTrackSource(MediaTrackKinds kind) : PushTrackSource(kind, "")
{
    /// <summary>Gets or sets the callback invoked when the source stops: its last track ended (the generator closes its writable stream).</summary>
    public Action? Stopped { get; set; }

    /// <summary>Delivers a copy of a frame to the tracks.</summary>
    public void Write(VideoFrame frame) => PresentVideoFrame(frame.Clone());

    /// <summary>Delivers a copy of audio samples to the tracks.</summary>
    public void Write(AudioData data) => PresentAudioData(data.Clone());

    /// <summary>Sets the muted state of the tracks (they fire <c>mute</c> or <c>unmute</c>).</summary>
    public void SetMutedState(bool muted) => SetMuted(muted);

    /// <summary>Ends the tracks of the generator (they fire <c>ended</c>).</summary>
    public void End() => EndAllTracks();

    protected override void OnStopped()
    {
        base.OnStopped();
        Stopped?.Invoke();
    }
}

/// <summary>
/// The source of a track received by another thread than the one of its source (a track transferred to a dedicated
/// worker, or from a dedicated worker to its window): the media and the state of the source are posted by the
/// <see cref="MediaStreamTrackRelay"/> that runs on the event loop of the source, and the source stops when the last
/// track of this source stops. The frames waiting for the event loop of this source are bounded: a frame replaces the
/// one that was not delivered yet.
/// </summary>
internal sealed class RemoteTrackSource : PushTrackSource
{
    private const int MaxPendingAudioData = 100;

    private readonly GlobalScope _global;
    private readonly MediaStreamTrackRelay? _relay;
    private readonly Lock _lock = new();
    private readonly Queue<AudioData> _pendingAudio = new();
    private VideoFrame? _pendingFrame;
    private bool _flushQueued;

    public RemoteTrackSource(GlobalScope global, MediaStreamTrackRelay? relay, MediaTrackKinds kind, string label, bool muted)
        : base(kind, label)
    {
        _global = global;
        _relay = relay;

        // The received track has the muted state of the transferred track
        if (muted)
        {
            SetMuted(muted: true);
        }
    }

    /// <summary>The event loop of the tracks of the source.</summary>
    public DomEventLoop EventLoop => _global.EventLoop;

    /// <summary>Posts a frame of the source (from the thread of the relay).</summary>
    public void PostVideoFrame(VideoFrame frame)
    {
        lock (_lock)
        {
            _pendingFrame?.Close();
            _pendingFrame = frame;
            QueueFlushLocked();
        }
    }

    /// <summary>Posts audio samples of the source (from the thread of the relay).</summary>
    public void PostAudioData(AudioData data)
    {
        lock (_lock)
        {
            if (_pendingAudio.Count >= MaxPendingAudioData)
            {
                _pendingAudio.Dequeue().Close();
            }

            _pendingAudio.Enqueue(data);
            QueueFlushLocked();
        }
    }

    /// <summary>Posts the muted state of the source (from the thread of the relay).</summary>
    public void PostMuted(bool muted) => Queue(() => SetMuted(muted));

    /// <summary>The source ended (from the thread of the relay): the tracks end and fire <c>ended</c>.</summary>
    public void PostEnded() => Queue(EndAllTracks);

    private void Queue(Action action) => _global.EventLoop.QueueTask(() => _global.RunGlobalTask(action));

    private void QueueFlushLocked()
    {
        if (_flushQueued)
            return;

        _flushQueued = true;
        Queue(Flush);
    }

    private void Flush()
    {
        VideoFrame? frame;
        AudioData[] audio;
        lock (_lock)
        {
            _flushQueued = false;
            frame = _pendingFrame;
            _pendingFrame = null;
            audio = [.. _pendingAudio];
            _pendingAudio.Clear();
        }

        foreach (var data in audio)
        {
            PresentAudioData(data);
        }

        if (frame is not null)
        {
            PresentVideoFrame(frame);
        }
    }

    protected override void OnStopped()
    {
        base.OnStopped();
        lock (_lock)
        {
            _pendingFrame?.Close();
            _pendingFrame = null;
            while (_pendingAudio.TryDequeue(out var data))
            {
                data.Close();
            }
        }

        _relay?.RequestStop();
    }
}
